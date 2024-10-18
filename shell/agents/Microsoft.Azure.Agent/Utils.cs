using System.Text.Json;

namespace Microsoft.Azure.Agent;

internal static class Utils
{
    internal const string JsonContentType = "application/json";

    private static readonly JsonSerializerOptions s_jsonOptions;
    private static readonly JsonSerializerOptions s_humanReadableOptions;

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
    }

    internal static JsonSerializerOptions JsonOptions => s_jsonOptions;
    internal static JsonSerializerOptions JsonHumanReadableOptions => s_humanReadableOptions;
}

internal class TokenRequestException : Exception
{
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
        return new CorruptDataException($"Unexpected copilot activity received. {message}\n\n{activity.Serialize()}\n");
    }
}

internal class ChatMessage
{
    public string Role { get; set; }
    public string Content { get; set; }
}
