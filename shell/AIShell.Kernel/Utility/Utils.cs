using System.Diagnostics;
using System.Globalization;
using System.Runtime;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.PowerShell;
using AIShell.Abstraction;

namespace AIShell.Kernel;

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
    internal const string DefaultAppName = "aish";
    internal const string DefaultPrompt = "aish";

    internal static string AppName;
    internal static string ConfigHome;
    internal static string AppCacheDir;
    internal static string AppConfigFile;
    internal static string AgentHome;
    internal static string AgentConfigHome;

    internal static void Setup(string appName)
    {
        string locationPath = OperatingSystem.IsWindows()
            ? Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            : Environment.GetEnvironmentVariable("HOME");

        AppName = appName?.Trim().ToLower() ?? DefaultAppName;
        ConfigHome = Path.Combine(locationPath, $".{AppName.Replace(' ', '-')}");
        AppCacheDir = Path.Combine(ConfigHome, ".cache");
        AppConfigFile = Path.Combine(ConfigHome, "config.json");
        AgentHome = Path.Join(ConfigHome, "agents");
        AgentConfigHome = Path.Join(ConfigHome, "agent-config");

        // Create the folders if they don't exist.
        CreateFolderWithRightPermission(ConfigHome);
        Directory.CreateDirectory(AppCacheDir);
        Directory.CreateDirectory(AgentHome);
        Directory.CreateDirectory(AgentConfigHome);

        // Enable optimization profiling to load assemblies in parallel if possible.
        ProfileOptimization.SetProfileRoot(AppCacheDir);
        ProfileOptimization.StartProfile("StartupProfileData");
    }

    internal static JsonSerializerOptions GetJsonSerializerOptions()
    {
        return new JsonSerializerOptions
        {
            AllowTrailingCommas = true,
            PropertyNameCaseInsensitive = true,
            ReadCommentHandling = JsonCommentHandling.Skip,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
        };
    }

    /// <summary>
    /// Check if the <paramref name="left"/> contains <paramref name="right"/> regardless of
    /// the leading and trailing space characters on each line if both are multi-line strings.
    /// </summary>
    internal static bool Contains(string left, string right)
    {
        ReadOnlySpan<char> leftSpan = left.AsSpan();
        if (leftSpan.IndexOf('\n') is -1)
        {
            // The 'left' is not a multi-line string, then a direct 'Contains' call is enough.
            return leftSpan.Contains(right.AsSpan().Trim(), StringComparison.Ordinal);
        }

        // The 'left' is a multi-line string. If the 'right' is also a multi-line string,
        // we want to check line by line regardless the leading and trailing space chars
        // on each line.
        int start, index = -1;
        while (true)
        {
            start = index + 1;
            if (start == right.Length)
            {
                break;
            }

            index = right.IndexOf('\n', start);
            if (index is -1)
            {
                return leftSpan.Contains(right.AsSpan(start).Trim(), StringComparison.Ordinal);
            }

            if (!leftSpan.Contains(right.AsSpan(start, index - start).Trim(), StringComparison.Ordinal))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Extract code blocks from the passed-in text.
    /// </summary>
    internal static List<CodeBlock> ExtractCodeBlocks(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        int start, index = -1;
        bool inCodeBlock = false;
        string language = null;
        StringBuilder code = null;
        List<CodeBlock> codeBlocks = null;

        do
        {
            start = index + 1;
            if (start == text.Length)
            {
                // Break out if we already reached end of the text.
                break;
            }

            index = text.IndexOf('\n', start);
            ReadOnlySpan<char> line = index is -1
                ? text.AsSpan(start)
                : text.AsSpan(start, index - start + 1);

            // Trim the line before checking for code fence.
            ReadOnlySpan<char> lineTrimmed = line.Trim();
            if (lineTrimmed.StartsWith("```"))
            {
                if (inCodeBlock)
                {
                    if (lineTrimmed.Length is 3)
                    {
                        // Current line is the ending code fence.
                        codeBlocks.Add(new CodeBlock(code.ToString(), language));

                        code.Clear();
                        language = null;
                        inCodeBlock = false;
                        continue;
                    }

                    // It's not the ending code fence, so keep appending to code.
                    code.Append(line);
                }
                else
                {
                    // Current line is the starting code fence.
                    code ??= new StringBuilder();
                    codeBlocks ??= [];
                    inCodeBlock = true;
                    language = lineTrimmed.Length > 3 ? lineTrimmed[3..].ToString() : null;
                }

                continue;
            }

            if (inCodeBlock)
            {
                // Append the line when we are within a code block.
                code.Append(line);
            }
        }
        while (index is not -1);

        if (inCodeBlock && code.Length > 0)
        {
            // It's possbile that the ending code fence is missing.
            codeBlocks.Add(new CodeBlock(code.ToString(), language));
        }

        return codeBlocks;
    }

    internal static void SetDefaultKeyHandlers()
    {
        string[] englishNumbers = ["One", "Two", "Three", "Four", "Five", "Six", "Seven", "Eight", "Nine"];
        string[] ordinalNumbers = ["1st", "2nd", "3rd", "4th", "5th", "6th", "7th", "8th", "9th"];

        PSConsoleReadLine.SetKeyHandler(
            ["Ctrl+d,Ctrl+c"],
            (key, arg) =>
            {
                PSConsoleReadLine.RevertLine();
                PSConsoleReadLine.Insert("/code copy");
                PSConsoleReadLine.AcceptLine();
            },
            "CopyCodeAll",
            "Copy all the code snippets from the last response to clipboard.");

        // Setup hot keys for copying a specific code snippet.
        for (int i = 1; i < 10; i++)
        {
            PSConsoleReadLine.SetKeyHandler(
                [$"Ctrl+{i}"],
                CopyCodeSnippet,
                $"CopyCode{englishNumbers[i-1]}",
                $"Copy the {ordinalNumbers[i-1]} code snippet from the last response to clipboard.");
        }

        PSConsoleReadLine.SetKeyHandler(
            ["Ctrl+d,Ctrl+d"],
            (key, arg) =>
            {
                PSConsoleReadLine.RevertLine();
                PSConsoleReadLine.Insert("/code post");
                PSConsoleReadLine.AcceptLine();
            },
            "PostCodeAll",
            "Post all the code snippets from the last response to the connected PowerShell session.");

        // Setup hot keys for posting a specific code snippet.
        for (int i = 1; i < 10; i++)
        {
            PSConsoleReadLine.SetKeyHandler(
                [$"Ctrl+d,{i}"],
                PostCodeSnippet,
                $"PostCode{englishNumbers[i-1]}",
                $"Post the {ordinalNumbers[i-1]} code snippet from the last response to the connected PowerShell session.");
        }

        static void CopyCodeSnippet(ConsoleKeyInfo? key, object arg)
        {
            PSConsoleReadLine.RevertLine();
            PSConsoleReadLine.Insert($"/code copy {key?.KeyChar}");
            PSConsoleReadLine.AcceptLine();
        }

        static void PostCodeSnippet(ConsoleKeyInfo? key, object arg)
        {
            PSConsoleReadLine.RevertLine();
            PSConsoleReadLine.Insert($"/code post {key?.KeyChar}");
            PSConsoleReadLine.AcceptLine();
        }
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
}
