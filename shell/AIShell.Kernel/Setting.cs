using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIShell.Kernel;

/// <summary>
/// Settings for AIShell.
/// </summary>
internal class Setting
{
    public string DefaultAgent { set; get; }
    public bool UseClipboardContent { set; get; }

    internal static Setting Load()
    {
        FileInfo file = new(Utils.AppConfigFile);
        if (file.Exists)
        {
            try
            {
                using var stream = file.OpenRead();
                return JsonSerializer.Deserialize(stream, AppSettingJsonContext.Default.Setting);
            }
            catch (Exception e)
            {
                Console.WriteLine($"""
                    Failed to load the configuration file '{Utils.AppConfigFile}' with the following error:
                    {e.Message}

                    Proceeding with the default configuration ...

                    """);
            }
        }

        return new Setting();
    }
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
[JsonSerializable(typeof(Setting))]
internal partial class AppSettingJsonContext : JsonSerializerContext { }
