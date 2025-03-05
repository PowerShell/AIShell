using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Linq;
using AIShell.Abstraction;
using OllamaSharp;
using OllamaSharp.Models;

namespace AIShell.Ollama.Agent;

internal class Settings
{
    private OllamaModel _activeModel;
    private ICollection<OllamaModel> _models = [];
    private bool _initialized = false;

    public ICollection<ModelConfig> Configs { get; }
    public string Endpoint { get; }
    public bool Stream { get; }
    public RunningConfig RunningConfig { get; private set; }

    public Settings(ConfigData configData)
    {
        if (string.IsNullOrWhiteSpace(configData.Endpoint))
        {
            throw new ArgumentException("\"Endpoint\" key is missing.");
        }

        Configs = configData.Configs;
        Endpoint = configData.Endpoint;
        Stream = configData.Stream;

        RunningConfig = new RunningConfig();

        if (Configs is not null)
        {
            var modelConfig = Configs.FirstOrDefault(c => c.Name == configData.DefaultConfig);
            if (modelConfig is not null)
            {
                RunningConfig = modelConfig.ToRunnigConfig();
            }
        }
    }

    private async Task EnsureModelsInitialized(CancellationToken cancellationToken = default)
    {
        if (!_initialized)
        {
            OllamaApiClient _client = null;
            try
            {
                _client = new OllamaApiClient(this.Endpoint);
                var models = await _client.ListLocalModelsAsync(cancellationToken).ConfigureAwait(false);
                this._models = models.Select(m => new OllamaModel(Name: m.Name)).ToList().AsReadOnly();

                if (this._models.Count == 0)
                {
                    throw new InvalidOperationException("No model is available.");
                }

                if (string.IsNullOrEmpty(this.RunningConfig.ModelName))
                {
                    // Active GPT not specified, but there is only one GPT defined, then use it by default.
                    _activeModel = _models.First();
                }
                else
                {
                    _activeModel = _models.FirstOrDefault(m => m.Name == this.RunningConfig.ModelName);
                    if (_activeModel == null)
                    {
                        string message = $"The Model '{this.RunningConfig.ModelName}' specified as \"Default\" in the configuration doesn't exist.";
                        throw new InvalidOperationException(message);
                    }
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
    }

    internal async Task<ICollection<OllamaModel>> GetAllModels(CancellationToken cancellationToken = default)
    {
        await EnsureModelsInitialized(cancellationToken).ConfigureAwait(false);
        return this._models;
    }

    internal async Task<OllamaModel> GetModelByName(string name, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        await EnsureModelsInitialized(cancellationToken).ConfigureAwait(false);
        var model = _models.FirstOrDefault(m => m.Name == name);

        if (model is not null)
        {
            return model;
        }

        throw new InvalidOperationException($"A model with the name '{name}' doesn't exist.");
    }


    internal async Task UseModel(string name, CancellationToken cancellationToken = default)
    {
        await EnsureModelsInitialized(cancellationToken).ConfigureAwait(false);
        _activeModel = await GetModelByName(name, cancellationToken);
    }

    internal void UseModel(OllamaModel model)
    {
        _activeModel = model;
    }

    internal void ShowSystemPrompt(IHost host)
    {
        host.RenderList(
            RunningConfig.SystemPrompt,
            [
                new CustomElement<string>(label: "System prompt is", str => str)
            ]);
    }

    internal void SetSystemPrompt(IHost host, string prompt)
    {   
        this.RunningConfig = this.RunningConfig with { SystemPrompt = prompt ?? string.Empty };
        host.RenderList(
            this.RunningConfig.SystemPrompt,
            [
                new CustomElement<string>(label: "New system prompt is", str => str)
            ]);
    }

    internal async Task ListAllModels(IHost host, CancellationToken cancellationToken = default)
    {
        await EnsureModelsInitialized(cancellationToken).ConfigureAwait(false);
        host.RenderTable(
            [.. _models],
            [
                new PropertyElement<OllamaModel>(nameof(OllamaModel.Name)),
                new CustomElement<OllamaModel>(label: "Active", m => m.Name == _activeModel?.Name ? "true" : string.Empty)
            ]);
    }

    internal async Task ShowOneModel(IHost host, string name, CancellationToken cancellationToken = default)
    {
        var model = await GetModelByName(name, cancellationToken).ConfigureAwait(false);
        host.RenderList(
            model,
            [
                new PropertyElement<OllamaModel>(nameof(OllamaModel.Name)),
                new CustomElement<OllamaModel>(label: "Active", m => m.Name == _activeModel?.Name ? "true" : string.Empty)
            ]);
    }

    internal void ShowOneConfig(IHost host, string name, CancellationToken cancellationToken = default)
    {
        var config = this.Configs.FirstOrDefault(c => c.Name == name);
        host.RenderList(
            config,
            [
                new PropertyElement<ModelConfig>(nameof(ModelConfig.Name)),
                new PropertyElement<ModelConfig>(nameof(ModelConfig.Description)),
                new PropertyElement<ModelConfig>(nameof(ModelConfig.ModelName)),
                new PropertyElement<ModelConfig>(nameof(ModelConfig.SystemPrompt)),
                new CustomElement<ModelConfig>(label: "Active", m => m.ToRunnigConfig() == this.RunningConfig ? "true" : string.Empty),
            ]);
    }

    internal async Task UseConfg(IHost host, ModelConfig config, CancellationToken cancellationToken = default)
    {
        this.RunningConfig = config.ToRunnigConfig();
        await UseModel(this.RunningConfig.ModelName, cancellationToken).ConfigureAwait(false);
    }

    internal void ListAllConfigs(IHost host)
    {
        host.RenderTable(
            [.. this.Configs],
            [
                new PropertyElement<ModelConfig>(nameof(ModelConfig.Name)),
                new CustomElement<ModelConfig>(label: "Active", m => m.ToRunnigConfig() == this.RunningConfig  ? "true" : string.Empty)
            ]);
    }

    internal async Task<OllamaModel> GetActiveModel(CancellationToken cancellationToken = default)
    {
        await EnsureModelsInitialized(cancellationToken).ConfigureAwait(false);
        return _activeModel;
    }    
}

internal record OllamaModel(string Name);

internal record ModelConfig(string Name, string SystemPrompt, string ModelName, string Description, bool ResetContext);

internal record ConfigData(List<ModelConfig> Configs, string Endpoint, bool Stream, string DefaultConfig);

internal record RunningConfig(string Name = "", string ModelName = "", string SystemPrompt = "", bool ResetContext = false);

static class ModelConfigExtensions
{
    public static RunningConfig ToRunnigConfig(this ModelConfig config)
    {
        return new RunningConfig()
        {
            Name = config.Name,
            ModelName = config.ModelName,
            SystemPrompt = config.SystemPrompt,
            ResetContext = config.ResetContext
        };
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
[JsonSerializable(typeof(ConfigData))]
internal partial class SourceGenerationContext : JsonSerializerContext { }
