using System.ClientModel;
using System.ClientModel.Primitives;
using Azure.AI.OpenAI;
using Azure.Identity;
using Microsoft.Extensions.AI;
using OpenAI;

using OpenAIChatClient = OpenAI.Chat.ChatClient;
using ChatMessage = Microsoft.Extensions.AI.ChatMessage;
using AIShell.Abstraction;

namespace AIShell.OpenAI.Agent;

internal class ChatService
{
    // TODO: Maybe expose this to our model registration?
    private const int MaxResponseToken = 1000;
    private readonly string _historyRoot;
    private readonly List<ChatMessage> _chatHistory;
    private readonly ChatOptions _chatOptions;

    private GPT _gptToUse;
    private Settings _settings;
    private IChatClient _client;

    internal ChatService(string historyRoot, Settings settings)
    {
        _chatHistory = [];
        _historyRoot = historyRoot;
        _settings = settings;

        _chatOptions = new ChatOptions()
        {
            MaxOutputTokens = MaxResponseToken,
        };
    }

    internal List<ChatMessage> ChatHistory => _chatHistory;

    internal void RefreshSettings(Settings settings)
    {
        _settings = settings;
    }

    private void RefreshOpenAIClient()
    {
        if (ReferenceEquals(_gptToUse, _settings.Active))
        {
            // Active GPT was not changed.
            return;
        }

        GPT old = _gptToUse;
        _gptToUse = _settings.Active;
        _chatHistory.Clear();

        if (old is not null
            && old.Type == _gptToUse.Type
            && string.Equals(old.Endpoint, _gptToUse.Endpoint)
            && string.Equals(old.Deployment, _gptToUse.Deployment)
            && string.Equals(old.ModelName, _gptToUse.ModelName)
            && old.AuthType == _gptToUse.AuthType
            && (old.AuthType is AuthType.EntraID || old.Key.IsEqualTo(_gptToUse.Key)))
        {
            // It's the same endpoint and auth type, so we reuse the existing client.
            return;
        }

        OpenAIChatClient client;
        EndpointType type = _gptToUse.Type;
        // Reasoning models do not support the temperature setting.
        _chatOptions.Temperature = _gptToUse.ModelInfo.Reasoning ? null : 0.5f;

        if (type is EndpointType.AzureOpenAI)
        {
            // Create a client that targets Azure OpenAI service or Azure API Management service.
            var clientOptions = new AzureOpenAIClientOptions() { RetryPolicy = new ChatRetryPolicy() };
            bool isApimEndpoint = _gptToUse.Endpoint.EndsWith(Utils.ApimGatewayDomain);

            if (_gptToUse.AuthType is AuthType.ApiKey)
            {
                string userKey = Utils.ConvertFromSecureString(_gptToUse.Key);

                if (isApimEndpoint)
                {
                    clientOptions.AddPolicy(
                        ApiKeyAuthenticationPolicy.CreateHeaderApiKeyPolicy(
                            new ApiKeyCredential(userKey),
                            Utils.ApimAuthorizationHeader),
                        PipelinePosition.PerTry);
                }

                string azOpenAIApiKey = isApimEndpoint ? "placeholder-api-key" : userKey;

                var aiClient = new AzureOpenAIClient(
                    new Uri(_gptToUse.Endpoint),
                    new ApiKeyCredential(azOpenAIApiKey),
                    clientOptions);

                client = aiClient.GetChatClient(_gptToUse.Deployment);
            }
            else
            {
                var credential = new DefaultAzureCredential(includeInteractiveCredentials: true);

                var aiClient = new AzureOpenAIClient(
                    new Uri(_gptToUse.Endpoint),
                    credential,
                    clientOptions);

                client = aiClient.GetChatClient(_gptToUse.Deployment);
            }
        }
        else
        {
            // Create a client that targets the non-Azure OpenAI service.
            var clientOptions = new OpenAIClientOptions() { RetryPolicy = new ChatRetryPolicy() };
            if (type is EndpointType.CompatibleThirdParty)
            {
                clientOptions.Endpoint = new(_gptToUse.Endpoint);
            }

            string userKey = Utils.ConvertFromSecureString(_gptToUse.Key);
            var aiClient = new OpenAIClient(new ApiKeyCredential(userKey), clientOptions);
            client = aiClient.GetChatClient(_gptToUse.ModelName);
        }

        _client = client.AsIChatClient()
            .AsBuilder()
            .UseFunctionInvocation(configure: c => c.IncludeDetailedErrors = true)
            .Build();
    }

    private void PrepareForChat(string input, IShell shell)
    {
        // Refresh the client in case the active model was changed.
        RefreshOpenAIClient();

        if (_chatHistory.Count is 0)
        {
            string system = _gptToUse.SystemPrompt;
            if (string.IsNullOrEmpty(system))
            {
                system = shell.ChannelEstablished
                    ? Prompt.SystemPromptWithConnectedPSSession
                    : Prompt.SystemPrompForStandaloneApp;
            }

            _chatHistory.Add(new(ChatRole.System, system));
        }

        _chatHistory.Add(new(ChatRole.User, input));
    }

    public async Task<IAsyncEnumerator<ChatResponseUpdate>> GetStreamingChatResponseAsync(string input, IShell shell, CancellationToken cancellationToken)
    {
        try
        {
            PrepareForChat(input, shell);

            var tools = await shell.GetAIFunctions();
            if (tools is { Count: > 0 })
            {
                _chatOptions.Tools = [.. tools];
            }

            IAsyncEnumerator<ChatResponseUpdate> enumerator = _client
                .GetStreamingResponseAsync(_chatHistory, _chatOptions, cancellationToken)
                .GetAsyncEnumerator(cancellationToken);

            return await enumerator
                .MoveNextAsync()
                .ConfigureAwait(continueOnCapturedContext: false) ? enumerator : null;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }
}
