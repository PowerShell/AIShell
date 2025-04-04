using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Management.Automation;

namespace AIShell.Integration;

public class InitAndCleanup : IModuleAssemblyInitializer, IModuleAssemblyCleanup
{
    private const int ScriptVersion = 1;
    private const string ScriptFileTemplate = "aish_split_pane_v{0}.py";
    private const string SplitPanePythonCode = """
        import iterm2
        import sys

        # iTerm needs to be running for this to work
        async def main(connection):
            app = await iterm2.async_get_app(connection)

            # Foreground the app
            await app.async_activate()

            window = app.current_terminal_window
            if window is not None:
                # Get the current pane so that we can split it.
                current_tab = window.current_tab
                current_pane = current_tab.current_session

                # Get the total width before splitting.
                width = current_pane.grid_size.width

                change = iterm2.LocalWriteOnlyProfile()
                change.set_use_custom_command('Yes')
                change.set_command(f'{app_path} --channel {channel}')

                # Split pane vertically
                split_pane = await current_pane.async_split_pane(vertical=True, profile_customizations=change)

                # Get the height of the pane after splitting. This value will be
                # slightly smaller than its height before splitting.
                height = current_pane.grid_size.height

                # Calculate the new width for both panes using the ratio 0.4 for the new pane.
                # Then set the preferred size for both pane sessions.
                new_current_width = round(width * 0.6);
                new_split_width = width - new_current_width;
                current_pane.preferred_size = iterm2.Size(new_current_width, height)
                split_pane.preferred_size = iterm2.Size(new_split_width, height);

                # Update the layout, which will change the panes to preferred size.
                await current_tab.async_update_layout()
            else:
                # You can view this message in the script console.
                print("No current iTerm2 window. Make sure you are running in iTerm2.")

        if len(sys.argv) > 1:
            app_path = sys.argv[1]
            channel = sys.argv[2]

            # Do not specify True for retry. It's possible that the user hasn't enable the Python API for iTerm2,
            # and in that case, we want it to fail immediately instead of stucking in retries.
            iterm2.run_until_complete(main)
        else:
            print("Please provide the application path as a command line argument.")
        """;

    internal static string CachePath { get; }
    internal static string PythonScript { get; }
    internal static string VirtualEnvPath { get; }
    internal static Task CreateVirtualEnvTask { get; }

    static InitAndCleanup()
    {
        CachePath = Path.Join(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".aish", ".cache");
        PythonScript = null;
        VirtualEnvPath = null;
        CreateVirtualEnvTask = null;

        if (OperatingSystem.IsMacOS())
        {
            PythonScript = Path.Join(CachePath, string.Format(CultureInfo.InvariantCulture, ScriptFileTemplate, ScriptVersion));
            VirtualEnvPath = Path.Join(CachePath, ".venv");
            CreateVirtualEnvTask = Task.Run(CreatePythonVirtualEnvironment);
        }
    }

    private static void CreatePythonVirtualEnvironment()
    {
        // Simply return if the virtual environment was already created.
        if (Directory.Exists(VirtualEnvPath))
        {
            return;
        }

        // Create a virtual environment where we can install the needed pacakges.
        ProcessStartInfo startInfo = new("python3")
        {
            ArgumentList = { "-m", "venv", VirtualEnvPath },
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };

        Process proc = Process.Start(startInfo);
        proc.WaitForExit();

        if (proc.ExitCode is 1)
        {
            string error = $"Failed to create a virtual environment by 'python3 -m venv {VirtualEnvPath}'.";
            string stderr = proc.StandardError.ReadToEnd();
            if (!string.IsNullOrEmpty(stderr))
            {
                error = $"{error}\nError details:\n{stderr}";
            }

            throw new NotSupportedException(error);
        }

        proc.Dispose();
    }

    public void OnImport()
    {
        if (!OperatingSystem.IsMacOS())
        {
            return;
        }

        // Remove old scripts, if there is any.
        for (int i = 1; i < ScriptVersion; i++)
        {
            string oldScript = Path.Join(CachePath, string.Format(CultureInfo.InvariantCulture, ScriptFileTemplate, i));
            if (File.Exists(oldScript))
            {
                File.Delete(oldScript);
            }
        }

        // Create the latest script, if not yet.
        if (!File.Exists(PythonScript))
        {
            File.WriteAllText(PythonScript, SplitPanePythonCode, Encoding.UTF8);
        }
    }

    public void OnRemove(PSModuleInfo psModuleInfo)
    {
        Channel.Singleton?.Dispose();
    }
}
