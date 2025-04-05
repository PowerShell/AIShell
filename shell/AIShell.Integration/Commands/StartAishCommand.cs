using System.Diagnostics;
using System.Management.Automation;

namespace AIShell.Integration.Commands;

[Alias("aish")]
[Cmdlet(VerbsLifecycle.Start, "AIShell")]
public class StartAIShellCommand : PSCmdlet
{
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; }

    private string _venvPipPath;
    private string _venvPythonPath;
    private static bool s_iterm2Installed = false;

    protected override void BeginProcessing()
    {
        if (Path is null)
        {
            var app = SessionState.InvokeCommand.GetCommand("aish", CommandTypes.Application);
            if (app is null)
            {
                ThrowTerminatingError(new(
                    new NotSupportedException("The executable 'aish' cannot be found."),
                    "AIShellMissing",
                    ErrorCategory.NotInstalled,
                    targetObject: null));
            }

            Path = app.Source;
        }
        else
        {
            var paths = GetResolvedProviderPathFromPSPath(Path, out _);
            if (paths.Count > 1)
            {
                ThrowTerminatingError(new(
                    new ArgumentException("Specified path is ambiguous as it's resolved to more than one paths."),
                    "InvalidPath",
                    ErrorCategory.InvalidArgument,
                    targetObject: null
                ));
            }

            Path = paths[0];
        }

        if (OperatingSystem.IsWindows())
        {
            // Validate if Windows Terminal is installed.
            var wtExe = SessionState.InvokeCommand.GetCommand("wt", CommandTypes.Application);
            if (wtExe is null)
            {
                ThrowTerminatingError(new(
                    new NotSupportedException("The executable 'wt' (Windows Terminal) cannot be found."),
                    "WindowsTerminalMissing",
                    ErrorCategory.NotInstalled,
                    targetObject: null));
            }

            // Validate if Windows Terminal is running, and assuming we are running in WT if the process exists.
            Process[] ps = Process.GetProcessesByName("WindowsTerminal");
            if (ps.Length is 0)
            {
                ThrowTerminatingError(new(
                    new NotSupportedException("The 'WindowsTerminal' process is not found. Please make sure running this cmdlet from within Windows Terminal."),
                    "NotInWindowsTerminal",
                    ErrorCategory.InvalidOperation,
                    targetObject: null));
            }
        }
        else if (OperatingSystem.IsMacOS())
        {
            string term = Environment.GetEnvironmentVariable("TERM_PROGRAM");
            if (!string.Equals(term, "iTerm.app", StringComparison.Ordinal))
            {
                ThrowTerminatingError(new(
                    new NotSupportedException("The environment variable 'TERM_PROGRAM' is missing or its value is not 'iTerm.app'. Please make sure running this cmdlet from within iTerm2."),
                    "NotIniTerm2",
                    ErrorCategory.InvalidOperation,
                    targetObject: null));
            }

            try
            {
                InitAndCleanup.CreateVirtualEnvTask.GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                ThrowTerminatingError(new(
                    exception,
                    "FailedToCreateVirtualEnvironment",
                    ErrorCategory.InvalidOperation,
                    targetObject: null));
            }

            _venvPipPath = System.IO.Path.Join(InitAndCleanup.VirtualEnvPath, "bin", "pip3");
            _venvPythonPath = System.IO.Path.Join(InitAndCleanup.VirtualEnvPath, "bin", "python3");
        }
        else
        {
            ThrowTerminatingError(new(
                new NotSupportedException("This platform is not yet supported."),
                "PlatformNotSupported",
                ErrorCategory.NotEnabled,
                targetObject: null));
        }
    }

    protected override void EndProcessing()
    {
        if (OperatingSystem.IsWindows())
        {
            ProcessStartInfo startInfo;
            string wtProfileGuid = Environment.GetEnvironmentVariable("WT_PROFILE_ID");
            string pipeName = Channel.Singleton.StartChannelSetup();

            if (wtProfileGuid is null)
            {
                // We may be running in a WT that was started by OS as the default terminal.
                // In this case, we don't specify the '-p' option.
                startInfo = new("wt")
                {
                    ArgumentList = {
                        "-w",
                        "0",
                        "sp",
                        "--tabColor",
                        "#345beb",
                        "-s",
                        "0.4",
                        "--title",
                        "AIShell",
                        Path,
                        "--channel",
                        pipeName
                    },
                };
            }
            else
            {
                // Specify the '-p' option to use the same profile.
                startInfo = new("wt")
                {
                    ArgumentList = {
                        "-w",
                        "0",
                        "sp",
                        "--tabColor",
                        "#345beb",
                        "-p",
                        wtProfileGuid,
                        "-s",
                        "0.4",
                        "--title",
                        "AIShell",
                        Path,
                        "--channel",
                        pipeName
                    },
                };
            }

            Process.Start(startInfo);
        }
        else if (OperatingSystem.IsMacOS())
        {
            Process proc;
            ProcessStartInfo startInfo;

            // Install the Python package 'iterm2' to the venv.
            if (!s_iterm2Installed)
            {
                startInfo = new(_venvPipPath)
                {
                    // Make 'pypi.org' and 'files.pythonhosted.org' as trusted hosts, because a security software
                    // may cause issue to SSL validation for access to/from those two endpoints.
                    // See https://stackoverflow.com/a/71993364 for details.
                    ArgumentList = {
                        "install",
                        "-q",
                        "--disable-pip-version-check",
                        "--trusted-host",
                        "pypi.org",
                        "--trusted-host",
                        "files.pythonhosted.org",
                        "iterm2"
                    },
                    RedirectStandardError = true,
                    RedirectStandardOutput = true
                };

                proc = Process.Start(startInfo);
                proc.WaitForExit();

                if (proc.ExitCode is 0)
                {
                    s_iterm2Installed = true;
                }
                else
                {
                    string error = "The Python package 'iterm2' cannot be installed. It's required to split a pane in iTerm2 programmatically.";
                    string stderr = proc.StandardError.ReadToEnd();
                    if (!string.IsNullOrEmpty(stderr))
                    {
                        error = $"{error}\nError details:\n{stderr}";
                    }

                    ThrowTerminatingError(new(
                        new NotSupportedException(error),
                        "iterm2Missing",
                        ErrorCategory.NotInstalled,
                        targetObject: null));
                }

                proc.Dispose();
            }

            // Run the Python script to split the pane and start AIShell.
            string pipeName = Channel.Singleton.StartChannelSetup();
            startInfo = new(_venvPythonPath) { ArgumentList = { InitAndCleanup.PythonScript, Path, pipeName } };
            proc = Process.Start(startInfo);
            proc.WaitForExit();
        }
    }
}
