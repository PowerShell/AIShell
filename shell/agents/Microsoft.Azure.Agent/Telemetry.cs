using System.Text.Json;

using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.WorkerService;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.Azure.Agent;

public class AzTrace
{
    /// <summary>
    /// Installation id from the Azure CLI installation.
    /// </summary>
    internal static string InstallationId { get; private set; }

    /// <summary>
    /// OS platform the application is running on.
    /// </summary>
    internal static string OSPlatform { get; private set; }

    internal static void Initialize()
    {
        InstallationId = GetInstallationId();
        OSPlatform = GetOSPlatform();
    }

    private static string GetInstallationId()
    {
        string azCLIProfilePath, azPSHProfilePath;
        string azureConfigDir = Environment.GetEnvironmentVariable("AZURE_CONFIG_DIR");
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        if (string.IsNullOrEmpty(azureConfigDir))
        {
            azCLIProfilePath = Path.Combine(userProfile, ".azure", "azureProfile.json");
            azPSHProfilePath = Path.Combine(userProfile, ".Azure", "AzureRmContextSettings.json");
        }
        else
        {
            azCLIProfilePath = Path.Combine(azureConfigDir, "azureProfile.json");
            azPSHProfilePath = Path.Combine(azureConfigDir, "AzureRmContextSettings.json");
        }

        try
        {
            if (File.Exists(azCLIProfilePath))
            {
                using var stream = File.OpenRead(azCLIProfilePath);
                var jsonElement = JsonSerializer.Deserialize<JsonElement>(stream);
                return jsonElement.GetProperty("installationId").GetString();
            }
            else if (File.Exists(azPSHProfilePath))
            {
                using var stream = File.OpenRead(azPSHProfilePath);
                var jsonElement = JsonSerializer.Deserialize<JsonElement>(stream);
                return jsonElement.GetProperty("Settings").GetProperty(nameof(InstallationId)).GetString();
            }
        }
        catch
        {
            // Something wrong when reading the config file.
        }

        return null;
    }

    private static string GetOSPlatform()
    {
        if (OperatingSystem.IsWindows())
        {
            return "Windows";
        }

        if (OperatingSystem.IsLinux())
        {
            return "Linux";
        }

        if (OperatingSystem.IsMacOS())
        {
            return "macOS";
        }

        return "Unknown";
    }

    /// <summary>
    /// Topic name of the response from Azure Copilot.
    /// </summary>
    internal string TopicName { get; set; }

    /// <summary>
    /// Each chat has a unique conversation id. When the customer runs '/refresh',
    /// a new chat will be initiated (i.e. a new conversation id will be created).
    /// </summary>
    internal string ConversationId { get; set; }

    /// <summary>
    /// The activity id of the user's query.
    /// </summary>
    internal string QueryId { get; set; }

    /// <summary>
    /// The event type of this telemetry.
    /// </summary>
    internal string EventType { get; set; }

    /// <summary>
    /// The shell command that triggered this telemetry.
    /// </summary>
    internal string ShellCommand { get; set; }

    /// <summary>
    /// Detailed information.
    /// </summary>
    internal object Details { get; set; }

    internal static AzTrace UserAction(
        string shellCommand,
        CopilotResponse response,
        object details)
    {
        if (Telemetry.Enabled)
        {
            return new()
            {
                EventType = "UserAction",
                ShellCommand = shellCommand,
                TopicName = response.TopicName,
                Details = details
            };
        }

        // Don't create an object when telemetry is disabled.
        return null;
    }

    internal static AzTrace Feedback(
        string shellCommand,
        bool shareConversation,
        CopilotResponse response,
        object details)
    {
        if (Telemetry.Enabled)
        {
            return new()
            {
                EventType = "Feedback",
                ShellCommand = shellCommand,
                TopicName = response.TopicName,
                QueryId = shareConversation ? response.ReplyToId : null,
                ConversationId = shareConversation ? response.ConversationId : null,
                Details = details
            };
        }

        // Don't create an object when telemetry is disabled.
        return null;
    }

    internal static AzTrace Chat(CopilotResponse response)
    {
        if (Telemetry.Enabled)
        {
            return new()
            {
                EventType = "Chat",
                TopicName = response.TopicName
            };
        }

        // Don't create an object when telemetry is disabled.
        return null;
    }

    internal static AzTrace Exception(object details)
    {
        if (Telemetry.Enabled)
        {
            return new()
            {
                EventType = "AIShell Exception",
                Details = details
            };
        }

        // Don't create an object when telemetry is disabled.
        return null;
    }
}

internal class Telemetry
{
    private static bool s_enabled;
    private static Telemetry s_singleton;

    private readonly TelemetryClient _telemetryClient;

    private Telemetry()
    {
        // Being a regular console app, there is no appsettings.json or configuration providers enabled by default.
        // Hence connection string must be specified here.
        IServiceCollection services = new ServiceCollection()
            .AddApplicationInsightsTelemetryWorkerService((ApplicationInsightsServiceOptions options) =>
                {
                    // Application insights in the AME environment.
                    options.ConnectionString = "InstrumentationKey=7a75c4d0-ae0b-4a63-9fb3-b99271f79537";
                    options.EnableHeartbeat = false;
                    options.EnableDiagnosticsTelemetryModule = false;
                });

        // Obtain TelemetryClient instance from DI, for additional manual tracking or to flush.
        _telemetryClient = services
            .BuildServiceProvider()
            .GetRequiredService<TelemetryClient>();

        // Suppress the PII recorded by default to reduce risk.
        _telemetryClient.Context.Cloud.RoleInstance = "Not Available";
    }

    private void LogTelemetry(AzTrace trace, Exception exception)
    {
        Dictionary<string, string> properties = new()
        {
            ["QueryId"] = trace.QueryId,
            ["ConversationId"] = trace.ConversationId,
            ["InstallationId"] = AzTrace.InstallationId,
            ["TopicName"] = trace.TopicName,
            ["EventType"] = trace.EventType,
            ["ShellCommand"] = trace.ShellCommand,
            ["Details"] = GetDetailedMessage(trace.Details),
            ["OSPlatform"] = AzTrace.OSPlatform
        };

        if (exception is null)
        {
            _telemetryClient.TrackTrace("AIShell", properties);
        }
        else
        {
            _telemetryClient.TrackException(exception, properties);
        }
    }

    private void Flush()
    {
        _telemetryClient.Flush();
    }

    private static string GetDetailedMessage(object details)
    {
        if (details is null)
        {
            return null;
        }

        if (details is string str)
        {
            return str;
        }

        return JsonSerializer.Serialize(details, Utils.RelaxedJsonEscapingOptions);
    }

    /// <summary>
    /// Gets whether or not telemetry is enabled.
    /// </summary>
    internal static bool Enabled => s_enabled;

    /// <summary>
    /// Initialize telemetry client.
    /// </summary>
    internal static void Initialize()
    {
        if (s_singleton is null)
        {
            s_singleton = new Telemetry();
            s_enabled = true;
            AzTrace.Initialize();
        }
    }

    /// <summary>
    /// Trace a telemetry metric.
    /// The method does nothing when telemetry is disabled.
    /// </summary>
    internal static void Trace(AzTrace trace) => s_singleton?.LogTelemetry(trace, exception: null);

    /// <summary>
    /// Trace a telemetry metric and an exception with it.
    /// The method does nothing when telemetry is disabled.
    /// </summary>
    internal static void Trace(AzTrace trace, Exception exception) => s_singleton?.LogTelemetry(trace, exception);

    /// <summary>
    /// Flush and close the telemetry.
    /// The method does nothing when telemetry is disabled.
    /// </summary>
    internal static void CloseAndFlush() => s_singleton?.Flush();
}
