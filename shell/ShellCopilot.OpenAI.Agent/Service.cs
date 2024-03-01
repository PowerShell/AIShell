using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

using Azure;
using Azure.Core;
using Azure.AI.OpenAI;
using SharpToken;

namespace ShellCopilot.OpenAI.Agent;

internal class ChatService
{
    // TODO: Maybe expose this to our model registration?
    // We can still use 1000 as the default value.
    private const int MaxResponseToken = 1000;
    private readonly bool _isInteractive;
    private readonly string _historyRoot;

    private GPT _gptToUse;
    private Settings _settings;
    private OpenAIClient _client;
    private List<ChatMessage> _chatHistory;

    internal ChatService(bool isInteractive, string historyRoot, Settings settings)
    {
        _isInteractive = isInteractive;
        _historyRoot = historyRoot;
        _settings = settings;
        _chatHistory = new List<ChatMessage>();
    }

    internal void AddResponseToHistory(string response)
    {
        if (string.IsNullOrEmpty(response))
        {
            return;
        }

        _chatHistory.Add(new ChatMessage(ChatRole.Assistant, response));
    }

    internal void RefreshSettings(Settings settings)
    {
        _settings = settings;
    }

    private void LoadHistory(string name)
    {
        string historyFile = Path.Combine(_historyRoot, name);
        if (File.Exists(historyFile))
        {
            using var stream = new FileStream(historyFile, FileMode.Open, FileAccess.Read, FileShare.Read);
            var options = new JsonSerializerOptions
            {
                AllowTrailingCommas = true,
                PropertyNameCaseInsensitive = true,
                ReadCommentHandling = JsonCommentHandling.Skip,
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
            };

            _chatHistory = JsonSerializer.Deserialize<List<ChatMessage>>(stream, options);
        }
    }

    private void SaveHistory(string name)
    {
        string historyFile = Path.Combine(_historyRoot, name);
        using var stream = new FileStream(historyFile, FileMode.Create, FileAccess.Write, FileShare.None);

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
        };

        JsonSerializer.Serialize(stream, _chatHistory, options);
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

        if (old is not null
            && old.Type == _gptToUse.Type
            && string.Equals(old.Endpoint, _gptToUse.Endpoint)
            && string.Equals(old.Deployment, _gptToUse.Deployment)
            && string.Equals(old.ModelName, _gptToUse.ModelName)
            && old.Key.IsEqualTo(_gptToUse.Key))
        {
            // It's the same same endpoint, so we reuse the existing client.
            return;
        }

        var clientOptions = new OpenAIClientOptions() { RetryPolicy = new ChatRetryPolicy() };

        if (_gptToUse.Type is EndpointType.AzureOpenAI)
        {
            // Create a client that targets Azure OpenAI service or Azure API Management service.
            bool isApimEndpoint = _gptToUse.Endpoint.EndsWith(Utils.ApimGatewayDomain);
            if (isApimEndpoint)
            {
                string userkey = Utils.ConvertFromSecureString(_gptToUse.Key);
                clientOptions.AddPolicy(
                    new UserKeyPolicy(
                        new AzureKeyCredential(userkey),
                        Utils.ApimAuthorizationHeader),
                    HttpPipelinePosition.PerRetry
                );
            }

            string azOpenAIApiKey = isApimEndpoint
                ? "placeholder-api-key"
                : Utils.ConvertFromSecureString(_gptToUse.Key);

            _client = new OpenAIClient(
                new Uri(_gptToUse.Endpoint),
                new AzureKeyCredential(azOpenAIApiKey),
                clientOptions);
        }
        else
        {
            // Create a client that targets the non-Azure OpenAI service.
            _client = new OpenAIClient(Utils.ConvertFromSecureString(_gptToUse.Key), clientOptions);
        }
    }

    private int CountTokenForMessages(IEnumerable<ChatMessage> messages)
    {
        ModelInfo modelDetail = _gptToUse.ModelInfo;
        GptEncoding encoding = modelDetail.GptEncoding;
        int tokensPerMessage = modelDetail.TokensPerMessage;
        int tokensPerName = modelDetail.TokensPerName;

        int tokenNumber = 0;
        foreach (ChatMessage message in messages)
        {
            tokenNumber += tokensPerMessage;
            tokenNumber += encoding.Encode(message.Role.ToString()).Count;
            tokenNumber += encoding.Encode(message.Content).Count;

            if (message.Name is not null)
            {
                tokenNumber += tokensPerName;
                tokenNumber += encoding.Encode(message.Name).Count;
            }
        }

        // Every reply is primed with <|start|>assistant<|message|>, which takes 3 tokens.
        tokenNumber += 3;
        return tokenNumber;
    }

    private void ReduceChatHistoryAsNeeded(List<ChatMessage> history, ChatMessage input)
    {
        int totalTokens = CountTokenForMessages(Enumerable.Repeat(input, 1));
        int tokenLimit = _gptToUse.ModelInfo.TokenLimit;

        if (totalTokens + MaxResponseToken >= tokenLimit)
        {
            var message = $"The input is too long to get a proper response without exceeding the token limit ({tokenLimit}).\nPlease reduce the input and try again.";
            throw new InvalidOperationException(message);
        }

        history.Add(input);
        totalTokens = CountTokenForMessages(history);

        while (totalTokens + MaxResponseToken >= tokenLimit)
        {
            history.RemoveAt(0);
            totalTokens = CountTokenForMessages(history);
        }
    }

    private ChatCompletionsOptions PrepareForChat(string input)
    {
        // Refresh the client in case the active model was changed.
        RefreshOpenAIClient();

        // TODO: Shall we expose some of the setting properties to our model registration?
        //  - max_tokens
        //  - temperature
        //  - top_p
        //  - presence_penalty
        //  - frequency_penalty
        // Those settings seem to be important enough, as the Semantic Kernel plugin specifies
        // those settings (see the URL below). We can use default values when not defined.
        // https://github.com/microsoft/semantic-kernel/blob/main/samples/skills/FunSkill/Joke/config.json
        ChatCompletionsOptions chatOptions = new()
        {
            ChoiceCount = 1,
            Temperature = 0,
            MaxTokens = MaxResponseToken,
        };

        List<ChatMessage> history = _isInteractive ? _chatHistory : [];
        if (history.Count is 0)
        {
            history.Add(new ChatMessage(ChatRole.System, _gptToUse.SystemPrompt));
        }

        ReduceChatHistoryAsNeeded(history, new ChatMessage(ChatRole.User, input));
        foreach (ChatMessage message in history)
        {
            chatOptions.Messages.Add(message);
        }

        return chatOptions;
    }

    public async Task<ChatChoice> GetChatResponseAsync(string input, CancellationToken cancellationToken = default)
    {
        try
        {
            ChatCompletionsOptions chatOptions = PrepareForChat(input);
            string deploymentOrModelName = _gptToUse.Type switch
            {
                EndpointType.AzureOpenAI => _gptToUse.Deployment,
                EndpointType.OpenAI => _gptToUse.ModelName,
                _ => throw new UnreachableException(),
            };

            var response = await _client.GetChatCompletionsAsync(
                deploymentOrModelName,
                chatOptions,
                cancellationToken);

            return response.Value.Choices[0];
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    public async Task<StreamingChatCompletions> GetStreamingChatResponseAsync(string input, CancellationToken cancellationToken = default)
    {
        try
        {
            ChatCompletionsOptions chatOptions = PrepareForChat(input);
            string deploymentOrModelName = _gptToUse.Type switch
            {
                EndpointType.AzureOpenAI => _gptToUse.Deployment,
                EndpointType.OpenAI => _gptToUse.ModelName,
                _ => throw new UnreachableException(),
            };

            var response = await _client.GetChatCompletionsStreamingAsync(
                deploymentOrModelName,
                chatOptions,
                cancellationToken);

            return response.Value;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }
}
