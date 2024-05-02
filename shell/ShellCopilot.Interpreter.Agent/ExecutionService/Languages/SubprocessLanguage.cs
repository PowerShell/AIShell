﻿using System.Diagnostics;
using System.Text;
using System.Runtime.InteropServices;

namespace ShellCopilot.Interpreter.Agent;

/// <summary>
/// This is the parent class for all languages.
/// </summary>
internal abstract class SubprocessLanguage : IDisposable
{
    protected Process Process { get; set; }

    /// <summary>
    /// The command to start the process. This is an array of strings where the first element is the program
    /// to run and the second element is the arguments to pass to the program.
    /// </summary>
    protected string[] StartCmd;

    /// <summary>
    /// The command to get the version of the language.
    /// </summary>
    protected string[] VersionCmd;

    /// <summary>
    /// This event is used to signal when the process has finished running.
    /// </summary>
    protected readonly ManualResetEventSlim DoneExeuctionEvent;

    /// <summary>
    /// The queue to store the output of code processes.
    /// </summary>
    protected readonly Queue<OutputData> OutputQueue;

    /// <summary>
    /// Preprocesses the code before running it wraps code in a try catch block and adds an end of execution marker.
    /// </summary>
    /// <param name="code"></param>
    /// <returns></returns>
    protected abstract string PreprocessCode(string code);

    protected SubprocessLanguage()
    {
        OutputQueue = new();
        DoneExeuctionEvent = new(false);
    }

    /// <summary>
    /// Gets version of the language executable on the user's local machine.
    /// </summary>
    public async Task<string> GetVersion()
    {
        // Get the version of the executable
        // Separate process needed to get version of executable because of different starting arguments.
        ProcessStartInfo startInfo = new()
        {
            FileName = VersionCmd[0],
            Arguments = VersionCmd[1],
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };

        Process VersionProcess = new Process { StartInfo = startInfo };

        VersionProcess.Start();

        string version = await VersionProcess.StandardOutput.ReadToEndAsync();

        VersionProcess.WaitForExit();
        VersionProcess.Dispose();

        return version;
    }

    /// <summary>
    /// Assigns process with a new process if possible.
    /// </summary>
    protected void StartLangServer()
    {
        ProcessStartInfo startInfo = new()
        {
            FileName = StartCmd[0],
            Arguments = StartCmd[1],
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            Environment = { ["NO_COLOR"] = "1", ["__SuppressAnsiEscapeSequences"] = "1" },
        };

        Process = new Process { StartInfo = startInfo };

        Process.OutputDataReceived += HandleStandardOutput;
        Process.ErrorDataReceived += HandleStandardError;

        Process.Start();

        Process.BeginOutputReadLine();
        Process.BeginErrorReadLine();
    }

    /// <summary>
    /// Runs the code and returns the output in a DataPacket.
    /// </summary>
    /// <param name="code"></param>
    /// <returns></returns>
    public async Task<Queue<OutputData>> Run(string code, CancellationToken token)
    {
        OutputQueue.Clear();

        string processedCode;

        try
        {
            processedCode = PreprocessCode(code);
            if(Process is null)
            {
                StartLangServer();
            }
        }
        catch(Exception e)
        {
            OutputQueue.Enqueue(new OutputData (OutputType.Error, "Error starting process\n" + e));
            return OutputQueue;
        }

        // Reset the event so we can wait for the process to finish
        DoneExeuctionEvent.Reset();

        try
        {
            WriteToProcess(processedCode);
        }
        catch(Exception e)
        {
            OutputQueue.Enqueue(new OutputData(OutputType.Error, "Error writing to process\n" + e));
            return OutputQueue;
        }

        DoneExeuctionEvent.Wait(token);

        // This is an effort to resolve the race condition between stderr and stdout with the Python Process.
        // Sometimes stdout receives "##end_of_execution_##" before stderr can fully enqueue all errors.
        // The above scenario will modify the OutputQueue during enumeration in "CodeExeuctionService".
        // The 300ms delay should be sufficient to allow stderr to finish enqueuing errors.
        await Task.Delay(300, token);

        return OutputQueue;
    }

    /// <summary>
    /// Ends the process and cleans up any resources.
    /// </summary>
    public void Dispose()
    {
        if(Process != null)
        {
            Process.OutputDataReceived -= HandleStandardOutput;
            Process.ErrorDataReceived -= HandleStandardError;
            Process.Kill();
            Process.Dispose();
            Process = null;
        }
        DoneExeuctionEvent.Dispose();
    }

    /// <summary>
    /// Writes code to the StandardInput of the process
    /// </summary>
    protected void WriteToProcess(string code)
    {
        Process.StandardInput.WriteLine(code);
        Process.StandardInput.Flush();
    }

    /// <summary>
    /// Internal function to handle the standard output of the process.
    /// </summary>
    private void HandleStandardError(object sender, DataReceivedEventArgs e)
    {
        string line = e.Data;
        if (string.IsNullOrEmpty(line))
        {
            return;
        }
        // Pressing CTRL+C in Shell Copilot during python code execution will propogate the command to 
        // the python process and cause a KeyboardInterrupt exception. The KeyboardInterrupt will stop 
        // code execution and print the exception to stderr. Then we will never encounter the end of exeuction
        // marker, "##end_of_execution##" so DoneExecutionEvent is set here instead.
        if (DetectKeyBoardInterrupt(line))
        {
            OutputQueue.Enqueue(new OutputData(OutputType.Interrupt, line));
            DoneExeuctionEvent.Set();
            return;
        }
        OutputQueue.Enqueue(new OutputData(OutputType.Error, line));
    }

    /// <summary>
    /// Internal function to handle the error output of the process.
    /// </summary>
    private void HandleStandardOutput(object sender, DataReceivedEventArgs e)
    {
        string line = e.Data;
        if(string.IsNullOrEmpty(line))
        {
            return;
        }
        if(DetectEndOfExecution(line))
        {
            OutputQueue.Enqueue(new OutputData(OutputType.End, line));
            DoneExeuctionEvent.Set();
            return;
        }
        OutputQueue.Enqueue(new OutputData(OutputType.Output, line));
    }
    
    /// <summary>
    /// Detects if the process was interrupted by a keyboard interrupt for Python.
    /// Overload this method for other languages.
    /// </summary>
    virtual protected bool DetectKeyBoardInterrupt(string line)
    {
        if (line.Contains("KeyboardInterrupt"))
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// All code will be preprocessed to contain the same end of execution marker.
    /// </summary>
    /// <param name="line"></param>
    /// <returns></returns>
    protected bool DetectEndOfExecution(string line)
    {
        return line.Contains("##end_of_execution##");
    }

    /// <summary>
    /// Checks if pwsh.exe or python.exe in on System PATH. Returns false if not found.
    /// </summary>
    public bool IsOnPath()
    {
        var values = Environment.GetEnvironmentVariable("PATH");
        foreach (var path in values.Split(Path.PathSeparator))
        {
            string fullPath = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? Path.Combine(path, StartCmd[0] + ".exe")
                : Path.Combine(path, StartCmd[0]);
            if (File.Exists(fullPath))
            {
                return true;
            }
        }
        return false;
    }
}
