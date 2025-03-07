using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

using AIShell.Abstraction;
using Azure.Identity;
using Serilog;

namespace Microsoft.Azure.Agent;

public sealed class AzureAgent : ILLMAgent
{
    public string Name { get; }
    public string Company { get; }
    public string Description { get; }
    public List<string> SampleQueries { get; }
    public Dictionary<string, string> LegalLinks { get; }
    public string SettingFile { private set; get; }

    internal ArgumentPlaceholder ArgPlaceholder { set; get; }
    internal CopilotResponse CopilotResponse => _copilotResponse;

    private const string SettingFileName = "az.config.json";
    private const string LoggingFileName = "log..txt";
    private const string InstructionPrompt = """
        NOTE: Follow the instructions below when generating Azure CLI or Azure PowerShell commands with placeholders:
        1. The targeting OS is '{0}'.
        2. Always assume the user has logged in Azure and a resource group already exists.
        3. DO NOT include any additional examples with made-up values.
        4. DO NOT use the line continuation operator (backslash `\`) in commands.
        5. Always represent a placeholder in the form of `<placeholder-name>` and enclose it within double quotes.
        6. Always use the consistent placeholder names across all your responses. For example, `<resourceGroupName>` should be used for all the places where a resource group name value is needed.
        7. When the commands contain placeholders, the placeholders should be summarized in markdown bullet points at the end of the response in the same order as they appear in the commands, following this format:
           ```
           Placeholders:
           - `<first-placeholder>`: <concise-description>
           - `<second-placeholder>`: <concise-description>
           ```
        8. DO NOT include the placeholder summary when the commands contains no placeholder.
        """;

    private int _turnsLeft;
    private CopilotResponse _copilotResponse;
    private AgentSetting _setting;

    private readonly string _instructions;
    private readonly StringBuilder _buffer;
    private readonly HttpClient _httpClient;
    private readonly ChatSession _chatSession;
    private readonly Dictionary<string, string> _valueStore;

    public AzureAgent()
    {
        _buffer = new StringBuilder();
        _httpClient = new HttpClient();
        Task.Run(() => DataRetriever.WarmUpMetadataService(_httpClient));

        _chatSession = new ChatSession(_httpClient);
        _valueStore = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        _instructions = string.Format(
            InstructionPrompt,
            OperatingSystem.IsMacOS() ? $"Mac OS X {Environment.OSVersion.Version}" : RuntimeInformation.OSDescription);

        Name = "azure";
        Company = "Microsoft";
        Description = "This AI assistant connects you to the Copilot in Azure and can generate Azure CLI and Azure PowerShell commands for managing Azure resources and answer questions about Azure.";

        SampleQueries = [
            "How do I create a resource group?",
            "Help me create a storage account"
        ];

        LegalLinks = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Terms"] = "https://aka.ms/AzureAgentTermsofUse",
            ["Privacy"] = "https://aka.ms/privacy",
            ["FAQ"] = "https://aka.ms/AzureAgentFAQ",
            ["Transparency"] = "https://aka.ms/AzureAgentTransparency",
        };
    }

    public void Dispose()
    {
        ResetArgumentPlaceholder();
        _chatSession.Dispose();
        _httpClient.Dispose();

        Log.CloseAndFlush();
        Telemetry.CloseAndFlush();
    }

    public void Initialize(AgentConfig config)
    {
        SettingFile = Path.Combine(config.ConfigurationRoot, SettingFileName);

        _turnsLeft = int.MaxValue;
        _setting = AgentSetting.LoadFromFile(SettingFile);

        if (_setting is null)
        {
            // Use default setting and create a setting file with the default settings.
            _setting = AgentSetting.Default;
            AgentSetting.NewSettingFile(SettingFile);
        }

        if (_setting.Logging)
        {
            string logFile = Path.Combine(config.ConfigurationRoot, LoggingFileName);
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Async(a => a.File(
                    path: logFile,
                    outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                    rollingInterval: RollingInterval.Day))
                .CreateLogger();
            Log.Information("Azure agent initialized.");
        }

        if (_setting.Telemetry)
        {
            Telemetry.Initialize();
        }
    }

    public IEnumerable<CommandBase> GetCommands() => [new ReplaceCommand(this)];
    public bool CanAcceptFeedback(UserAction action) => Telemetry.Enabled;

    public void OnUserAction(UserActionPayload actionPayload)
    {
        // Send telemetry about the user action.
        bool isUserFeedback = false;
        bool shareConversation = false;
        string details = null;
        string action = actionPayload.Action.ToString();

        switch (actionPayload)
        {
            case DislikePayload dislike:
                isUserFeedback = true;
                shareConversation = dislike.ShareConversation;
                details = string.Format("{0} | {1}", dislike.ShortFeedback, dislike.LongFeedback);
                break;

            case LikePayload like:
                isUserFeedback = true;
                shareConversation = like.ShareConversation;
                break;

            default:
                break;
        }

        if (isUserFeedback)
        {
            Telemetry.Trace(AzTrace.Feedback(action, shareConversation, _copilotResponse, details));
        }
        else
        {
            Telemetry.Trace(AzTrace.UserAction(action, _copilotResponse, details));
        }
    }

    public async Task RefreshChatAsync(IShell shell, bool force)
    {
        IHost host = shell.Host;
        CancellationToken cancellationToken = shell.CancellationToken;

        _copilotResponse = null;
        ResetArgumentPlaceholder();

        try
        {
            string welcome = await host.RunWithSpinnerAsync(
                status: "Initializing ...",
                spinnerKind: SpinnerKind.Processing,
                func: async context => await _chatSession.RefreshAsync(context, force, cancellationToken)
            ).ConfigureAwait(false);

            if (!string.IsNullOrEmpty(welcome))
            {
                _turnsLeft = int.MaxValue;
                host.WriteLine(welcome);
            }
        }
        catch (OperationCanceledException)
        {
            host.WriteErrorLine("Operation cancelled. Please run '/refresh' to start a new conversation.");
        }
        catch (TokenRequestException e)
        {
            if (e.UserUnauthorized)
            {
                host.WriteLine("Sorry, you are not authorized to access Azure Copilot services.");
                host.WriteLine($"Details: {e.Message}");
                return;
            }

            Exception inner = e.InnerException;
            if (inner is CredentialUnavailableException)
            {
                host.WriteErrorLine($"Failed to start a chat session: Access token not available.");
                host.WriteErrorLine($"The '{Name}' agent depends on the Azure CLI credential or Azure PowerShell credential to acquire access token. Please run 'az login' or 'Connect-AzAccount' from a command-line shell to setup account. Once you've successfully logged in, run '/refresh' to start a new conversation.");
                return;
            }

            host.WriteErrorLine(e.Message);
            host.WriteErrorLine("Please try '/refresh' to start a new conversation.");
        }
        catch (Exception e)
        {
            host.WriteErrorLine($"Failed to start a chat session: {e.Message}\n{e.StackTrace}")
                .WriteErrorLine()
                .WriteErrorLine("Please try '/refresh' to start a new conversation.");
        }
    }

    public async Task<bool> ChatAsync(string input, IShell shell)
    {
        IHost host = shell.Host;
        CancellationToken token = shell.CancellationToken;

        _copilotResponse = null;
        ResetArgumentPlaceholder();

        if (_turnsLeft is 0)
        {
            host.WriteLine("\nSorry, you've reached the maximum length of a conversation. Please run '/refresh' to start a new conversation.\n");
            return true;
        }

        if (!_chatSession.UserAuthorized)
        {
            host.WriteLine("\nSorry, you are not authorized to access Azure Copilot services.\n");
            return true;
        }

        try
        {
            string query = $"{input}\n\n---\n\n{_instructions}";
            _copilotResponse = await host.RunWithSpinnerAsync(
                status: "Thinking ...",
                spinnerKind: SpinnerKind.Processing,
                func: async context => await _chatSession.GetChatResponseAsync(query, context, token)
            ).ConfigureAwait(false);

            if (_copilotResponse is null)
            {
                // User cancelled the operation.
                return true;
            }

            if (_copilotResponse.ChunkReader is null)
            {
                if (_copilotResponse.IsError)
                {
                    string errorMessage = _copilotResponse.Text;
                    Telemetry.Trace(AzTrace.Exception(errorMessage));
                    host.WriteErrorLine()
                        .WriteErrorLine(errorMessage)
                        .WriteErrorLine();
                }
                else
                {
                    // Process CLI handler response specially to support parameter injection.
                    ResponseData data = null;
                    if (_copilotResponse.TopicName is CopilotActivity.CLIHandlerTopic or CopilotActivity.PSHandlerTopic)
                    {
                        data = ParseCodeResponse(shell);
                    }

                    if (data?.PlaceholderSet is not null)
                    {
                        ArgPlaceholder = new ArgumentPlaceholder(input, data, _httpClient);
                    }

                    string answer = data is null ? _copilotResponse.Text : GenerateAnswer(data, shell);
                    host.RenderFullResponse(answer);
                }
            }
            else
            {
                try
                {
                    using var streamingRender = host.NewStreamRender(token);
                    CopilotActivity prevActivity = null;

                    while (true)
                    {
                        CopilotActivity activity = _copilotResponse.ChunkReader.ReadChunk(token);
                        if (activity is null)
                        {
                            prevActivity.ExtractMetadata(out string[] suggestion, out ConversationState state);
                            _copilotResponse.SuggestedUserResponses = suggestion;
                            _copilotResponse.ConversationState = state;
                            break;
                        }

                        int start = prevActivity is null ? 0 : prevActivity.Text.Length;
                        streamingRender.Refresh(activity.Text[start..]);
                        prevActivity = activity;
                    }
                }
                catch (OperationCanceledException)
                {
                    // User cancelled the operation.
                    // TODO: we may need to notify azure copilot somehow about the cancellation.
                }
            }

            // The 'ConversationState' could be null when Azure Copilot returns an error response.
            var conversationState = _copilotResponse.ConversationState;
            if (conversationState is not null)
            {
                _turnsLeft = conversationState.TurnLimit - conversationState.TurnNumber;
                if (_turnsLeft <= 5)
                {
                    string message = _turnsLeft switch
                    {
                        1 => $"[yellow]{_turnsLeft} request left[/]",
                        0 => $"[red]{_turnsLeft} request left[/]",
                        _ => $"[yellow]{_turnsLeft} requests left[/]",
                    };

                    host.RenderDivider(message, DividerAlignment.Right);
                    if (_turnsLeft is 0)
                    {
                        host.WriteLine("\nYou've reached the maximum length of a conversation. To continue, please run '/refresh' to start a new conversation.\n");
                    }
                }
            }

            Log.Debug("[AzureAgent] TopicName: {0}", _copilotResponse.TopicName);
            Telemetry.Trace(AzTrace.Chat(_copilotResponse));
        }
        catch (Exception ex)
        {
            if (ex is TokenRequestException or ConnectionDroppedException)
            {
                host.WriteErrorLine(ex.Message);
                host.WriteErrorLine("Please run '/refresh' to start a new chat session and try again.");
                return false;
            }

            Log.Error(ex, "Exception thrown when processing the query '{0}'", input);
            if (_copilotResponse?.Text is not null)
            {
                Log.Error("Response text:\n{0}", _copilotResponse.Text);
            }

            throw;
        }

        return true;
    }

    private ResponseData ParseCodeResponse(IShell shell)
    {
        string text = _copilotResponse.Text;
        List<CodeBlock> codeBlocks = shell.ExtractCodeBlocks(text, out List<SourceInfo> sourceInfos);
        if (codeBlocks is null || codeBlocks.Count is 0)
        {
            return null;
        }

        Debug.Assert(codeBlocks.Count == sourceInfos.Count, "There should be 1-to-1 mapping for code block and its source info.");

        HashSet<string> phSet = null;
        List<PlaceholderItem> placeholders = null;
        List<CommandItem> commands = new(capacity: codeBlocks.Count);

        for (int i = 0; i < codeBlocks.Count; i++)
        {
            string script = codeBlocks[i].Code;
            commands.Add(new CommandItem { SourceInfo = sourceInfos[i], Script = script });

            // Go through all code blocks to find placeholders. Placeholder is in the `<xxx>` form.
            int start = -1;
            for (int k = 0; k < script.Length; k++)
            {
                char c = script[k];
                if (c is '<')
                {
                    start = k;
                }
                else if (c is '>' && start > -1)
                {
                    placeholders ??= [];
                    phSet ??= new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    string ph = script[start..(k+1)];
                    if (phSet.Add(ph))
                    {
                        placeholders.Add(new PlaceholderItem { Name = ph, Desc = ph, Type = "string" });
                    }

                    start = -1;
                }
            }
        }

        if (placeholders is null)
        {
            return null;
        }

        ResponseData data = new() {
            Text = text,
            CommandSet = commands,
            TopicName = _copilotResponse.TopicName,
            PlaceholderSet = placeholders,
            Locale = _copilotResponse.Locale,
        };

        string first = placeholders[0].Name;
        int begin = sourceInfos[^1].End + 1;

        // We instruct Az Copilot to summarize placeholders in the fixed format shown below.
        // So, we assume the response will adhere to this format and parse the text based on it.
        //  Placeholders:
        //  - `<first-placeholder>`: <concise-description>
        //  - `<second-placeholder>`: <concise-description>
        const string pattern = "- `{0}`:";
        int index = text.IndexOf(string.Format(pattern, first), begin);
        if (index > 0 && IsInPlaceholderSection(text, index, out begin))
        {
            // For each placeholder, try to extract its description.
            foreach (var phItem in placeholders)
            {
                string key = string.Format(pattern, phItem.Name);
                index = text.IndexOf(key, begin);
                if (index > 0)
                {
                    // Extract out the description of the particular placeholder.
                    int i = index + key.Length, k = i;
                    for (; k < text.Length && text[k] is not '\n'; k++);
                    var desc = text.AsSpan(i, k - i).Trim();
                    if (desc.Length > 0)
                    {
                        phItem.Desc = desc.ToString();
                    }
                }
            }

            data.Text = text[0..begin];
        }
        else
        {
            // The placeholder section is not in the format as we've instructed ...
            Log.Error("Placeholder section not in expected format:\n{0}", text);
            Telemetry.Trace(AzTrace.Exception("Placeholder section not in expected format."));
        }

        ReplaceKnownPlaceholders(data);
        return data;

        static bool IsInPlaceholderSection(string text, int index, out int sectionStart)
        {
            // This section should immediately follow "Placeholders:" on the next line.
            // The "- `<xxx>`" part mostly starts at the beginning of the new line, but
            // sometimes starts after a few space characters.
            int firstNonSpaceCharBackward = -1;
            for (int i = index - 1; i >= 0; i--)
            {
                if (text[i] is not ' ')
                {
                    firstNonSpaceCharBackward = i;
                    break;
                }
            }

            if (firstNonSpaceCharBackward > 0
                && text[firstNonSpaceCharBackward] is '\n'
                && text[firstNonSpaceCharBackward - 1] is ':')
            {
                // Get the start index of the placeholder section.
                int n = firstNonSpaceCharBackward - 1;
                for (; text[n] is not '\n'; n--);

                sectionStart = n + 1;
                return true;
            }

            sectionStart = -1;
            return false;
        }
    }

    internal void ResetArgumentPlaceholder()
    {
        ArgPlaceholder?.DataRetriever?.Dispose();
        ArgPlaceholder = null;
    }

    internal void SaveUserValue(string phName, string value)
    {
        ArgumentException.ThrowIfNullOrEmpty(phName);
        ArgumentException.ThrowIfNullOrEmpty(value);

        _valueStore[phName] = value;
    }

    internal void ReplaceKnownPlaceholders(ResponseData data)
    {
        List<PlaceholderItem> placeholders = data.PlaceholderSet;
        if (_valueStore.Count is 0 || placeholders is null)
        {
            return;
        }

        List<int> indices = null;
        Dictionary<string, string> pairs = null;

        for (int i = 0; i < placeholders.Count; i++)
        {
            PlaceholderItem item = placeholders[i];
            if (_valueStore.TryGetValue(item.Name, out string value))
            {
                indices ??= [];
                pairs ??= [];

                indices.Add(i);
                pairs.Add(item.Name, value);
            }
        }

        if (pairs is null)
        {
            return;
        }

        foreach (CommandItem command in data.CommandSet)
        {
            foreach (var entry in pairs)
            {
                string script = command.Script;
                command.Script = script.Replace(entry.Key, entry.Value, StringComparison.OrdinalIgnoreCase);
                if (!ReferenceEquals(script, command.Script))
                {
                    command.Updated = true;
                }
            }
        }

        if (pairs.Count == placeholders.Count)
        {
            data.PlaceholderSet = null;
        }
        else
        {
            for (int i = indices.Count - 1; i >= 0; i--)
            {
                placeholders.RemoveAt(indices[i]);
            }
        }
    }

    internal string GenerateAnswer(ResponseData data, IShell shell)
    {
        // Use green (0,195,0) on grey (48,48,48) for rendering commands in the markdown.
        // TODO: the color formatting should be exposed by the shell as utility method.
        const string CommandVTColor = "\x1b[38;2;0;195;0;48;2;48;48;48m";
        const string ResetVT = "\x1b[0m";

        _buffer.Clear();
        string text = data.Text;

        int index = 0;
        foreach (CommandItem item in data.CommandSet)
        {
            if (item.Updated)
            {
                _buffer.Append(text.AsSpan(index, item.SourceInfo.Start - index));
                _buffer.Append(item.Script);
                index = item.SourceInfo.End + 1;
            }
        }

        if (index is 0)
        {
            _buffer.Append(text);
        }
        else if (index < text.Length)
        {
            _buffer.Append(text.AsSpan(index, text.Length - index));
        }

        if (data.PlaceholderSet is null)
        {
            _buffer.Append(shell.ChannelEstablished
                ? $"\nRun {CommandVTColor} /code post {ResetVT} or press {CommandVTColor} Ctrl+d,Ctrl+d {ResetVT} to post the code to the connected shell.\n"
                : $"\nRun {CommandVTColor} /code copy {ResetVT} or press {CommandVTColor} Ctrl+d,Ctrl+c {ResetVT} to copy the code to clipboard.\n");
        }
        else
        {
            // Construct text about the placeholders if we successfully stripped the placeholder
            // section off from the original response.
            //
            // TODO: Note that the original response could be in a different locale, and in
            // that case, we should be using a localized resource string based on the locale.
            // For now, we just hard code with English strings.
            var first = data.PlaceholderSet[0];
            if (first.Name != first.Desc)
            {
                _buffer.Append("\nReplace the placeholders with your specific values:\n");
                foreach (var phItem in data.PlaceholderSet)
                {
                    _buffer.Append($"- `{phItem.Name}`: {phItem.Desc}\n");
                }

                // Use green (0,195,0) on grey (48,48,48) for rendering the command '/replace'.
                // TODO: the color formatting should be exposed by the shell as utility method.
                _buffer.Append($"\nRun {CommandVTColor} /replace {ResetVT} to get assistance in placeholder replacement.\n");
            }
        }

        return _buffer.ToString();
    }
}
