using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Security;
using AIShell.Abstraction;

namespace AIShell.Interpreter.Agent;

internal enum EndpointType
{
    AzureOpenAI,
    OpenAI,
}

public enum AuthType
{
    ApiKey,
    EntraID
}

internal class Settings
{
    internal EndpointType Type { get; }
    internal bool Dirty { set; get; }
    internal ModelInfo ModelInfo { private set; get; }

    public string Endpoint { set; get; }
    public string Deployment { set; get; }
    public string ModelName { set; get; }
    public SecureString Key { set; get; }

    public AuthType AuthType { set; get; } = AuthType.ApiKey;

    public bool AutoExecution { set; get; }
    public bool DisplayErrors { set; get; }

    public Settings(ConfigData configData)
    {
        ArgumentException.ThrowIfNullOrEmpty(configData.ModelName);

        Endpoint = configData.Endpoint?.Trim().TrimEnd('/');
        Deployment = configData.Deployment;
        ModelName = configData.ModelName.ToLowerInvariant();
        AutoExecution = configData.AutoExecution ?? false;
        DisplayErrors = configData.DisplayErrors ?? true;
        Key = configData.Key;
        AuthType = configData.AuthType;

        Dirty = false;
        ModelInfo = ModelInfo.TryResolve(ModelName, out var model) ? model : null;

        bool noEndpoint = string.IsNullOrEmpty(Endpoint);
        bool noDeployment = string.IsNullOrEmpty(Deployment);
        Type = noEndpoint && noDeployment
            ? EndpointType.OpenAI
            : !noEndpoint && !noDeployment
                ? EndpointType.AzureOpenAI
                : throw new InvalidOperationException($"Invalid setting: {(noEndpoint ? "Endpoint" : "Deployment")} key is missing. To use Azure OpenAI service, please specify both the 'Endpoint' and 'Deployment' keys. To use OpenAI service, please ignore both keys.");

        // EntraID authentication is only supported for Azure OpenAI
        if (AuthType == AuthType.EntraID && Type != EndpointType.AzureOpenAI)
        {
            throw new InvalidOperationException("EntraID authentication is only supported for Azure OpenAI service.");
        }
    }

    internal void MarkClean()
    {
        Dirty = false;
    }

    /// <summary>
    /// Self check for required ModelInfo and Key.
    /// </summary>
    /// <returns></returns>
    internal async Task<bool> SelfCheck(IHost host, CancellationToken token)
    {
        if ((AuthType is AuthType.EntraID || Key is not null) && ModelInfo is not null)
        {
            return true;
        }

        host.WriteLine()
            .MarkupNoteLine($"Some required information is missing in the configuration:");
        ShowEndpointInfo(host);

        try
        {
            if (ModelInfo is null)
            {
                await AskForModel(host, token);
            }

            if (AuthType == AuthType.ApiKey && Key is null)
            {
                await AskForKeyAsync(host, token);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            // User cancelled the prompt.
            host.MarkupLine("[red]^C[/]\n");
            return false;
        }
    }

    private void ShowEndpointInfo(IHost host)
    {
        CustomElement<Settings>[] GPTInfo = Type switch
        {
            EndpointType.AzureOpenAI =>
                [
                    new(label: "  Type", m => m.Type.ToString()),
                    new(label: "  Endpoint", m => m.Endpoint),
                    new(label: "  Deployment", m => m.Deployment),
                    new(label: "  Model", m => m.ModelName),
                    new(label: "  Auth Type", m => m.AuthType.ToString()),
                ],

            EndpointType.OpenAI =>
                [
                    new(label: "  Type", m => m.Type.ToString()),
                    new(label: "  Model", m => m.ModelName),
                    new(label: "  Auth Type", m => m.AuthType.ToString()),
                ],

            _ => throw new UnreachableException(),
        };

        host.RenderList(this, GPTInfo);
    }

    /// <summary>
    /// Validate the model name and prompt for fixing it if it's invalid.
    /// </summary>
    /// <returns>A boolean value indicates whether the validation and setup was successful.</returns>
    private async Task AskForModel(IHost host, CancellationToken cancellationToken)
    {
        host.MarkupWarningLine($"'{ModelName}' is not a supported OpenAI chat completion model.");
        ModelName = await host.PromptForSelectionAsync(
            title: "Choose from the list of [green]supported OpenAI models[/]:",
            choices: ModelInfo.SupportedModels(),
            cancellationToken: cancellationToken);

        Dirty = true;
        ModelInfo = ModelInfo.GetByName(ModelName);
        host.WriteLine();
    }

    /// <summary>
    /// Prompt for setting up the access key if it doesn't exist.
    /// </summary>
    /// <returns>A boolean value indicates whether the setup was successfully.</returns>
    private async Task AskForKeyAsync(IHost host, CancellationToken cancellationToken)
    {
        host.MarkupNoteLine($"The access key is missing.");
        string secret = await host
            .PromptForSecretAsync("Enter key: ", cancellationToken)
            .ConfigureAwait(false);

        Dirty = true;
        Key = Utils.ConvertToSecureString(secret);
    }

    internal ConfigData ToConfigData()
    {
        return new ConfigData()
        {
            Endpoint = this.Endpoint,
            Deployment = this.Deployment,
            ModelName = this.ModelName,
            AutoExecution = this.AutoExecution,
            DisplayErrors = this.DisplayErrors,
            AuthType = this.AuthType,
            Key = this.Key,
        };
    }
}

internal class ConfigData
{
    public string Endpoint { set; get; }
    public string Deployment { set; get; }
    public string ModelName { set; get; }
    public AuthType AuthType { set; get; } = AuthType.ApiKey;
    public bool? AutoExecution { set; get; }
    public bool? DisplayErrors { set; get; }

    [JsonConverter(typeof(SecureStringJsonConverter))]
    public SecureString Key { set; get; }

    internal bool IsEmpty()
    {
        return string.IsNullOrEmpty(Endpoint)
            && string.IsNullOrEmpty(Deployment)
            && string.IsNullOrEmpty(ModelName);
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
