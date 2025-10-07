using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Channel;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.ApplicationInsights.Extensibility.Implementation;
using Microsoft.ApplicationInsights.Metrics;

namespace AIShell.Kernel;

internal class Telemetry
{
    private const string TelemetryFailure = "TELEMETRY_FAILURE";
    private const string DefaultUUID = "a586d96e-f941-406c-b87d-5b67e8bc2fcb";
    private const string MetricNamespace = "aishell.telemetry";

    private static readonly TelemetryClient s_client;
    private static readonly string s_os, s_uniqueId;
    private static readonly MetricIdentifier s_sessionCount, s_queryCount;
    private static readonly HashSet<string> s_knownAgents;

    private static bool s_enabled = false;

    static Telemetry()
    {
        s_enabled = !GetEnvironmentVariableAsBool(
            name: "AISHELL_TELEMETRY_OPTOUT",
            defaultValue: false);

        if (s_enabled)
        {
            var config = TelemetryConfiguration.CreateDefault();
            config.ConnectionString = "InstrumentationKey=b273044e-f4af-4a1d-bb8a-ad1fe7ac4cad;IngestionEndpoint=https://centralus-2.in.applicationinsights.azure.com/;LiveEndpoint=https://centralus.livediagnostics.monitor.azure.com/;ApplicationId=1cccb480-3eff-41a0-baad-906cca2cfadb";
            config.TelemetryChannel.DeveloperMode = false;
            config.TelemetryInitializers.Add(new NameObscurerTelemetryInitializer());

            s_client = new TelemetryClient(config);
            s_uniqueId = GetUniqueIdentifier().ToString();
            s_os = OperatingSystem.IsWindows()
                ? "Windows"
                : OperatingSystem.IsMacOS() ? "macOS" : "Linux";

            s_sessionCount = new MetricIdentifier(MetricNamespace, "SessionCount", "uuid", "os", "standalone");
            s_queryCount = new MetricIdentifier(MetricNamespace, "QueryCount", "uuid", "agent", "remote");
            s_knownAgents = ["openai-gpt", "azure", "interpreter", "ollama", "PhiSilica"];
        }
    }

    /// <summary>
    /// Retrieve the unique identifier from the persisted file, if it doesn't exist create it.
    /// Generate a guid which will be used as the UUID.
    /// </summary>
    /// <returns>A guid which represents the unique identifier.</returns>
    private static Guid GetUniqueIdentifier()
    {
        // Try to get the unique id.
        // If this returns false, we'll create/recreate the 'aishell.uuid' file.
        string uuidPath = Path.Join(Utils.AppCacheDir, "aishell.uuid");
        if (TryGetIdentifier(uuidPath, out Guid id))
        {
            return id;
        }

        try
        {
            // Multiple AIShell processes may (unlikely though) start simultaneously so we need
            // a system-wide way to control access to the file in that rare case.
            using var m = new Mutex(true, "AIShell_CreateUniqueUserId");
            m.WaitOne();
            try
            {
                return CreateUniqueIdAndFile(uuidPath);
            }
            finally
            {
                m.ReleaseMutex();
            }
        }
        catch (Exception)
        {
            // The method 'CreateUniqueIdAndFile' shouldn't throw, but the mutex might.
            // Any problem in generating a uuid will result in no telemetry being sent.
            // Try to send the failure in telemetry without the unique id.
            s_client.GetMetric(TelemetryFailure, "detail").TrackValue(1, "mutex");
        }

        // Something bad happened, turn off telemetry since the unique id wasn't set.
        s_enabled = false;
        return id;
    }

    /// <summary>
    /// Try to read the file and collect the guid.
    /// </summary>
    /// <param name="telemetryFilePath">The path to the telemetry file.</param>
    /// <param name="id">The newly created id.</param>
    /// <returns>The method returns a bool indicating success or failure of creating the id.</returns>
    private static bool TryGetIdentifier(string telemetryFilePath, out Guid id)
    {
        if (File.Exists(telemetryFilePath))
        {
            // attempt to read the persisted identifier
            const int GuidSize = 16;
            byte[] buffer = new byte[GuidSize];
            try
            {
                using FileStream fs = new(telemetryFilePath, FileMode.Open, FileAccess.Read);

                // If the read is invalid, or wrong size, we return it
                int n = fs.Read(buffer, 0, GuidSize);
                if (n is GuidSize)
                {
                    id = new Guid(buffer);
                    if (id != Guid.Empty)
                    {
                        return true;
                    }
                }
            }
            catch
            {
                // something went wrong, the file may not exist or not have enough bytes, so return false
            }
        }

        id = Guid.Empty;
        return false;
    }

    /// <summary>
    /// Try to create a unique identifier and persist it to the telemetry.uuid file.
    /// </summary>
    /// <param name="telemetryFilePath">The path to the persisted telemetry.uuid file.</param>
    /// <returns>The method node id.</returns>
    private static Guid CreateUniqueIdAndFile(string telemetryFilePath)
    {
        // One last attempt to retrieve before creating incase we have a lot of simultaneous entry into the mutex.
        if (TryGetIdentifier(telemetryFilePath, out Guid id))
        {
            return id;
        }

        // The directory may not exist, so attempt to create it
        // CreateDirectory will simply return the directory if exists
        bool attemptFileCreation = true;
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(telemetryFilePath));
        }
        catch
        {
            // There was a problem in creating the directory for the file, do not attempt to create the file.
            // We don't send telemetry here because there are valid reasons for the directory to not exist
            // and not be able to be created.
            attemptFileCreation = false;
        }

        // If we were able to create the directory, try to create the file,
        // if this fails we will send telemetry to indicate this and then use the default identifier.
        if (attemptFileCreation)
        {
            try
            {
                id = Guid.NewGuid();
                File.WriteAllBytes(telemetryFilePath, id.ToByteArray());
                return id;
            }
            catch
            {
                // another bit of telemetry to notify us about a problem with saving the unique id.
                s_client.GetMetric(TelemetryFailure, "detail").TrackValue(1, "saveuuid");
            }
        }

        // all attempts to create an identifier have failed, so use the default node id.
        id = new Guid(DefaultUUID);
        return id;
    }

    /// <summary>
    /// Determine whether the environment variable is set and how.
    /// </summary>
    /// <param name="name">The name of the environment variable.</param>
    /// <param name="defaultValue">If the environment variable is not set, use this as the default value.</param>
    /// <returns>A boolean representing the value of the environment variable.</returns>
    private static bool GetEnvironmentVariableAsBool(string name, bool defaultValue)
    {
        var str = Environment.GetEnvironmentVariable(name);
        if (string.IsNullOrEmpty(str))
        {
            return defaultValue;
        }

        var boolStr = str.AsSpan();
        if (boolStr.Length == 1)
        {
            if (boolStr[0] == '1')
            {
                return true;
            }

            if (boolStr[0] == '0')
            {
                return false;
            }
        }

        if (boolStr.Length == 3 &&
            (boolStr[0] == 'y' || boolStr[0] == 'Y') &&
            (boolStr[1] == 'e' || boolStr[1] == 'E') &&
            (boolStr[2] == 's' || boolStr[2] == 'S'))
        {
            return true;
        }

        if (boolStr.Length == 2 &&
            (boolStr[0] == 'n' || boolStr[0] == 'N') &&
            (boolStr[1] == 'o' || boolStr[1] == 'O'))
        {
            return false;
        }

        if (boolStr.Length == 4 &&
            (boolStr[0] == 't' || boolStr[0] == 'T') &&
            (boolStr[1] == 'r' || boolStr[1] == 'R') &&
            (boolStr[2] == 'u' || boolStr[2] == 'U') &&
            (boolStr[3] == 'e' || boolStr[3] == 'E'))
        {
            return true;
        }

        if (boolStr.Length == 5 &&
            (boolStr[0] == 'f' || boolStr[0] == 'F') &&
            (boolStr[1] == 'a' || boolStr[1] == 'A') &&
            (boolStr[2] == 'l' || boolStr[2] == 'L') &&
            (boolStr[3] == 's' || boolStr[3] == 'S') &&
            (boolStr[4] == 'e' || boolStr[4] == 'E'))
        {
            return false;
        }

        return defaultValue;
    }

    internal static void TrackSession(bool standalone)
    {
        if (s_enabled)
        {
            s_client.GetMetric(s_sessionCount).TrackValue(1.0, s_uniqueId, s_os, standalone ? "true" : "false");
        }
    }

    internal static void TrackQuery(string agentName, bool isRemote)
    {
        if (s_enabled && s_knownAgents.Contains(agentName))
        {
            s_client.GetMetric(s_queryCount).TrackValue(1.0, s_uniqueId, agentName, isRemote ? "true" : "false");
        }
    }
}

/// <summary>
/// Set up the telemetry initializer to mask the platform specific names.
/// </summary>
internal class NameObscurerTelemetryInitializer : ITelemetryInitializer
{
    // Report the platform name information as "na".
    private const string NotAvailable = "na";

    /// <summary>
    /// Initialize properties we are obscuring to "na".
    /// </summary>
    /// <param name="telemetry">The instance of our telemetry.</param>
    public void Initialize(ITelemetry telemetry)
    {
        telemetry.Context.Cloud.RoleName = NotAvailable;
        telemetry.Context.GetInternalContext().NodeName = NotAvailable;
        telemetry.Context.Cloud.RoleInstance = NotAvailable;
    }
}
