﻿using System.Diagnostics;
using System.Text;

using AIShell.Abstraction;
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

    private const string SettingFileName = "az.agent.json";
    private const string LoggingFileName = "log..txt";
    private const string InstructionPrompt = """
        NOTE: follow the below instructions when generating responses that include Azure CLI commands with placeholders:
        1. User's OS is `{0}`. Make sure the generated commands are suitable for the specified OS.
        2. DO NOT include the command for creating a new resource group unless the query explicitly asks for it. Otherwise, assume a resource group already exists.
        3. DO NOT include an additional example with made-up values unless it provides additional context or value beyond the initial command.
        4. Always represent a placeholder in the form of `<placeholder-name>`.
        5. Always use the consistent placeholder names across all your responses. For example, `<resourceGroupName>` should be used for all the places where a resource group name value is needed.
        6. When the commands contain placeholders, the placeholders should be summarized in markdown bullet points at the end of the response in the same order as they appear in the commands, following this format:
           ```
           Placeholders:
           - `<first-placeholder>`: <concise-description>
           - `<second-placeholder>`: <concise-description>
           ```
        7. DO NOT include the placeholder summary when the commands contains no placeholder.
        """;

    private int _turnsLeft;

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
        _instructions = string.Format(InstructionPrompt, Environment.OSVersion.VersionString);

        Name = "Azure";
        Company = "Microsoft";
        Description = "This AI assistant can generate Azure CLI and Azure PowerShell commands for managing Azure resources, answer questions, and provides information tailored to your specific Azure environment.";

        SampleQueries = [
            "Create a VM with a public IP address",
            "How to create a web app?",
            "Backup an Azure SQL database to a storage container"
        ];

        LegalLinks = new(StringComparer.OrdinalIgnoreCase)
        {
            ["Terms"] = "https://aka.ms/TermsofUseCopilot",
            ["Privacy"] = "https://aka.ms/privacy",
            ["FAQ"] = "https://aka.ms/CopilotforAzureClientToolsFAQ",
            ["Transparency"] = "https://aka.ms/CopilotAzCLIPSTransparency",
        };
    }

    public void Dispose()
    {
        ArgPlaceholder?.DataRetriever?.Dispose();
        _chatSession.Dispose();
        _httpClient.Dispose();

        Log.CloseAndFlush();
    }

    public void Initialize(AgentConfig config)
    {
        _turnsLeft = int.MaxValue;
        SettingFile = Path.Combine(config.ConfigurationRoot, SettingFileName);

        string logFile = Path.Combine(config.ConfigurationRoot, LoggingFileName);
        Log.Logger = new LoggerConfiguration()
            .WriteTo.Async(a => a.File(
                path: logFile,
                outputTemplate: "{Timestamp:HH:mm:ss} [{Level:u3}] {Message:lj}{NewLine}{Exception}",
                rollingInterval: RollingInterval.Day))
            .CreateLogger();
        Log.Information("Azure agent initialized.");
    }

    public IEnumerable<CommandBase> GetCommands() => [new ReplaceCommand(this)];
    public bool CanAcceptFeedback(UserAction action) => false;
    public void OnUserAction(UserActionPayload actionPayload) {}

    public async Task RefreshChatAsync(IShell shell)
    {
        // Refresh the chat session.
        await _chatSession.RefreshAsync(shell.Host, shell.CancellationToken);
    }

    public async Task<bool> ChatAsync(string input, IShell shell)
    {
        IHost host = shell.Host;
        CancellationToken token = shell.CancellationToken;

        if (_turnsLeft is 0)
        {
            host.WriteLine("\nSorry, you've reached the maximum length of a conversation. Please run '/refresh' to start a new conversation.\n");
            return true;
        }

        try
        {
            string query = $"{input}\n\n---\n\n{_instructions}";
            CopilotResponse copilotResponse = await host.RunWithSpinnerAsync(
                status: "Thinking ...",
                spinnerKind: SpinnerKind.Processing,
                func: async context => await _chatSession.GetChatResponseAsync(query, context, token)
            ).ConfigureAwait(false);

            if (copilotResponse is null)
            {
                // User cancelled the operation.
                return true;
            }

            if (copilotResponse.ChunkReader is null)
            {
                ArgPlaceholder?.DataRetriever?.Dispose();
                ArgPlaceholder = null;

                // Process CLI handler response specially to support parameter injection.
                ResponseData data = null;
                if (copilotResponse.TopicName == CopilotActivity.CLIHandlerTopic)
                {
                    data = ParseCLIHandlerResponse(copilotResponse, shell);
                }

                if (data?.PlaceholderSet is not null)
                {
                    ArgPlaceholder = new ArgumentPlaceholder(input, data, _httpClient);
                }

                string answer = data is null ? copilotResponse.Text : GenerateAnswer(data);
                host.RenderFullResponse(answer);
            }
            else
            {
                try
                {
                    using var streamingRender = host.NewStreamRender(token);
                    CopilotActivity prevActivity = null;

                    while (true)
                    {
                        CopilotActivity activity = copilotResponse.ChunkReader.ReadChunk(token);
                        if (activity is null)
                        {
                            prevActivity.ExtractMetadata(out string[] suggestion, out ConversationState state);
                            copilotResponse.SuggestedUserResponses = suggestion;
                            copilotResponse.ConversationState = state;
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

            var conversationState = copilotResponse.ConversationState;
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
        catch (Exception ex) when (ex is TokenRequestException or ConnectionDroppedException)
        {
            host.WriteErrorLine(ex.Message);
            host.WriteErrorLine("Please run '/refresh' to start a new chat session and try again.");
            return false;
        }

        return true;
    }

    private ResponseData ParseCLIHandlerResponse(CopilotResponse copilotResponse, IShell shell)
    {
        string text = copilotResponse.Text;
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
            PlaceholderSet = placeholders,
            Locale = copilotResponse.Locale,
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
        if (index > 0 && text[index - 1] is '\n' && text[index - 2] is ':')
        {
            // Get the start index of the placeholder section.
            int n = index - 2;
            for (; text[n] is not '\n'; n--);
            begin = n + 1;

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
            // TODO: send telemetry about this case.
            Log.Error("Placeholder section not in expected format:\n{0}", text);
        }

        ReplaceKnownPlaceholders(data);
        return data;
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

    internal string GenerateAnswer(ResponseData data)
    {
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

        if (data.PlaceholderSet is not null)
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

                _buffer.Append("\nRun `/replace` to get assistance in placeholder replacement.\n");
            }
        }

        return _buffer.ToString();
    }
}
