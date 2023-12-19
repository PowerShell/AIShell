using System.Text;
using System.Reflection;
using Azure.AI.OpenAI;
using Microsoft.PowerShell;
using ShellCopilot.Abstraction;
using ShellCopilot.Kernel.Commands;
using Spectre.Console;

namespace ShellCopilot.Kernel;

internal class Shell
{
    private readonly bool _interactive;
    private readonly string _agentHome;
    private readonly string _agentConfigHome;
    private readonly List<ILLMAgent> _agents;
    private readonly Stack<ILLMAgent> _stack;
    private readonly Host _host;
    private readonly MarkdownRender _markdownRender;
    private readonly CommandRunner _commandRunner;

    private bool _hasExited;
    private CancellationTokenSource _cancellationSource;

    private bool ChatDisabled { get; set; }
    private ILLMAgent ActiveAgent => _stack.TryPeek(out var agent) ? agent : null;

    internal Host Host => _host;
    internal MarkdownRender MarkdownRender => _markdownRender;
    internal CommandRunner CommandRunner => _commandRunner;
    internal CancellationToken CancellationToken => _cancellationSource.Token;

    internal Shell(bool interactive, bool useAlternateBuffer = false, string historyFileNamePrefix = null)
    {
        _agentHome = Path.Join(Utils.AppConfigHome, "agents");
        _agentConfigHome = Path.Join(Utils.AppConfigHome, "agent-config");

        _agents = new List<ILLMAgent>();
        _stack = new Stack<ILLMAgent>();
        _host = new Host();
        _markdownRender = new MarkdownRender();
        _cancellationSource = new CancellationTokenSource();

        ChatDisabled = false;
        LoadAvailableAgents();
        Console.CancelKeyPress += OnCancelKeyPress;

        if (interactive)
        {
            _commandRunner = new CommandRunner(this);

            // Write out the active model information.
            AnsiConsole.WriteLine("\nShell Copilot (v0.1)");
            if (ActiveAgent is not null)
            {
                AnsiConsole.MarkupLine($"Using the agent [green]{ActiveAgent.Name}[/]:");
            }

            // Write out help.
            AnsiConsole.MarkupLine($"Type {ConsoleRender.FormatInlineCode("/help")} for instructions.");
            AnsiConsole.WriteLine();

            // Set readline configuration.
            SetReadLineExperience();
        }
    }

    internal void LoadOneAgent(string pluginFile)
    {
        Assembly plugin = Assembly.LoadFrom(pluginFile);
        foreach (Type type in plugin.ExportedTypes)
        {
            if (!typeof(ILLMAgent).IsAssignableFrom(type))
            {
                continue;
            }

            var agent = (ILLMAgent)Activator.CreateInstance(type);
            var agentHome = Path.Join(_agentConfigHome, agent.Name);
            var config = new AgentConfig
            {
                ConfigurationRoot = Directory.CreateDirectory(agentHome).FullName,
                RenderingStyle = Console.IsOutputRedirected
                    ? RenderingStyle.FullResponsePreferred
                    : RenderingStyle.StreamingResponsePreferred
            };

            agent.Initialize(config);
            _agents.Add(agent);
        }
    }

    private void LoadAvailableAgents()
    {
        // Create the folders if they don't exist.
        Directory.CreateDirectory(_agentHome);
        Directory.CreateDirectory(_agentConfigHome);

        // Load all available agents.
        foreach (string dir in Directory.EnumerateDirectories(_agentHome))
        {
            string name = Path.GetFileName(dir);
            string file = Path.Join(dir, $"{name}.dll");

            try
            {
                if (File.Exists(file))
                {
                    LoadOneAgent(file);
                }
            }
            catch (Exception ex)
            {
                AnsiConsole.MarkupLine(ConsoleRender.FormatError($"Failed to load the agent '{name}': {ex.Message}"));
            }
        }

        if (_agents.Count is 0)
        {
            AnsiConsole.MarkupLine(ConsoleRender.FormatError($"No agent available."));
        }
        else
        {
            var chosenAgent = _host.PromptForSelection(
                title: "Select the agent [green]to use[/]:",
                choices: _agents,
                converter: static a => a.Name);

            _stack.Push(chosenAgent);
        }
    }

    /// <summary>
    /// For reference:
    /// https://github.com/dotnet/command-line-api/blob/67df30a1ac4152e7f6278847b88b8f1ea1492ba7/src/System.CommandLine/Invocation/ProcessTerminationHandler.cs#L73
    /// TODO: We may want to implement `OnPosixSignal` too for more reliable cancellation on non-Windows.
    /// </summary>
    private void OnCancelKeyPress(object sender, ConsoleCancelEventArgs args)
    {
        // Set the Cancel property to true to prevent the process from terminating.
        args.Cancel = true;
        switch (args.SpecialKey)
        {
            // Treat both Ctrl-C and Ctrl-Break as the same.
            case ConsoleSpecialKey.ControlC:
            case ConsoleSpecialKey.ControlBreak:
                // Request cancellation and refresh the cancellation source.
                _cancellationSource.Cancel();
                _cancellationSource = new CancellationTokenSource();
                return;
        }
    }

    private void SetReadLineExperience()
    {
        PSConsoleReadLineOptions options = PSConsoleReadLine.GetOptions();
        options.RenderHelper = new ReadLineHelper(_commandRunner);

        PSConsoleReadLine.SetKeyHandler(
            new[] { "Ctrl+d,Ctrl+c" },
            (key, arg) =>
            {
                PSConsoleReadLine.RevertLine();
                PSConsoleReadLine.Insert("/code copy");
                PSConsoleReadLine.AcceptLine();
            },
            "CopyCode",
            "Copy the code snippet from the last response to clipboard.");
    }

    private static string ReadLinePrompt(int count, bool chatDisabled)
    {
        var indicator = chatDisabled ? ConsoleRender.FormatWarning(" ! ", usePrefix: false) : null;
        return $"[bold green]aish[/]:{count}{indicator}> ";
    }

    private static string GetWarningBasedOnFinishReason(CompletionsFinishReason reason)
    {
        if (reason.Equals(CompletionsFinishReason.TokenLimitReached))
        {
            return "The response may not be complete as the max token limit was exhausted.";
        }

        if (reason.Equals(CompletionsFinishReason.ContentFiltered))
        {
            return "The response is not complete as it was identified as potentially sensitive per content moderation policies.";
        }

        return null;
    }

    private static void WriteChatDisabledWarning()
    {
        string useCommand = ConsoleRender.FormatInlineCode($"/use");
        string helpCommand = ConsoleRender.FormatInlineCode("/help");

        AnsiConsole.WriteLine();
        AnsiConsole.MarkupLine(ConsoleRender.FormatWarning("Chat disabled due to the missing access key."));
        AnsiConsole.MarkupLine(ConsoleRender.FormatWarning($"Run {useCommand} to switch to a different model. Type {helpCommand} for more instructions."));
        AnsiConsole.WriteLine();
    }

    internal void RenderFullResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            AnsiConsole.WriteLine();
            AnsiConsole.MarkupLine(ConsoleRender.FormatNote("Received response is empty or contains whitespace only."));
        }
        else if (Console.IsOutputRedirected)
        {
            Console.WriteLine(response);
        }
        else
        {
            // Render the markdown only if standard output is not redirected.
            string text = _markdownRender.RenderText(response);
            if (!Utils.LeadingWhiteSpaceHasNewLine(text))
            {
                Console.WriteLine();
            }

            Console.WriteLine(text);
        }
    }

    private void PrintChatResponse(ChatResponse response)
    {
        RenderFullResponse(response.Content);

        string warning = GetWarningBasedOnFinishReason(response.FinishReason);
        if (warning is not null)
        {
            AnsiConsole.MarkupLine(ConsoleRender.FormatWarning(warning));
            AnsiConsole.WriteLine();
        }
    }

    private async Task<string> PrintStreamingChatResponse(StreamingChatCompletions streamingChatCompletions)
    {
        var content = new StringBuilder();
        var streamingRender = new StreamingRender();
        using var response = streamingChatCompletions;

        try
        {
            // Hide the cursor position when rendering the streaming response.
            Console.CursorVisible = false;
            await foreach (StreamingChatChoice choice in response.GetChoicesStreaming())
            {
                await foreach (ChatMessage message in choice.GetMessageStreaming())
                {
                    if (string.IsNullOrEmpty(message.Content))
                    {
                        continue;
                    }

                    content.Append(message.Content);
                    string text = _markdownRender.RenderText(content.ToString());
                    streamingRender.Refresh(text);
                }
            }
        }
        finally
        {
            Console.CursorVisible = true;
        }

        Console.WriteLine();
        return content.ToString();
    }

    internal void ExitShell()
    {
        _hasExited = true;
    }

    internal bool EnsureKeyPresentForActiveModel()
    {
        AiModel model = _config.GetModelInUse();
        if (model.Key is null && model.RequestForKey(mandatory: true, CancellationToken, showBackendInfo: false))
        {
            Configuration.WriteToConfigFile(_config);
        }

        return model.Key is not null;
    }

    internal async Task RunOnceAsync(string prompt)
    {
        AiModel model = _config.GetModelInUse();
        if (model.Key is null)
        {
            string setCommand = ConsoleRender.FormatInlineCode($"{Utils.AppName} set");
            string helpCommand = ConsoleRender.FormatInlineCode($"{Utils.AppName} set -h");

            using var _ = ConsoleRender.UseErrorConsole();
            AnsiConsole.MarkupLine(ConsoleRender.FormatError($"Access key is missing for the active model '{model.Name}'."));
            AnsiConsole.MarkupLine(ConsoleRender.FormatError($"You can set the key by {setCommand}. Run {helpCommand} for details."));

            return;
        }

        try
        {
            Task<ChatResponse> func() => _service.GetChatResponseAsync(prompt, insertToHistory: false, CancellationToken);
            ChatResponse response = await _host.RunWithSpinnerAsync(func).ConfigureAwait(false);

            if (response is not null)
            {
                PrintChatResponse(response);
            }
        }
        catch (OperationCanceledException)
        {
            AnsiConsole.MarkupLine(ConsoleRender.FormatError("Operation was aborted."));
        }
        catch (ShellCopilotException exception)
        {
            AnsiConsole.MarkupLine(ConsoleRender.FormatError(exception.Message));
        }
    }

    internal async Task RunREPLAsync()
    {
        if (!EnsureKeyPresentForActiveModel())
        {
            ChatDisabled = true;
            WriteChatDisabledWarning();
        }

        int count = 1;
        while (!_hasExited)
        {
            string rlPrompt = ReadLinePrompt(count, ChatDisabled);
            AnsiConsole.Markup(rlPrompt);

            try
            {
                string input = PSConsoleReadLine.ReadLine();
                if (string.IsNullOrEmpty(input))
                {
                    continue;
                }

                count++;
                if (input.StartsWith('/'))
                {
                    string commandLine = input[1..].Trim();
                    if (commandLine == string.Empty)
                    {
                        AnsiConsole.MarkupLine(ConsoleRender.FormatError("Command is missing."));
                        continue;
                    }

                    try
                    {
                        _commandRunner.InvokeCommand(commandLine);
                    }
                    catch (Exception e)
                    {
                        AnsiConsole.MarkupLine(ConsoleRender.FormatError(e.Message));
                    }

                    continue;
                }

                // Chat to the AI endpoint.
                if (ChatDisabled)
                {
                    WriteChatDisabledWarning();
                    continue;
                }

                Task<StreamingChatCompletions> func() => _service.GetStreamingChatResponseAsync(input, insertToHistory: true, CancellationToken);
                StreamingChatCompletions response = await _host.RunWithSpinnerAsync(func).ConfigureAwait(false);

                if (response is not null)
                {
                    await PrintStreamingChatResponse(response);
                }
            }
            catch (ShellCopilotException e)
            {
                AnsiConsole.MarkupLine(ConsoleRender.FormatError(e.Message));
                if (e.HandlerAction is ExceptionHandlerAction.Stop)
                {
                    break;
                }
            }
        }
    }
}
