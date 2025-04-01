using System.Text.Json;
using System.Text.Json.Serialization;
using AIShell.Abstraction;
using OllamaSharp;

namespace AIShell.Ollama.Agent;

internal class Settings
{
    private bool _initialized = false;
    private List<string> AvailableModels { get; set; } = [];
    public List<ModelConfig> Configs { get; }
    public string Endpoint { get; }
    public bool Stream { get; }
    public ModelConfig RunningConfig { get; private set; }

    public Settings(ConfigData configData)
    {
        if (string.IsNullOrWhiteSpace(configData.Endpoint))
        {
            throw new InvalidOperationException("'Endpoint' key is missing in configuration.");
        }

        Configs = configData.Configs ?? [];
        Endpoint = configData.Endpoint;
        Stream = configData.Stream;

        if (string.IsNullOrEmpty(configData.DefaultConfig))
        {
            RunningConfig = Configs.Count > 0
                ? Configs[0] with { }  /* No default config - use the first one defined in Configs */
                : new ModelConfig(nameof(RunningConfig), ModelName: ""); /* No config available - use empty */
        }
        else
        {
            // Ensure the default configuration is available in the list of configurations.
            var first = Configs.FirstOrDefault(c => c.Name == configData.DefaultConfig)
                ?? throw new InvalidOperationException($"The selected default configuration '{configData.DefaultConfig}' doesn't exist.");
            // Use the default config
            RunningConfig = first with { };
        }
    }

    private async Task EnsureModelsInitialized(CancellationToken cancellationToken = default)
    {
        if (_initialized)
        { 
            return;
        }

        OllamaApiClient _client = null;
        try
        {
            _client = new OllamaApiClient(Endpoint);
            var models = await _client.ListLocalModelsAsync(cancellationToken).ConfigureAwait(false);
            AvailableModels = [.. models.Select(m => m.Name)];

            if (AvailableModels.Count == 0)
            {
                throw new InvalidOperationException($"No models are available to use from '{Endpoint}'.");
            }
            _initialized = true;
        }
        finally
        {
            if (_client is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
    }

    internal async Task<ICollection<string>> GetAllModels(CancellationToken cancellationToken = default)
    {
        await EnsureModelsInitialized(cancellationToken).ConfigureAwait(false);
        return AvailableModels;
    }

    internal async Task<string> GetModelByName(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        await EnsureModelsInitialized(cancellationToken).ConfigureAwait(false);
        if (!AvailableModels.Contains(name))
        {
            throw new InvalidOperationException($"A model with the name '{name}' doesn't exist in the list of available models.");
        }

        return name;
    }

    private static List<IRenderElement<string>> GetSystemPromptRenderElements() => [ new CustomElement<string>(label: "System prompt", s => s) ];

    internal void ShowSystemPrompt(IHost host) => host.RenderList(RunningConfig.SystemPrompt, GetSystemPromptRenderElements());

    internal void SetSystemPrompt(IHost host, string prompt)
    {   
        RunningConfig = RunningConfig with { SystemPrompt = prompt ?? string.Empty };
        host.RenderList(RunningConfig.SystemPrompt, GetSystemPromptRenderElements());
    }
    
    private static List<IRenderElement<string>> GetRenderModelElements(string currentModelName) => [
        new CustomElement<string>(label: "Model Name", m => m),
        new CustomElement<string>(label: "Active", m => m == currentModelName ? "true" : string.Empty)
    ];

    internal async Task UseModel(string name, CancellationToken cancellationToken = default) =>
        RunningConfig = RunningConfig with { ModelName = await GetModelByName(name, cancellationToken).ConfigureAwait(false) };

    internal async Task ListAllModels(IHost host, CancellationToken cancellationToken = default)
    {
        await EnsureModelsInitialized(cancellationToken).ConfigureAwait(false);
        host.RenderTable(AvailableModels, GetRenderModelElements(RunningConfig.ModelName));
    }

    internal async Task ShowOneModel(IHost host, string name, CancellationToken cancellationToken = default)
    {
        var model = await GetModelByName(name, cancellationToken).ConfigureAwait(false);
        host.RenderList(model, GetRenderModelElements(RunningConfig.ModelName));
    }

    internal async Task UseConfg(ModelConfig config, CancellationToken cancellationToken = default)
    {
        RunningConfig = config with { };
        await UseModel(RunningConfig.ModelName, cancellationToken).ConfigureAwait(false);
    }

    internal void ListAllConfigs(IHost host)
    {
        host.RenderTable(
            Configs,
            [
                new PropertyElement<ModelConfig>(nameof(ModelConfig.Name)),
                new CustomElement<ModelConfig>(label: "Active", m => m == RunningConfig  ? "true" : string.Empty)
            ]);
    }

    internal void ShowOneConfig(IHost host, string name)
    {
        var config = Configs.FirstOrDefault(c => c.Name == name);
        host.RenderList(
            config,
            [
                new PropertyElement<ModelConfig>(nameof(ModelConfig.Name)),
                new PropertyElement<ModelConfig>(nameof(ModelConfig.Description)),
                new PropertyElement<ModelConfig>(nameof(ModelConfig.ModelName)),
                new PropertyElement<ModelConfig>(nameof(ModelConfig.SystemPrompt)),
                new CustomElement<ModelConfig>(label: "Active", m => m == RunningConfig ? "true" : string.Empty),
            ]);
    }

    internal async Task<string> GetActiveModel(IHost host, CancellationToken cancellationToken = default)
    {
        await EnsureModelsInitialized(cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrEmpty(RunningConfig.ModelName))
        {
            // There is no model set, so use the first one available.
            RunningConfig = RunningConfig with { ModelName = AvailableModels.First() };
            host.MarkupLine($"No Ollama model is configured. Using the first available model [green]'{RunningConfig.ModelName}'.");
        }
        else
        {
            if (!AvailableModels.Contains(RunningConfig.ModelName))
            {
                throw new InvalidOperationException($"The configured Ollama model '{RunningConfig.ModelName}' doesn't exist in the list of available models.");
            }
        }

        return RunningConfig.ModelName;
    }
}

/// <summary>
/// Represents a configuration for an Ollama model.
/// </summary>
/// <param name="Name">Required. The unique identifier name for this configuration.</param>
/// <param name="ModelName">Required. The name of the Ollama model to be used.</param>
/// <param name="SystemPrompt">Optional. The system prompt to be used with this model configuration.</param>
/// <param name="Description">Optional. A human-readable description of this configuration.</param>
/// <param name="ResetContext">Optional. Indicates whether the context should be reset when switching to this configuration. Defaults to <c>false</c>.</param>

internal record ModelConfig(string Name, string ModelName, string SystemPrompt = "", string Description = "", bool ResetContext = false);

/// <summary>
/// Represents the configuration data for the AI Shell Ollama Agent.
/// </summary>
/// <param name="Configs">Optional. A list of predefined model configurations.</param>
/// <param name="Endpoint">Required. The endpoint URL for the agent.</param>
/// <param name="Stream">Optional. Indicates whether streaming is enabled. Defaults to <c>false</c>.</param>
/// <param name="DefaultConfig">Optional. Specifies the default configuration name. If not provided, the first available config will be used.</param>
internal record ConfigData(List<ModelConfig> Configs, string Endpoint, bool Stream = false, string DefaultConfig = "");

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
