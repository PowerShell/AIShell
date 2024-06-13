using System.Diagnostics;
using System.Security;
using System.Text.Json.Serialization;
using AIShell.Abstraction;

namespace AIShell.OpenAI.Agent;

internal enum EndpointType
{
    AzureOpenAI,
    OpenAI,
}

public class GPT
{
    internal EndpointType Type { get; }
    internal bool Dirty { set; get; }
    internal ModelInfo ModelInfo { private set; get; }

    public string Name { set; get; }
    public string Description { set; get; } 
    public string Endpoint { set; get; }
    public string Deployment { set; get; }
    public string ModelName { set; get; }

    [JsonConverter(typeof(SecureStringJsonConverter))]
    public SecureString Key { set; get; }
    public string SystemPrompt { set; get; }

    public GPT(
        string name,
        string description,
        string endpoint,
        string deployment,
        string modelName,
        string systemPrompt,
        SecureString key)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(description);
        ArgumentException.ThrowIfNullOrEmpty(modelName);
        ArgumentException.ThrowIfNullOrEmpty(systemPrompt);

        Name = name;
        Description = description;
        Endpoint = endpoint?.Trim().TrimEnd('/');
        Deployment = deployment;
        ModelName = modelName.ToLowerInvariant();
        SystemPrompt = systemPrompt;
        Key = key;

        Dirty = false;
        ModelInfo = ModelInfo.TryResolve(ModelName, out var model) ? model : null;

        bool noEndpoint = string.IsNullOrEmpty(Endpoint);
        bool noDeployment = string.IsNullOrEmpty(Deployment);
        Type = noEndpoint && noDeployment
            ? EndpointType.OpenAI
            : !noEndpoint && !noDeployment
                ? EndpointType.AzureOpenAI
                : throw new InvalidOperationException($"Invalid setting: {(noEndpoint ? "Endpoint" : "Deployment")} key is missing. To use Azure OpenAI service, please specify both the 'Endpoint' and 'Deployment' keys. To use OpenAI service, please ignore both keys.");
    }

    /// <summary>
    /// Self check 
    /// </summary>
    /// <returns></returns>
    internal async Task<bool> SelfCheck(IHost host, CancellationToken token)
    {
        if (Key is not null && ModelInfo is not null)
        {
            return true;
        }

        host.WriteLine()
            .MarkupNoteLine($"Some required information is missing for the GPT [green]'{Name}'[/]:");
        ShowEndpointInfo(host);

        try
        {
            if (ModelInfo is null)
            {
                await AskForModel(host, token);
            }

            if (Key is null)
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

    /// <summary>
    /// Validate the model name and prompt for fixing it if it's invalid.
    /// </summary>
    /// <returns>A boolean value indicates whether the validation and setup was successful.</returns>
    private async Task AskForModel(IHost host, CancellationToken cancellationToken)
    {
        host.WriteErrorLine($"'{ModelName}' is not a supported OpenAI chat completion model.");
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

    private void ShowEndpointInfo(IHost host)
    {
        var elements = Type switch
        {
            EndpointType.AzureOpenAI => new CustomElement<GPT>[]
                {
                    new(label: "  Type", m => m.Type.ToString()),
                    new(label: "  Endpoint", m => m.Endpoint),
                    new(label: "  Deployment", m => m.Deployment),
                    new(label: "  Model", m => m.ModelName),
                },

            EndpointType.OpenAI => new CustomElement<GPT>[]
                {
                    new(label: "  Type", m => m.Type.ToString()),
                    new(label: "  Model", m => m.ModelName),
                },

            _ => throw new UnreachableException(),
        };

        host.RenderList(this, elements);
    }
}
