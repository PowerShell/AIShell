using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIShell.Ollama.Agent;

internal class Settings
{
    public string Model { get; }
    public string Endpoint { get; }
    public bool Stream { get; }

    public Settings(ConfigData configData)
    {
        // Validate Model and Endpoint for null or empty values
        if (string.IsNullOrWhiteSpace(configData.Model))
        {
            throw new ArgumentException("\"Model\" key is missing.");
        }

        if (string.IsNullOrWhiteSpace(configData.Endpoint))
        {
            throw new ArgumentException("\"Endpoint\" key is missing.");
        }

        Model = configData.Model;
        Endpoint = configData.Endpoint;
        Stream = configData.Stream;
    }
}

internal class ConfigData
{
    public string Model { get; set; }
    public string Endpoint { get; set; }
    public bool Stream { get; set; }
}

/// <summary>
/// Use source generation to serialize and deserialize the setting file.
/// Both metadata-based and serialization-optimization modes are used to gain the best performance.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    AllowTrailingCommas = true,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ConfigData))]
internal partial class SourceGenerationContext : JsonSerializerContext { }
