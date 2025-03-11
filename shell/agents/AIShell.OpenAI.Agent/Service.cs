using System.ClientModel;
using System.ClientModel.Primitives;
using Azure.AI.OpenAI;
using Azure.Core;
using Azure.Identity;
using Microsoft.ML.Tokenizers;
using OpenAI;
using OpenAI.Chat;

namespace AIShell.OpenAI.Agent;

internal class ChatService
{
    // TODO: Maybe expose this to our model registration?
    // We can still use 1000 as the default value.
    private const int MaxResponseToken = 2000;
    private readonly string _historyRoot;
    private readonly List<ChatMessage> _chatHistory;
    private readonly List<int> _chatHistoryTokens;
    private readonly ChatCompletionOptions _chatOptions;

    private GPT _gptToUse;
    private Settings _settings;
    private ChatClient _client;
    private int _totalInputToken;

    internal ChatService(string historyRoot, Settings settings)
    {
        _chatHistory = [];
        _chatHistoryTokens = [];
        _historyRoot = historyRoot;

        _totalInputToken = 0;
        _settings = settings;

        _chatOptions = new ChatCompletionOptions()
        {
            Temperature = 0,
            MaxOutputTokenCount = MaxResponseToken,
        };
    }

    internal List<ChatMessage> ChatHistory => _chatHistory;

    internal void AddResponseToHistory(string response)
    {
        if (string.IsNullOrEmpty(response))
        {
            return;
        }

        _chatHistory.Add(ChatMessage.CreateAssistantMessage(response));
    }

    internal void RefreshSettings(Settings settings)
    {
        _settings = settings;
    }

    /// <summary>
    /// It's almost impossible to relative-accurately calculate the token counts of all
    /// messages, especially when tool calls are involved (tool call definitions and the
    /// tool call payloads in AI response).
    /// So, I decide to leverage the useage report from AI to track the token count of
    /// the chat history. It's also an estimate, but I think more accurate than doing the
    /// counting by ourselves.
    /// </summary>
    internal void CalibrateChatHistory(ChatTokenUsage usage, AssistantChatMessage response)
    {
        if (usage is null)
        {
            // Response was cancelled and we will remove the last query from history.
            int index = _chatHistory.Count - 1;
            _chatHistory.RemoveAt(index);
            _chatHistoryTokens.RemoveAt(index);

            return;
        }

        // Every reply is primed with <|start|>assistant<|message|>, so we subtract 3 from the 'InputTokenCount'.
        int promptTokenCount = usage.InputTokenCount - 3;
        // 'ReasoningTokenCount' should be 0 for non-o1 models.
        int reasoningTokenCount = usage.OutputTokenDetails is null ? 0 : usage.OutputTokenDetails.ReasoningTokenCount;
        int responseTokenCount = usage.OutputTokenCount - reasoningTokenCount;

        if (_totalInputToken is 0)
        {
            // It was the first user message, so instead of adjusting the user message token count,
            // we set the token count for system message and tool calls.
            _chatHistoryTokens[0] = promptTokenCount - _chatHistoryTokens[^1];
        }
        else
        {
            // Adjust the token count of the user message, as our calculation is an estimate.
            _chatHistoryTokens[^1] = promptTokenCount - _totalInputToken;
        }

        _chatHistory.Add(response);
        _chatHistoryTokens.Add(responseTokenCount);
        _totalInputToken = promptTokenCount + responseTokenCount;
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
        _chatHistoryTokens.Clear();

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

        EndpointType type = _gptToUse.Type;

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

                _client = aiClient.GetChatClient(_gptToUse.Deployment);
            }
            else
            {
                var credential = new DefaultAzureCredential();

                var aiClient = new AzureOpenAIClient(
                    new Uri(_gptToUse.Endpoint),
                    credential,
                    clientOptions);

                _client = aiClient.GetChatClient(_gptToUse.Deployment);
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
            _client = aiClient.GetChatClient(_gptToUse.ModelName);
        }
    }

    /// <summary>
    /// Reference: https://github.com/openai/openai-cookbook/blob/main/examples/How_to_count_tokens_with_tiktoken.ipynb
    /// </summary>
    private int CountTokenForUserMessage(UserChatMessage message)
    {
        ModelInfo modelDetail = _gptToUse.ModelInfo;
        Tokenizer encoding = modelDetail.Encoding;

        // Tokens per message plus 1 token for the role.
        int tokenNumber = modelDetail.TokensPerMessage + 1;
        foreach (ChatMessageContentPart part in message.Content)
        {
            tokenNumber += encoding.CountTokens(part.Text);
        }

        return tokenNumber;
    }

    private void PrepareForChat(string input)
    {
        // Refresh the client in case the active model was changed.
        RefreshOpenAIClient();

        if (_chatHistory.Count is 0)
        {
            _chatHistory.Add(ChatMessage.CreateSystemMessage(_gptToUse.SystemPrompt));
            _chatHistoryTokens.Add(0);
        }

        var userMessage = new UserChatMessage(input);
        int msgTokenCnt = CountTokenForUserMessage(userMessage);
        _chatHistory.Add(userMessage);
        _chatHistoryTokens.Add(msgTokenCnt);

        int inputLimit = _gptToUse.ModelInfo.TokenLimit;
        // Every reply is primed with <|start|>assistant<|message|>, so adding 3 tokens.
        int newTotal = _totalInputToken + msgTokenCnt + 3;

        // Shrink the chat history if we have less than 50 free tokens left (50-token buffer).
        while (inputLimit - newTotal < 50)
        {
            // We remove a round of conversation for every trimming operation.
            int userMsgCnt = 0;
            List<int> indices = [];

            for (int i = 0; i < _chatHistory.Count; i++)
            {
                if (_chatHistory[i] is UserChatMessage)
                {
                    if (userMsgCnt is 1)
                    {
                        break;
                    }

                    userMsgCnt++;
                }

                if (userMsgCnt is 1)
                {
                    indices.Add(i);
                }
            }

            foreach (int i in indices)
            {
                newTotal -= _chatHistoryTokens[i];
            }

            _chatHistory.RemoveRange(indices[0], indices.Count);
            _chatHistoryTokens.RemoveRange(indices[0], indices.Count);
            _totalInputToken = newTotal - msgTokenCnt;
        }
    }

    public async Task<IAsyncEnumerator<StreamingChatCompletionUpdate>> GetStreamingChatResponseAsync(string input, CancellationToken cancellationToken)
    {
        try
        {
            PrepareForChat(input);
            IAsyncEnumerator<StreamingChatCompletionUpdate> enumerator = _client
                .CompleteChatStreamingAsync(_chatHistory, _chatOptions, cancellationToken)
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
