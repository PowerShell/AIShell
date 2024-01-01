using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Security;
using System.Security.AccessControl;
using System.Security.Principal;

namespace ShellCopilot.Kernel;

internal sealed class Disposable : IDisposable
{
    private Action m_onDispose;

    internal static readonly Disposable NonOp = new();

    private Disposable()
    {
        m_onDispose = null;
    }

    public Disposable(Action onDispose)
    {
        m_onDispose = onDispose ?? throw new ArgumentNullException(nameof(onDispose));
    }

    public void Dispose()
    {
        if (m_onDispose != null)
        {
            m_onDispose();
            m_onDispose = null;
        }
    }
}

internal static class Utils
{
    internal const int InvalidProcessId = -1;
    internal const string AppName = "aish";

    internal const string ApimAuthorizationHeader = "Ocp-Apim-Subscription-Key";
    internal const string ApimGatewayDomain = ".azure-api.net";
    internal const string AzureOpenAIDomain = ".openai.azure.com";

    internal const string ShellCopilotEndpoint = "https://pscopilot.azure-api.net";
    internal const string KeyApplicationHelpLink = "https://github.com/PowerShell/ShellCopilot#readme";

    internal static readonly string OS;
    internal static readonly string ShellConfigHome;
    internal static readonly string AgentHome;
    internal static readonly string AgentConfigHome;

    private static int? s_parentProcessId;

    static Utils()
    {
        string rid = RuntimeInformation.RuntimeIdentifier;
        int index = rid.IndexOf('-');
        OS = index is -1 ? rid : rid[..index];

        string locationPath = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)
            : Environment.GetEnvironmentVariable("HOME");

        ShellConfigHome = Path.Combine(locationPath, AppName);
        AgentHome = Path.Join(ShellConfigHome, "agents");
        AgentConfigHome = Path.Join(ShellConfigHome, "agent-config");

        // Create the folders if they don't exist.
        CreateFolderWithRightPermission(ShellConfigHome);
        Directory.CreateDirectory(AgentHome);
        Directory.CreateDirectory(AgentConfigHome);
    }

    private static void CreateFolderWithRightPermission(string dirPath)
    {
        if (Directory.Exists(dirPath))
        {
            return;
        }

        Directory.CreateDirectory(dirPath);
        if (OperatingSystem.IsWindows())
        {
            // Windows platform.
            // For Windows, file permissions are set to FullAccess for current user account only.
            // SetAccessRule method applies to this directory.
            var dirSecurity = new DirectorySecurity();
            dirSecurity.SetAccessRule(
                new FileSystemAccessRule(
                    identity: WindowsIdentity.GetCurrent().User,
                    type: AccessControlType.Allow,
                    fileSystemRights: FileSystemRights.FullControl,
                    inheritanceFlags: InheritanceFlags.None,
                    propagationFlags: PropagationFlags.None));

            // AddAccessRule method applies to child directories and files.
            dirSecurity.AddAccessRule(
                new FileSystemAccessRule(
                identity: WindowsIdentity.GetCurrent().User,
                fileSystemRights: FileSystemRights.FullControl,
                type: AccessControlType.Allow,
                inheritanceFlags: InheritanceFlags.ObjectInherit | InheritanceFlags.ContainerInherit,
                propagationFlags: PropagationFlags.InheritOnly));

            // Set access rule protections.
            dirSecurity.SetAccessRuleProtection(
                isProtected: true,
                preserveInheritance: false);

            // Set directory owner.
            dirSecurity.SetOwner(WindowsIdentity.GetCurrent().User);

            // Apply new rules.
            FileSystemAclExtensions.SetAccessControl(
                directoryInfo: new DirectoryInfo(dirPath),
                directorySecurity: dirSecurity);
        }
        else
        {
            // On non-Windows platforms, set directory permissions to current user only.
            //   Current user is user owner.
            //   Current user is group owner.
            //   Permission for user dir owner:      rwx    (execute for directories only)
            //   Permission for user file owner:     rw-    (no file execute)
            //   Permissions for group owner:        ---    (no access)
            //   Permissions for others:             ---    (no access)
            string argument = string.Format(CultureInfo.InvariantCulture, @"u=rwx,g=---,o=--- {0}", dirPath);
            ProcessStartInfo startInfo = new("chmod", argument);
            Process.Start(startInfo).WaitForExit();
        }
    }

    internal static SecureString ConvertToSecureString(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        var ss = new SecureString();
        foreach (char c in text)
        {
            ss.AppendChar(c);
        }

        return ss;
    }

    internal static int GetParentProcessId()
    {
        if (!s_parentProcessId.HasValue)
        {
            s_parentProcessId = GetParentProcessId(Process.GetCurrentProcess());
        }

        return s_parentProcessId.Value;
    }

    private static int GetParentProcessId(Process process)
    {
        if (OperatingSystem.IsWindows())
        {
            var res = Interop.Windows.NtQueryInformationProcess(
                process.Handle,
                processInformationClass: 0,
                processInformation: out Interop.Windows.PROCESS_BASIC_INFORMATION pbi,
                processInformationLength: Marshal.SizeOf<Interop.Windows.PROCESS_BASIC_INFORMATION>(),
                returnLength: out _);

            return res is 0 ? pbi.InheritedFromUniqueProcessId.ToInt32() : InvalidProcessId;
        }
        else if (OperatingSystem.IsMacOS())
        {
            return Interop.MacOS.GetPPid(process.Id);
        }
        else if (OperatingSystem.IsLinux())
        {
            // Read '/proc/<pid>/status' for the row beginning with 'PPid:', which is the parent process id.
            // We could check '/proc/<pid>/stat', but although that file was meant to be a space delimited line,
            // it contains a value which could contain spaces itself.
            // Using the 'status' file is a lot simpler because each line contains a record with a simple label.
            // https://github.com/PowerShell/PowerShell/issues/17541#issuecomment-1159911577
            var path = $"/proc/{process.Id}/status";
            try
            {
                string line = null;
                using StreamReader sr = new(path);

                while ((line = sr.ReadLine()) is not null)
                {
                    if (!line.StartsWith("PPid:\t", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string[] lineSplit = line.Split(
                        separator: '\t',
                        count: 2,
                        options: StringSplitOptions.RemoveEmptyEntries);

                    if (lineSplit.Length is not 2)
                    {
                        continue;
                    }

                    if (int.TryParse(lineSplit[1].Trim(), out int ppid))
                    {
                        return ppid;
                    }
                }
            }
            catch (Exception)
            {
                // Ignore exception thrown from reading the proc file.
            }
        }

        return InvalidProcessId;
    }
}

internal static partial class Interop
{
    internal static unsafe partial class Windows
    {
        [StructLayout(LayoutKind.Sequential)]
        internal struct PROCESS_BASIC_INFORMATION
        {
            public nint ExitStatus;
            public nint PebBaseAddress;
            public nint AffinityMask;
            public nint BasePriority;
            public nint UniqueProcessId;
            public nint InheritedFromUniqueProcessId;
        }

        [LibraryImport("ntdll.dll")]
        internal static partial int NtQueryInformationProcess(
                nint processHandle,
                int processInformationClass,
                out PROCESS_BASIC_INFORMATION processInformation,
                int processInformationLength,
                out int returnLength);
    }

    internal static unsafe partial class MacOS
    {
        [LibraryImport("libpsl-native")]
        internal static partial int GetPPid(int pid);
    }
}
