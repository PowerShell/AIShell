using System.Collections.Concurrent;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Management.Automation.Runspaces;

namespace Microsoft.Azure.Agent;

using PowerShell = System.Management.Automation.PowerShell;
using ExecutionPolicy = Microsoft.PowerShell.ExecutionPolicy;

internal static class Utils
{
    internal const string JsonContentType = "application/json";

    private static readonly JsonSerializerOptions s_jsonOptions;
    private static readonly JsonSerializerOptions s_humanReadableOptions;
    private static readonly JsonSerializerOptions s_relaxedJsonEscapingOptions;

    static Utils()
    {
        s_jsonOptions = new JsonSerializerOptions()
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        s_humanReadableOptions = new JsonSerializerOptions()
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        s_relaxedJsonEscapingOptions = new JsonSerializerOptions
        {
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
    }

    internal static JsonSerializerOptions JsonOptions => s_jsonOptions;
    internal static JsonSerializerOptions JsonHumanReadableOptions => s_humanReadableOptions;
    internal static JsonSerializerOptions RelaxedJsonEscapingOptions => s_relaxedJsonEscapingOptions;

    internal async static Task EnsureSuccessStatusCodeForTokenRequest(this HttpResponseMessage response, string errorMessage)
    {
        if (!response.IsSuccessStatusCode)
        {
            string responseText = await response.Content.ReadAsStringAsync(CancellationToken.None);
            if (string.IsNullOrEmpty(responseText))
            {
                responseText = "<empty>";
            }

            string message = $"{errorMessage} HTTP status: {response.StatusCode}, Response: {responseText}.";
            Telemetry.Trace(AzTrace.Exception(message));
            throw new TokenRequestException(message);
        }
    }
}

internal class TokenRequestException : Exception
{
    /// <summary>
    /// Access to Copilot was denied.
    /// </summary>
    internal bool UserUnauthorized { get; set; }

    internal TokenRequestException(string message)
        : base(message)
    {
    }

    internal TokenRequestException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

internal class ConnectionDroppedException : Exception
{
    internal ConnectionDroppedException(string message)
        : base(message)
    {
    }
}

internal class CorruptDataException : Exception
{
    private CorruptDataException(string message)
        : base(message)
    {
    }

    internal static CorruptDataException Create(string message, CopilotActivity activity)
    {
        string errorMessage = $"Unexpected copilot activity received. {message}\n\n{activity.Serialize()}\n";
        Telemetry.Trace(AzTrace.Exception(errorMessage));
        return new CorruptDataException(errorMessage);
    }
}

internal class ChatMessage
{
    public string Role { get; set; }
    public string Content { get; set; }
}

internal class PowerShellPool
{
    private readonly int _size;
    private readonly BlockingCollection<PowerShell> _pool;

    internal PowerShellPool(int size)
    {
        _size = size;
        _pool = new(boundedCapacity: size);

        var iss = InitialSessionState.CreateDefault();
        iss.ImportPSModule("Az.Accounts");

        if (OperatingSystem.IsWindows())
        {
            iss.ExecutionPolicy = ExecutionPolicy.Bypass;
        }

        // Pre-populate the pool on worker thread.
        Task.Factory.StartNew(
            CreatePowerShell,
            iss,
            CancellationToken.None,
            TaskCreationOptions.DenyChildAttach,
            TaskScheduler.Default);
    }

    private void CreatePowerShell(object state)
    {
        var iss = (InitialSessionState)state;

        for (int i = 0; i < _size; i++)
        {
            var runspace = RunspaceFactory.CreateRunspace(iss);
            runspace.Open();

            var pwsh = PowerShell.Create(runspace);
            _pool.Add(pwsh);
        }
    }

    internal PowerShell Checkout()
    {
        return _pool.Take();
    }

    internal void Return(PowerShell pwsh)
    {
        if (pwsh is not null)
        {
            pwsh.Commands.Clear();
            pwsh.Streams.ClearStreams();

            _pool.Add(pwsh);
        }
    }
}
