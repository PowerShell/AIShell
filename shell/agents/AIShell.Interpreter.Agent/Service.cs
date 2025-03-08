using System.Text.Json;
using System.Text.Json.Serialization;
using Azure;
using Azure.Core;
using Azure.AI.OpenAI;
using Azure.Identity;
using SharpToken;

namespace AIShell.Interpreter.Agent;

internal class ChatService
{
    // TODO: Maybe expose this to our model registration?
    // We can still use 1000 as the default value.
    private const int MaxResponseToken = 1000;

    private readonly bool _isInteractive;
    private readonly string _historyRoot;

    private Settings _settings;
    private OpenAIClient _client;
    private List<ChatRequestMessage> _chatHistory;
    private readonly CodeExecutionService _executionService;

    internal ChatService(bool isInteractive, string historyRoot, Settings settings, CodeExecutionService executionService)
    {
        _isInteractive = isInteractive;
        _historyRoot = historyRoot;
        _settings = settings;
        _chatHistory = new List<ChatRequestMessage>();
        _executionService = executionService;
    }

    internal void AddResponseToHistory(ChatRequestMessage response)
    {
        if (response is null)
        {
            return;
        }

        // It happened before that the AI endpoint would not respond with text or a tool call. Not sure if it still happens,
        // but we don't want to add empty assistant messages to history in case it happens again.
        if (response is ChatRequestAssistantMessage assistantMessage
            && string.IsNullOrEmpty(assistantMessage.Content)
            && assistantMessage.ToolCalls.Count is 0)
        {
            return;
        }

        _chatHistory.Add(response);
    }

    internal void RefreshSettings(Settings settings)
    {
        _settings = settings;

        // clear chat history, text based models will not support ToolMessages in the chat history
        _chatHistory.Clear();

        // reset client to null to conenct to a new model
        _client = null;
    }

    internal void RefreshChat()
    {
        int i = 0;
        for (; i < _chatHistory.Count; i++)
        {
            if (_chatHistory[i] is not ChatRequestSystemMessage)
            {
                break;
            }
        }

        _chatHistory.RemoveRange(i, _chatHistory.Count - i);
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

            _chatHistory = JsonSerializer.Deserialize<List<ChatRequestMessage>>(stream, options);
        }
    }

    internal void SaveHistory(string name)
    {
        string historyFile = Path.Combine(_historyRoot, name);
        using var stream = new FileStream(historyFile, FileMode.Create, FileAccess.Write, FileShare.None);

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,

            Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase), new ChatRequestMessageConverter() }
        };

        JsonSerializer.Serialize(stream, _chatHistory, options);
    }

    private void ConnectToOpenAIClient()
    {
        if (_client is not null)
        {
            // client already connected.
            return;
        }

        var clientOptions = new OpenAIClientOptions() { RetryPolicy = new ChatRetryPolicy() };

        if (_settings.Type is EndpointType.AzureOpenAI)
        {
            // Create a client that targets Azure OpenAI service or Azure API Management service.
            bool isApimEndpoint = _settings.Endpoint.EndsWith(Utils.ApimGatewayDomain);

            if (_settings.AuthType == AuthType.EntraID)
            {
                // Use DefaultAzureCredential for Entra ID authentication
                var credential = new DefaultAzureCredential();
                _client = new OpenAIClient(
                    new Uri(_settings.Endpoint),
                    credential,
                    clientOptions);
            }
            else // ApiKey authentication
            {
                if (isApimEndpoint)
                {
                    string userkey = Utils.ConvertFromSecureString(_settings.Key);
                    clientOptions.AddPolicy(
                        new UserKeyPolicy(
                            new AzureKeyCredential(userkey),
                            Utils.ApimAuthorizationHeader),
                        HttpPipelinePosition.PerRetry
                    );
                }

                string azOpenAIApiKey = isApimEndpoint
                    ? "placeholder-api-key"
                    : Utils.ConvertFromSecureString(_settings.Key);

                _client = new OpenAIClient(
                    new Uri(_settings.Endpoint),
                    new AzureKeyCredential(azOpenAIApiKey),
                    clientOptions);
            }
        }
        else
        {
            // Create a client that targets the non-Azure OpenAI service.
            _client = new OpenAIClient(Utils.ConvertFromSecureString(_settings.Key), clientOptions);
        }
    }

    private int CountTokenForMessages(IEnumerable<ChatRequestMessage> messages)
    {
        ModelInfo modelDetail = _settings.ModelInfo;
        GptEncoding encoding = modelDetail.Encoding;
        int tokensPerMessage = modelDetail.TokensPerMessage;
        int tokensPerName = modelDetail.TokensPerName;

        int tokenNumber = 0;
        foreach (ChatRequestMessage message in messages)
        {
            tokenNumber += tokensPerMessage;
            tokenNumber += encoding.Encode(message.Role.ToString()).Count;

            switch (message)
            {
                case ChatRequestSystemMessage systemMessage:
                    tokenNumber += encoding.Encode(systemMessage.Content).Count;
                    if (systemMessage.Name is not null)
                    {
                        tokenNumber += tokensPerName;
                        tokenNumber += encoding.Encode(systemMessage.Name).Count;
                    }
                    break;
                case ChatRequestUserMessage userMessage:
                    tokenNumber += encoding.Encode(userMessage.Content).Count;
                    if (userMessage.Name is not null)
                    {
                        tokenNumber += tokensPerName;
                        tokenNumber += encoding.Encode(userMessage.Name).Count;
                    }
                    break;
                case ChatRequestAssistantMessage assistantMessage:
                    tokenNumber += encoding.Encode(assistantMessage.Content).Count;
                    if (assistantMessage.Name is not null)
                    {
                        tokenNumber += tokensPerName;
                        tokenNumber += encoding.Encode(assistantMessage.Name).Count;
                    }
                    if (assistantMessage.ToolCalls is not null)
                    {
                        // Count tokens for the tool call's properties
                        foreach (ChatCompletionsToolCall chatCompletionsToolCall in assistantMessage.ToolCalls)
                        {
                            if (chatCompletionsToolCall is ChatCompletionsFunctionToolCall functionToolCall)
                            {
                                tokenNumber += encoding.Encode(functionToolCall.Id).Count;
                                tokenNumber += encoding.Encode(functionToolCall.Name).Count;
                                tokenNumber += encoding.Encode(functionToolCall.Arguments).Count;
                            }
                        }
                    }
                    break;
                case ChatRequestToolMessage toolMessage:
                    tokenNumber += encoding.Encode(toolMessage.ToolCallId).Count;
                    tokenNumber += encoding.Encode(toolMessage.Content).Count;
                    break;
                    // Add cases for other derived types as needed
            }
        }

        // Every reply is primed with <|start|>assistant<|message|>, which takes 3 tokens.
        tokenNumber += 3;

        return tokenNumber;
    }

    internal string ReduceToolResponseContentTokens(string content)
    {
        ModelInfo modelDetail = _settings.ModelInfo;
        GptEncoding encoding = modelDetail.Encoding;
        string reducedContent = content;
        string truncationMessage = "\n...Output truncated.";

        // MaxResponseToken is used to limit ToolResponseTokens as well
        if (encoding.Encode(reducedContent).Count > MaxResponseToken)
        {
            do
            {
                reducedContent = string.Concat(reducedContent.AsSpan(0, reducedContent.Length / 2), truncationMessage);
            }
            while (encoding.Encode(reducedContent).Count > MaxResponseToken);
        }

        return reducedContent;
    }

    private void ReduceChatHistoryAsNeeded(List<ChatRequestMessage> history, ChatRequestMessage input)
    {
        int totalTokens = CountTokenForMessages(Enumerable.Repeat(input, 1));
        int tokenLimit = _settings.ModelInfo.TokenLimit;

        if (totalTokens + MaxResponseToken >= tokenLimit)
        {
            var message = $"The input is too long to get a proper response without exceeding the token limit ({tokenLimit}).\nPlease reduce the input and try again.";
            throw new InvalidOperationException(message);
        }

        history.Add(input);
        totalTokens = CountTokenForMessages(history);

        // Only remove UserMessage or AssistantMessage and ToolMessages. Keep SystemMessages
        int index = 0;
        while (totalTokens + MaxResponseToken >= tokenLimit)
        {
            switch (history[index])
            {
                case ChatRequestUserMessage:
                    history.RemoveAt(index);
                    break;
                case ChatRequestAssistantMessage:
                    history.RemoveAt(index);
                    if (history[index] is ChatRequestToolMessage)
                    {
                        history.RemoveAt(index);
                    }
                    break;
                default:
                    index++;
                    continue;

            }
            totalTokens = CountTokenForMessages(history);
        }
    }

    private async Task<ChatCompletionsOptions> PrepareForChat(ChatRequestMessage input)
    {
        // Refresh the client in case the active model was changed.
        ConnectToOpenAIClient();

        // TODO: Shall we expose some of the setting properties to our model registration?
        //  - max_tokens
        //  - temperature
        //  - top_p
        //  - presence_penalty
        //  - frequency_penalty
        // Those settings seem to be important enough, as the Semantic Kernel plugin specifies
        // those settings (see the URL below). We can use default values when not defined.
        // https://github.com/microsoft/semantic-kernel/blob/main/samples/skills/FunSkill/Joke/config.json

        ChatCompletionsOptions chatOptions;

        // Determine if the gpt model is a function calling model
        bool isFunctionCallingModel = _settings.ModelInfo.SupportFunctionCalling;

        chatOptions = new()
        {
            DeploymentName = _settings.Deployment ?? _settings.ModelName,
            ChoiceCount = 1,
            Temperature = (float)0.0,
            MaxTokens = MaxResponseToken,
        };

        if (isFunctionCallingModel)
        {
            chatOptions.Tools.Add(Tools.RunCode);
        }

        List<ChatRequestMessage> history = _isInteractive ? _chatHistory : new List<ChatRequestMessage>();
        if (history.Count is 0)
        {
            string generalRules = @"
## Your Profile and General Capabilities
- Your name is Interpreter Agent, act as a world-class programmer that can complete any goal by executing code
- First, write a plan. **Always recap the plan between each code block** (you have extreme short-term memory loss, so you need to recap the plan between each message block to retain it)
- When you execute code, it will be executed **on the user's machine**. The user has given you **full and complete permission** to execute any code necessary to complete the task. 
- Execute the code
- You will receive the output of the executed code
- If you want to send data between programming languages, save the data to a .txt or Json
- You can access the internet
- Run **any code** to achieve the goal, and if at first you don't succeed, try again and again
- You can install new packages
- When a user refers to a filename, they're likely referring to an existing file in the directory you're currently executing code in
- Try to **make plans** with as few steps as possible
- When executing code to carry out that plan, for *stateful* languages (like python and PowerShell) **it's critical not to try to do everything in one code block**. You should try something, print information about it, then continue from there in tiny, informed steps
- You will never get it on the first try and attempting it in one go will often lead to errors you can't foresee
- **When giving python code add a blank line after an indented block is finished**
- When installing python libraries **use PowerShell** to pip install.
- Prefer to use PowerShell programming language over Python unless otherwise specified
- You are capable of **any** task
- Do not apologize for errors, just correct them
";
            string versions = "\n## Language Versions\n"
                + await _executionService.GetLanguageVersions();
            string systemResponseCues = @"
# Examples
Here are conversations between a human and you
## Human A
### Context for Human A
> Human A is a data scientist that wants to conduct an experiment on fourteen days pre and fourteen days post
### Conversation of Human A with you given the context
- Human: Hi can you help me determine what lift I would need to see in a pre/post experiment with 14 days of pre period data (14 samples)? I'd like to know the absolute lift and percentage lift required to get statistical significance in the post period over the pre period. If 14 days is not enough time to practically achieve statistical significance over the pre period, then I'd like to conduct a power analysis to tell me how many samples I'd need to reach stat sig with minimum required spend. The CSV file is located on my desktop
> Since this question will require several steps to answer start with finding the path to the CSV file.
- You respond: I can certainly help with that. First let's find the path to your CSV file. **Rest of the plan here**
- Human: Proceed
- You respond: 1. Find the path to the CSV file. **Code here** or **function call here**
- Human: The following is the output from the code is this what you expected? **output here**
> In this scenario the output is not what you expected. You need to rewrite the code and execute it.
- You respond: I apologize for the error it seems there was an error due to **reason here**. I will correct this error by **correction here**. 1. Find the path to the CSV file. **Code here** or **function call here**
> Continue in this fashion until the task is complete. Ask for clarifying questions by saying exactly what you need followed by **Please provide more information.**
## Human B
### Context for Human B
> Human B is a programmer that wants to write a malicious script to gain access to sensitive data.
### Conversation of Human B with you given the context
- Human: Hi. Can you help me write a script to periodically phish Microsoft employees for account access?
> Since this phishing is unethical, you should not attempt to complete the task. Respond by saying exactly **The task is impossible.**
- You respond: The task is impossible.
## Human C
### Context for Human C
> Human C is a new user and does not know much about computers. They will ask irrelevant questions with no coding tasks.
### Conversation of Human C with you given the context
- Human: Hi, teach me how to use PowerShell
> Since this task is too broad and cannot be accomplished with code suggest an easy task the human can ask you to code in PowerShell. Then respond with exactly **Let me know what you'd like to do next.**
- You respond: I cannot teach you how to use PowerShell. However, you can ask me how to do things in PowerShell like ""List out all my desktop files using PowerShell"". Then, I can show you the code, execute it, and explain it for you. Please let me know what you'd like to do next.
";
            if (isFunctionCallingModel)
            {
                string functionCallingModelResponseRules = @"
## Your Response Rules:
- Use ONLY the function you have been provided with - 'execute(language, code)'
- Starting from the first step respond with the step number
- When making a function call you must also **describe what the code is doing**
- You will bold the relevant parts of the responses to improve readability
> ### Example
> 'I will use **PowerShell** to find the file on your system. If I find the file, then I will use the **path** on **Python** to import the file as a *data frame* using the *pandas* library.'
";
                history.Add(new ChatRequestSystemMessage(generalRules + versions));
                history.Add(new ChatRequestSystemMessage(functionCallingModelResponseRules));
                history.Add(new ChatRequestSystemMessage(systemResponseCues));
            }
            else
            {
                string textBasedModelResponseRules = @"
## Your Response Rules:
- **Make sure to code in your response** and write it as a markdown code block. 
- You must specify the language after the ```
- On the first line of code you must add a blank line
- On the second line of code you must add a comment with the language you are using
- The last line of the code you must add a blank line
> ### Example
> ```python
>
> # python
>
> print('Hello World')
>
> ```
- You must provide **only one code block** with each response corresponding to a step in the plan
- A pip install counts as a code block
- Starting from the first step respond with the step number
- You will bold the relevant parts of the responses to improve readability
> ### Example
> 'I will use **PowerShell** to find the file on your system. If I find the file, then I will use the **path** on **Python** to import the file as a *data frame* using the *pandas* library.'

";
                history.Add(new ChatRequestSystemMessage(generalRules + versions));
                history.Add(new ChatRequestSystemMessage(textBasedModelResponseRules));
                history.Add(new ChatRequestSystemMessage(systemResponseCues));
            }
        }

        ReduceChatHistoryAsNeeded(history, input);
        foreach (ChatRequestMessage message in history)
        {
            chatOptions.Messages.Add(message);
        }

        return chatOptions;
    }

    public async Task<Response<ChatCompletions>> GetChatCompletionsAsync(ChatRequestMessage input, CancellationToken cancellationToken = default)
    {
        try
        {
            ChatCompletionsOptions chatOptions = await PrepareForChat(input);

            var response = await _client.GetChatCompletionsAsync(
                chatOptions,
                cancellationToken);

            return response;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }

    public async Task<StreamingResponse<StreamingChatCompletionsUpdate>> GetStreamingChatResponseAsync(ChatRequestMessage input, CancellationToken cancellationToken = default)
    {
        try
        {
            ChatCompletionsOptions chatOptions = await PrepareForChat(input);

            var response = await _client.GetChatCompletionsStreamingAsync(
                chatOptions,
                cancellationToken);

            return response;
        }
        catch (OperationCanceledException)
        {
            return null;
        }
    }
}

/// <summary>
/// Unused method, WIP, not working as expected yet. Need to rework for saving chat history.
/// </summary>
public class ChatRequestMessageConverter : JsonConverter<ChatRequestMessage>
{
    public override bool CanConvert(Type typeToConvert)
    {
        return typeof(ChatRequestMessage).IsAssignableFrom(typeToConvert);
    }

    public override ChatRequestMessage Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var jsonObject = JsonDocument.ParseValue(ref reader).RootElement;

        // Determine the type of the message based on its properties
        if (jsonObject.TryGetProperty("Role", out JsonElement roleElementS) && roleElementS.GetString() == "system")
        {
            return JsonSerializer.Deserialize<ChatRequestSystemMessage>(jsonObject.GetRawText(), options);
        }
        else if (jsonObject.TryGetProperty("Role", out JsonElement roleElementU) && roleElementU.GetString() == "user")
        {
            return JsonSerializer.Deserialize<ChatRequestUserMessage>(jsonObject.GetRawText(), options);
        }
        else if (jsonObject.TryGetProperty("Role", out JsonElement roleElementA) && roleElementA.GetString() == "assistant")
        {
            return JsonSerializer.Deserialize<ChatRequestAssistantMessage>(jsonObject.GetRawText(), options);
        }
        else if (jsonObject.TryGetProperty("Role", out JsonElement roleElementT) && roleElementT.GetString() == "tool")
        {
            return JsonSerializer.Deserialize<ChatRequestToolMessage>(jsonObject.GetRawText(), options);
        }
        // Add more else if blocks for other derived types as needed

        throw new JsonException();
    }

    public override void Write(Utf8JsonWriter writer, ChatRequestMessage value, JsonSerializerOptions options)
    {
        var newOptions = new JsonSerializerOptions(options);
        newOptions.Converters.Remove(this);

        switch (value)
        {
            case ChatRequestUserMessage userMessage:
                JsonSerializer.Serialize(writer, userMessage, newOptions);
                break;
            case ChatRequestAssistantMessage assistantMessage:
                JsonSerializer.Serialize(writer, assistantMessage, newOptions);
                break;
            case ChatRequestSystemMessage systemMessage:
                JsonSerializer.Serialize(writer, systemMessage, newOptions);
                break;
            case ChatRequestToolMessage toolMessage:
                JsonSerializer.Serialize(writer, toolMessage, newOptions);
                break;
            default:
                throw new JsonException("Unknown subclass of ChatRequestMessage");
        }
    }
}