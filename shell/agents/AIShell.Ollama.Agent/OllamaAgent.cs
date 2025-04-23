using System.Text;
using System.Text.Json;
using AIShell.Abstraction;
using OllamaSharp;
using OllamaSharp.Models;

namespace AIShell.Ollama.Agent;

public sealed class OllamaAgent : ILLMAgent
{
    private bool _reloadSettings;
    private bool _isDisposed;
    private string _configRoot;
    private Settings _settings;
    private OllamaApiClient _client;
    private GenerateRequest _request;
    private FileSystemWatcher _watcher;

    /// <summary>
    /// The name of setting file
    /// </summary>
    private const string SettingFileName = "ollama.config.json";

    /// <summary>
    /// Gets the settings.
    /// </summary>
    internal Settings Settings => _settings;

    /// <summary>
    /// The name of the agent
    /// </summary>
    public string Name => "ollama";

    /// <summary>
    /// The description of the agent to be shown at start up
    /// </summary>
    public string Description => "This is an AI assistant to interact with a language model running locally or remotely by utilizing the Ollama API. Be sure to follow all prerequisites in https://github.com/PowerShell/AIShell/tree/main/shell/agents/AIShell.Ollama.Agent";

    /// <summary>
    /// This is the company added to /like and /dislike verbiage for who the telemetry helps.
    /// </summary>
    public string Company => "Microsoft";

    /// <summary>
    /// These are samples that are shown at start up for good questions to ask the agent
    /// </summary>
    public List<string> SampleQueries => [
        "How do I list files in a given directory?"
    ];

    /// <summary>
    /// These are any optional legal/additional information links you want to provide at start up
    /// </summary>
    public Dictionary<string, string> LegalLinks { private set; get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Ollama Docs"] = "https://github.com/ollama/ollama",
        ["Prerequisites"] = "https://github.com/PowerShell/AIShell/tree/main/shell/agents/AIShell.Ollama.Agent"
    };

    /// <summary>
    /// Dispose method to clean up the unmanaged resource of the chatService
    /// </summary>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        GC.SuppressFinalize(this);
        _watcher.Dispose();
        _isDisposed = true;
    }

    /// <summary>
    /// Initializing function for the class when the shell registers an agent
    /// </summary>
    /// <param name="config">Agent configuration for any configuration file and other settings</param>
    public void Initialize(AgentConfig config)
    {
        _configRoot = config.ConfigurationRoot;

        SettingFile = Path.Combine(_configRoot, SettingFileName);
        _settings = ReadSettings();

        if (_settings is null)
        {
            // Create the setting file with examples to serve as a template for user to update.
            NewExampleSettingFile();
            _settings = ReadSettings();
        }

        // Create Ollama request
        _request = new GenerateRequest();

        // Create Ollama client
        _client = new OllamaApiClient(_settings.Endpoint);

        // Watch for changes to the settings file
        _watcher = new FileSystemWatcher(_configRoot, SettingFileName)
        {
            NotifyFilter = NotifyFilters.LastWrite,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += OnSettingFileChange;
    }

    /// <summary>
    /// Get commands that an agent can register to the shell when being loaded.
    /// </summary>
    public IEnumerable<CommandBase> GetCommands() => [new PresetCommand(this), new ModelCommand(this), new SystemPromptCommand(this)];

    /// <summary>
    /// Gets the path to the setting file of the agent.
    /// </summary>
    public string SettingFile { private set; get; }

    /// <summary>
    /// Gets a value indicating whether the agent accepts a specific user action feedback.
    /// </summary>
    /// <param name="action">The user action.</param>
    public bool CanAcceptFeedback(UserAction action) => false;

    /// <summary>
    /// A user action was taken against the last response from this agent.
    /// </summary>
    /// <param name="action">Type of the action.</param>
    /// <param name="actionPayload"></param>
    public void OnUserAction(UserActionPayload actionPayload) {}

    /// <summary>
    /// Refresh the current chat by starting a new chat session.
    /// This method allows an agent to reset chat states, interact with user for authentication, print welcome message, and more.
    /// </summary>
    public Task RefreshChatAsync(IShell shell, bool force)
    {
        if (force)
        {
            // Reload the setting file if needed.
            ReloadSettings();

            // Reset context
            _request.Context = null;
        }

        return Task.CompletedTask;
    }

    public void ResetContext()
    {
        _request.Context = null;
    }

    /// <summary>
    /// Main chat function that takes the users input and passes it to the LLM and renders it.
    /// </summary>
    /// <param name="input">The user input from the chat experience.</param>
    /// <param name="shell">The shell that provides host functionality.</param>
    /// <returns>Task Boolean that indicates whether the query was served by the agent.</returns>
    public async Task<bool> ChatAsync(string input, IShell shell)
    {
        // Get the shell host
        IHost host = shell.Host;

        // Get the cancellation token
        CancellationToken token = shell.CancellationToken;

        // Reload the setting file if needed.
        ReloadSettings();

        bool success = await _settings.PerformSelfcheck(host);
        if (!success)
        {
            return false;
        }

        ModelConfig config = _settings.RunningConfig;

        // Prepare request
        _request.Prompt = input;
        _request.Model = config.ModelName;
        _request.Stream = _settings.Stream;

        if (!string.IsNullOrWhiteSpace(config.SystemPrompt))
        {
            _request.System = config.SystemPrompt;
        }

        try
        {
            if (_request.Stream)
            {
                // Wait for the stream with the spinner running
                var ollamaStreamEnumerator = await host.RunWithSpinnerAsync(
                    status: "Thinking ...",
                    func: async () =>
                    {
                        // Start generating the stream asynchronously and return an enumerator
                        var enumerator = _client.GenerateAsync(_request, token).GetAsyncEnumerator(token);
                        if (await enumerator.MoveNextAsync().ConfigureAwait(false))
                        {
                            return enumerator;
                        }
                        return null;
                    }
                ).ConfigureAwait(false);

                if (ollamaStreamEnumerator is not null)
                {
                    using IStreamRender streamingRender = host.NewStreamRender(token);

                    do
                    {
                        var currentStream = ollamaStreamEnumerator.Current;

                        // Update the render with stream response
                        streamingRender.Refresh(currentStream.Response);

                        if (currentStream.Done)
                        {
                            // If the stream is complete, update the request context with the last stream context
                            var ollamaLastStream = (GenerateDoneResponseStream)currentStream;
                            _request.Context = ollamaLastStream.Context;
                        }
                    }
                    while (await ollamaStreamEnumerator.MoveNextAsync().ConfigureAwait(false));
                }
            }
            else
            {
                // Build single response with spinner
                var ollamaResponse = await host.RunWithSpinnerAsync(
                    status: "Thinking ...",
                    func: async () => { return await _client.GenerateAsync(_request, token).StreamToEndAsync(); }
                ).ConfigureAwait(false);

                // Update request context
                _request.Context = ollamaResponse.Context;

                // Render the full response
                host.RenderFullResponse(ollamaResponse.Response);
            }
        }
        catch (OperationCanceledException)
        {
            // Ignore the cancellation exception.
        }
        catch (HttpRequestException e)
        {
            host.WriteErrorLine($"{e.Message}");
            host.WriteErrorLine($"Ollama active model: \"{config.ModelName}\"");
            host.WriteErrorLine($"Ollama endpoint:     \"{_settings.Endpoint}\"");
            host.WriteErrorLine($"Ollama settings:     \"{SettingFile}\"");
        }

        return true;
    }

    internal void ReloadSettings()
    {
        if (_reloadSettings)
        {
            _reloadSettings = false;
            var settings = ReadSettings();
            if (settings is null)
            {
                return;
            }

            _settings = settings;

            // Check if the endpoint has changed
            bool isEndpointChanged = !string.Equals(_settings.Endpoint, _client.Uri.OriginalString, StringComparison.OrdinalIgnoreCase);

            if (isEndpointChanged)
            {
                // Create a new client with updated endpoint
                _client = new OllamaApiClient(_settings.Endpoint);
            }
        }
    }

    private Settings ReadSettings()
    {
        Settings settings = null;
        FileInfo file = new(SettingFile);

        if (file.Exists)
        {
            try
            {
                using var stream = file.OpenRead();
                var data = JsonSerializer.Deserialize(stream, SourceGenerationContext.Default.ConfigData);
                settings = new Settings(data);
            }
            catch (Exception e)
            {
                throw new InvalidDataException($"Parsing settings from '{SettingFile}' failed with the following error: {e.Message}", e);
            }
        }

        return settings;
    }

    private void OnSettingFileChange(object sender, FileSystemEventArgs e)
    {
        if (e.ChangeType is WatcherChangeTypes.Changed)
        {
            _reloadSettings = true;
        }
    }

    private void NewExampleSettingFile()
    {
        string SampleContent = """
        {
            // To use Ollama API service:
            // 1. Install Ollama: `winget install Ollama.Ollama`
            // 2. Start Ollama API server: `ollama serve`
            // 3. Install Ollama model: `ollama pull phi3`
            
            // Declare predefined model configurations
            "Presets": [
                {
                    "Name": "PowerShell Expert",
                    "Description": "A ollama agent with expertise in PowerShell scripting and command line utilities.",
                    "ModelName": "phi3",
                    "SystemPrompt": "1. You are a helpful and friendly assistant with expertise in PowerShell scripting and command line.\n2. Assume user is using the operating system `Windows 11` unless otherwise specified.\n3. Use the `code block` syntax in markdown to encapsulate any part in responses that is code, YAML, JSON or XML, but not table.\n4. When encapsulating command line code, use '```powershell' if it's PowerShell command; use '```sh' if it's non-PowerShell CLI command.\n5. When generating CLI commands, never ever break a command into multiple lines. Instead, always list all parameters and arguments of the command on the same line.\n6. Please keep the response concise but to the point. Do not overexplain."
                }
            ],
            // Declare Ollama endpoint
            "Endpoint": "http://localhost:11434",
            // Enable Ollama streaming
            "Stream": false,
            // Specify the default preset to use
            "DefaultPreset": "PowerShell Expert"
        }
        """;
        File.WriteAllText(SettingFile, SampleContent, Encoding.UTF8);
    }
}
