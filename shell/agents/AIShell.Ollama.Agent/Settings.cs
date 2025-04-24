using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using AIShell.Abstraction;
using OllamaSharp;

namespace AIShell.Ollama.Agent;

internal partial class Settings
{
    private bool _initialized = false;
    private bool _runningConfigChecked = false;
    private bool? _isRunningLocalHost = null;
    private List<string> _availableModels = [];
    public List<ModelConfig> Presets { get; }
    public string Endpoint { get; }
    public bool Stream { get; }
    public ModelConfig RunningConfig { get; private set; }

    public Settings(ConfigData configData)
    {
        if (string.IsNullOrWhiteSpace(configData.Endpoint))
        {
            throw new InvalidOperationException("'Endpoint' key is missing in configuration.");
        }

        Presets = configData.Presets ?? [];
        Endpoint = configData.Endpoint;
        Stream = configData.Stream;

        if (string.IsNullOrEmpty(configData.DefaultPreset))
        {
            RunningConfig = Presets.Count > 0
                ? Presets[0] with { }  /* No default preset - use the first one defined in Presets */
                : new ModelConfig(name: nameof(RunningConfig), modelName: ""); /* No presets are defined - use empty */
        }
        else
        {
            // Ensure the default configuration is available in the list of configurations.
            var first = Presets.FirstOrDefault(c => c.Name == configData.DefaultPreset)
                ?? throw new InvalidOperationException($"The selected default preset '{configData.DefaultPreset}' doesn't exist.");
            // Use the default config
            RunningConfig = first with { };
        }
    }

    /// <summary>
    /// Retrieve available models from the Ollama endpoint.
    /// </summary>
    /// <param name="host">Used for writing error to host when it's a local endpoint but the Ollama server is not started. When the value is null, the endpoint check will be skipped.</param>
    /// <param name="cancellationToken">Used for cancel the operation.</param>
    /// <returns></returns>
    private async Task<bool> EnsureModelsInitialized(IHost host, CancellationToken cancellationToken = default)
    {
        if (_initialized)
        {
            return true;
        }

        // The endpoint check is supposed to be interactive and can be skipped in some cases, such as when
        // the `PerformSelfcheck` method was already called right before entering this method.
        // So, we will simply skip the endpoint check when the passed-in host is null. If there's anything
        // wrong with the endpoint, the subsequent calls to retrieve models will fail and throw anyway.
        if (host is not null)
        {
            bool success = await PerformSelfcheck(host, checkEndpointOnly: true);
            if (!success)
            {
                return false;
            }
        }

        using OllamaApiClient client = new(Endpoint);
        var models = await client.ListLocalModelsAsync(cancellationToken).ConfigureAwait(false);
        _availableModels = [.. models.Select(m => m.Name)];
        _initialized = true;
        return true;
    }

    internal async Task<ICollection<string>> GetAllModels(IHost host = null, CancellationToken cancellationToken = default)
    {
        if (await EnsureModelsInitialized(host, cancellationToken).ConfigureAwait(false))
        {
            return _availableModels;
        }

        return [];
    }

    internal void EnsureModelNameIsValid(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        if (!_availableModels.Contains(name.AddLatestTagIfNecessery()))
        {
            throw new InvalidOperationException($"A model with the name '{name}' doesn't exist. The available models are: [{string.Join(", ", _availableModels)}].");
        }
    }

    private static List<IRenderElement<string>> GetSystemPromptRenderElements() => [new CustomElement<string>(label: "System prompt", s => s)];

    internal void ShowSystemPrompt(IHost host) => host.RenderList(RunningConfig.SystemPrompt, GetSystemPromptRenderElements());

    internal void SetSystemPrompt(IHost host, string prompt)
    {
        RunningConfig = RunningConfig with { SystemPrompt = prompt ?? string.Empty };
        host.RenderList(RunningConfig.SystemPrompt, GetSystemPromptRenderElements());
    }

    private static List<IRenderElement<string>> GetRenderModelElements(Func<string, bool> isActive) => [
        new CustomElement<string>(label: "Name", m => m),
        new CustomElement<string>(label: "Active", m => isActive(m) ? "true" : string.Empty)
    ];

    internal async Task UseModel(IHost host, string name, CancellationToken cancellationToken = default)
    {
        if (await EnsureModelsInitialized(host, cancellationToken).ConfigureAwait(false))
        {
            EnsureModelNameIsValid(name);
            RunningConfig = RunningConfig with { ModelName = name };
            _runningConfigChecked = true;
        }
    }

    internal async Task ListAllModels(IHost host, CancellationToken cancellationToken = default)
    {
        if (await EnsureModelsInitialized(host, cancellationToken).ConfigureAwait(false))
        {
            host.RenderTable(_availableModels, GetRenderModelElements(m => m == RunningConfig.ModelName.AddLatestTagIfNecessery()));
        }
    }

    internal async Task ShowOneModel(IHost host, string name, CancellationToken cancellationToken = default)
    {
        if (await EnsureModelsInitialized(host, cancellationToken).ConfigureAwait(false))
        {
            EnsureModelNameIsValid(name);
            host.RenderList(name, GetRenderModelElements(m => m == RunningConfig.ModelName.AddLatestTagIfNecessery()));
        }
    }

    internal async Task UsePreset(IHost host, ModelConfig preset, CancellationToken cancellationToken = default)
    {
        if (await EnsureModelsInitialized(host, cancellationToken).ConfigureAwait(false))
        {
            EnsureModelNameIsValid(preset.ModelName);
            RunningConfig = preset with { };
            _runningConfigChecked = true;
        }
    }

    internal void ListAllPresets(IHost host)
    {
        host.RenderTable(
            Presets,
            [
                new PropertyElement<ModelConfig>(nameof(ModelConfig.Name)),
                new CustomElement<ModelConfig>(label: "Active", m => m == RunningConfig  ? "true" : string.Empty)
            ]);
    }

    internal void ShowOnePreset(IHost host, string name)
    {
        var preset = Presets.FirstOrDefault(c => c.Name == name);
        if (preset is null)
        {
            host.WriteErrorLine($"The preset '{name}' doesn't exist.");
            return;
        }

        host.RenderList(
            preset,
            [
                new PropertyElement<ModelConfig>(nameof(ModelConfig.Name)),
                new PropertyElement<ModelConfig>(nameof(ModelConfig.Description)),
                new PropertyElement<ModelConfig>(nameof(ModelConfig.ModelName)),
                new PropertyElement<ModelConfig>(nameof(ModelConfig.SystemPrompt)),
                new CustomElement<ModelConfig>(label: "Active", m => m == RunningConfig ? "true" : string.Empty),
            ]);
    }

    internal async Task<bool> PerformSelfcheck(IHost host, bool checkEndpointOnly = false)
    {
        _isRunningLocalHost ??= IsLocalHost().IsMatch(new Uri(Endpoint).Host);

        if (_isRunningLocalHost is true && Process.GetProcessesByName("ollama").Length is 0)
        {
            host.WriteErrorLine("Please be sure the Ollama is installed and server is running. Check all the prerequisites in the README of this agent are met.");
            return false;
        }

        if (!checkEndpointOnly && !_runningConfigChecked)
        {
            // Skip the endpoint check in 'EnsureModelsInitialized' as we already did it.
            await EnsureModelsInitialized(host: null).ConfigureAwait(false);
            if (string.IsNullOrEmpty(RunningConfig.ModelName))
            {
                // There is no model set, so use the first one available.
                if (_availableModels.Count is 0)
                {
                    host.WriteErrorLine($"No models are available to use from '{Endpoint}'.");
                    return false;
                }

                RunningConfig = RunningConfig with { ModelName = _availableModels.First() };
                host.MarkupLine($"No Ollama model is configured. Using the first available model [green]'{RunningConfig.ModelName}'[/].");
            }
            else
            {
                try
                {
                    EnsureModelNameIsValid(RunningConfig.ModelName);
                }
                catch (InvalidOperationException e)
                {
                    host.WriteErrorLine(e.Message);
                    return false;
                }
            }

            _runningConfigChecked = true;
        }

        return true;
    }

    /// <summary>
    /// Defines a generated regular expression to match localhost addresses
    /// "localhost", "127.0.0.1" and "[::1]" with case-insensitivity.
    /// </summary>
    [GeneratedRegex("^(localhost|127\\.0\\.0\\.1|\\[::1\\])$", RegexOptions.IgnoreCase)]
    internal partial Regex IsLocalHost();
}

/// <summary>
/// Represents a configuration for an Ollama model.
/// </summary>
internal record ModelConfig
{
    [JsonRequired]
    public string Name { get; init; }

    [JsonRequired]
    public string ModelName { get; init; }

    public string SystemPrompt { get; init; } = string.Empty;

    public string Description { get; init; } = string.Empty;
    
    /// <summary>
    /// Initializes a new instance of the <see cref="ModelConfig"/> class with the specified parameters.
    /// </summary>
    /// <param name="name">The name of the model configuration.</param>
    /// <param name="modelName">The name of the model to be used.</param>
    /// <param name="systemPrompt">An optional system prompt to guide the model's behavior. Defaults to an empty string.</param>
    /// <param name="description">An optional description of the model configuration. Defaults to an empty string.</param>
    public ModelConfig(string name, string modelName, string systemPrompt = "", string description = "")
    {
        Name = name;
        ModelName = modelName;
        SystemPrompt = systemPrompt;
        Description = description;
    }
}

/// <summary>
/// Represents the configuration data for the AI Shell Ollama Agent.
/// </summary>
/// <param name="Presets">Optional. A list of predefined model configurations.</param>
/// <param name="Endpoint">Optional. The endpoint URL for the agent. Defaults to "http://localhost:11434"
/// <param name="Stream">Optional. Indicates whether streaming is enabled. Defaults to <c>false</c>.</param>
/// <param name="DefaultPreset">Optional. Specifies the default preset name. If not provided, the first available preset will be used.</param>
internal record ConfigData(List<ModelConfig> Presets, string Endpoint = "http://localhost:11434", bool Stream = false, string DefaultPreset = "");

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

static class TagExtensions
{
    public static string AddLatestTagIfNecessery(this string model) =>
        model.Contains(':') ? model : string.Concat(model, ":latest");
}
