using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace AIShell.Kernel.Mcp;

/// <summary>
/// MCP configuration defined in mcp.json.
/// </summary>
internal class McpConfig
{
    [JsonPropertyName("servers")]
    public Dictionary<string, McpServerConfig> Servers { get; set; } = [];

    internal static McpConfig Load()
    {
        McpConfig mcpConfig = null;
        FileInfo file = new(Utils.AppMcpFile);
        if (file.Exists)
        {
            using var stream = file.OpenRead();
            mcpConfig = JsonSerializer.Deserialize(stream, McpJsonContext.Default.McpConfig);
            mcpConfig.Validate();
        }

        return mcpConfig is { Servers.Count: 0 } ? null : mcpConfig;
    }

    /// <summary>
    /// Post-deserialization validation.
    /// </summary>
    /// <exception cref="InvalidOperationException"></exception>
    private void Validate()
    {
        List<string> allErrors = null;

        foreach (var (name, server) in Servers)
        {
            server.Name = name;
            if (Enum.TryParse(server.Type, ignoreCase: true, out McpType mcpType))
            {
                server.Transport = mcpType;
            }
            else
            {
                (allErrors ??= []).Add($"Server '{name}': 'type' is required and the value should be one of the following: {string.Join(',', Enum.GetNames<McpType>())}.");
                continue;
            }

            List<string> curErrs = null;

            if (mcpType is McpType.stdio)
            {
                bool hasUrlGroup = !string.IsNullOrEmpty(server.Url) || server.Headers is { };
                if (hasUrlGroup)
                {
                    (curErrs ??= []).Add($"'url' and 'headers' fields are invalid for 'stdio' type servers.");
                }

                if (string.IsNullOrEmpty(server.Command))
                {
                    (curErrs ??= []).Add($"'command' is required for 'stdio' type servers.");
                }
            }
            else
            {
                bool hasCommandGroup = !string.IsNullOrEmpty(server.Command) || server.Args is { } || server.Env is { };
                if (hasCommandGroup)
                {
                    (curErrs ??= []).Add($"'command', 'args', and 'env' fields are invalid for '{mcpType}' type servers.");
                }

                if (string.IsNullOrEmpty(server.Url))
                {
                    (curErrs ??= []).Add($"'url' is required for '{mcpType}' type servers.");
                }
                else if (!Uri.TryCreate(server.Url, UriKind.Absolute, out Uri uri))
                {
                    (curErrs ??= []).Add($"the specified value for 'url' is not a valid URI.");
                }
                else if (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps)
                {
                    (curErrs ??= []).Add($"'url' is expected to be a 'http' or 'https' resource.");
                }
                else
                {
                    server.Endpoint = uri;
                }
            }

            if (curErrs is [string onlyErr])
            {
                (allErrors ??= []).Add($"Server '{name}': {onlyErr}");
            }
            else if (curErrs is { Count: > 1 })
            {
                string prefix = $"Server '{name}':";
                int size = curErrs.Sum(a => a.Length) + curErrs.Count * 5 + prefix.Length;
                StringBuilder sb = new(prefix, capacity: size);

                foreach (string element in curErrs)
                {
                    sb.Append($"\n  - {element}");
                }

                (allErrors ??= []).Add(sb.ToString());
            }
        }

        if (allErrors is { })
        {
            string errorMsg = string.Join('\n', allErrors);
            throw new InvalidOperationException(errorMsg);
        }
    }
}

/// <summary>
/// Configuration of a server.
/// </summary>
internal class McpServerConfig
{
    [JsonPropertyName("type")]
    public string Type { get; set; }

    [JsonPropertyName("command")]
    public string Command { get; set; }

    [JsonPropertyName("args")]
    public List<string> Args { get; set; }

    [JsonPropertyName("env")]
    public Dictionary<string, string> Env { get; set; }

    [JsonPropertyName("url")]
    public string Url { get; set; }

    [JsonPropertyName("headers")]
    public Dictionary<string, string> Headers { get; set; }

    internal string Name { get; set; }
    internal Uri Endpoint { get; set; }
    internal McpType Transport { get; set; }

    internal IClientTransport ToClientTransport()
    {
        return Transport switch
        {
            McpType.stdio => new StdioClientTransport(new()
            {
                Name = Name,
                Command = Command,
                Arguments = Args,
                EnvironmentVariables = Env,
            }),

            _ => new SseClientTransport(new()
            {
                Name = Name,
                Endpoint = Endpoint,
                AdditionalHeaders = Headers,
                TransportMode = Transport is McpType.sse ? HttpTransportMode.Sse : HttpTransportMode.StreamableHttp,
                ConnectionTimeout = TimeSpan.FromSeconds(15),
            })
        };
    }
}

/// <summary>
/// MCP transport types.
/// </summary>
internal enum McpType
{
    stdio,
    sse,
    http,
}

/// <summary>
/// Source generation helper for deserializing mcp.json.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    AllowTrailingCommas = true,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(McpConfig))]
[JsonSerializable(typeof(McpServerConfig))]
[JsonSerializable(typeof(CallToolResponse))]
internal partial class McpJsonContext : JsonSerializerContext { }
