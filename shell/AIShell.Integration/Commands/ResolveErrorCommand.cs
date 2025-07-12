using System.Collections;
using System.Text;
using System.Management.Automation;
using System.Management.Automation.Host;
using Microsoft.PowerShell.Commands;
using AIShell.Abstraction;

namespace AIShell.Integration.Commands;

[Alias("fixit")]
[Cmdlet(VerbsDiagnostic.Resolve, "Error")]
public class ResolveErrorCommand : PSCmdlet
{
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string Agent { get; set; }

    [Parameter]
    public SwitchParameter IncludeOutputFromClipboard { get; set; }

    protected override void EndProcessing()
    {
        using var pwsh = PowerShell.Create(RunspaceMode.CurrentRunspace);
        var results = pwsh
            .AddCommand("Get-History")
            .AddParameter("Count", 1)
            .Invoke<HistoryInfo>();

        if (results.Count is 0)
        {
            WriteWarning("No error to resolve. No command line has been executed yet.");
            return;
        }

        HistoryInfo lastHistory = results[0];
        ArrayList errors = (ArrayList)GetVariableValue("Error");
        bool questionMarkValue = (bool)GetVariableValue("?");
        object value = GetVariableValue("LASTEXITCODE");
        int lastExitCode = value is null ? 0 : (int)value;

        ErrorRecord lastError = null;
        bool useLastError = false;

        if (errors.Count > 0)
        {
            object last = errors[0];
            lastError = last switch
            {
                ParseException pe => pe.ErrorRecord,
                ErrorRecord er => er,
                _ => throw new NotSupportedException($"Unexpected type of object '{last.GetType().FullName}' is found in '$Error'.")
            };

            // Use the last error for troubleshooting when
            //  - last error maps to the last history command, OR
            //  - they don't map but 'LastExitCode' is 0 (meaning no native command failure).
            useLastError = lastError.InvocationInfo.HistoryId == lastHistory.Id || lastExitCode is 0;
        }

        string query = null;
        Channel channel = Channel.Singleton;
        string commandLine = lastHistory.CommandLine;

        if (useLastError)
        {
            query = ErrorFeedback.CreateQueryForError(lastError);
        }
        else if (lastExitCode is 0)
        {
            // $Error is empty and no failure from native command execution.
            WriteWarning("Cannot find an error to resolve.");
            return;
        }
        else if (!questionMarkValue)
        {
            // In this scenario, we have:
            //  - Last error doesn't map to the last history command;
            //  - $LastExitCode is non-zero;
            //  - $? is false.
            // It indicates the last command is a native command and it failed.
            string output = ScrapeScreenForNativeCommandOutput(commandLine);

            if (output is null)
            {
                if (UseClipboardForCommandOutput())
                {
                    pwsh.Commands.Clear();
                    output = pwsh
                        .AddCommand("Get-Clipboard")
                        .AddParameter("Raw")
                        .Invoke<string>()
                        .FirstOrDefault();
                }
                else
                {
                    ThrowTerminatingError(new(
                        new NotSupportedException($"The output content is needed for suggestions on native executable failures."),
                        errorId: "OutputNeededForNativeCommand",
                        ErrorCategory.InvalidData,
                        targetObject: null
                    ));
                }
            }

            query = $"""
                Please troubleshoot the command-line error that happend in the connected PowerShell session, and suggest the fix. The error details are given below:

                ---

                ## Command Line
                ```
                {commandLine}
                ```

                ## Error Output
                ```
                {output}
                ```
                """;
        }
        else
        {
            // When reaching here, we have
            //  - Last error doesn't map to the last history command, or $Error is empty;
            //  - $LastExitCode is non-zero;
            //  - $? is true.
            // The user may want to fix a command that failed previously, but it's unknown whether
            // that was a native command failure or PowerShell command failure.
            query = "Take a look at the terminal output of the connected PowerShell session and try resolving the last error you see.";
        }

        channel.PostQuery(new PostQueryMessage(query, context: null, Agent));
    }

    private bool UseClipboardForCommandOutput()
    {
        if (IncludeOutputFromClipboard)
        {
            return true;
        }

        string query = "The last failed command is a native command that did not produce an ErrorRecord object.\nPlease \x1b[93mcopy its output and then press 'y'\x1b[0m to allow using the output as context information.";
        return ShouldContinue(query, caption: "Include output from the clipboard");
    }

    private string ScrapeScreenForNativeCommandOutput(string lastCommandLine)
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            PSHostRawUserInterface rawUI = Host.UI.RawUI;
            Coordinates start = new(0, 0), end = rawUI.CursorPosition;

            string currentCommandLine = MyInvocation.Line;
            end.X = rawUI.BufferSize.Width - 1;

            BufferCell[,] content = rawUI.GetBufferContents(new Rectangle(start, end));
            StringBuilder line = new(), buffer = new();

            bool collect = false;
            int rows = content.GetLength(0);
            int columns = content.GetLength(1);

            for (int row = 0; row < rows; row++)
            {
                line.Clear();
                for (int column = 0; column < columns; column++)
                {
                    line.Append(content[row, column].Character);
                }

                string lineStr = line.ToString().TrimEnd();
                if (!collect && IsStartOfCommand(lineStr, columns, lastCommandLine))
                {
                    collect = true;
                    buffer.Append(lineStr);
                    continue;
                }

                if (collect)
                {
                    // The current command line is just `Resolve-Error` or `fixit`, which should be on the same line
                    // and thus there is no need to check for the span-to-the-next-line case.
                    if (lineStr.EndsWith(currentCommandLine, StringComparison.Ordinal))
                    {
                        break;
                    }

                    buffer.Append('\n').Append(lineStr);
                }
            }

            return buffer.Length is 0 ? null : buffer.ToString();
        }
        catch
        {
            return null;
        }

        static bool IsStartOfCommand(string lineStr, int columns, string commandLine)
        {
            if (lineStr.EndsWith(commandLine, StringComparison.Ordinal))
            {
                return true;
            }

            // Handle the case where the command line is too long and spans to the next line on screen,
            // like az, gcloud, and aws CLI commands which are usually long with many parameters.
            if (columns - lineStr.Length > 3 || commandLine.Length < 20)
            {
                // The line on screen unlikely spanned to the next line in this case.
                return false;
            }

            // We check if the prefix of the command line is the suffix of the current line on screen.
            ReadOnlySpan<char> lineStrSpan = lineStr.AsSpan();
            ReadOnlySpan<char> cmdLineSpan = commandLine.AsSpan();

            // We assume the first 20 chars of the command line should be in the current line on screen.
            // This assumption is not perfect but practically good enough.
            int index = lineStrSpan.IndexOf(cmdLineSpan[..20], StringComparison.Ordinal);
            if (index >= 0 && cmdLineSpan.StartsWith(lineStrSpan[index..], StringComparison.Ordinal))
            {
                return true;
            }

            return false;
        }
    }
}
