using System.Text;
using System.Text.Json;
using AIShell.Abstraction;

namespace AIShell.Interpreter.Agent;

public sealed class InterpreterAgent : ILLMAgent
{
    public string Name => "interpreter";
    public string Description { private set; get; }
    public string SettingFile { private set; get; }

    private const string SettingFileName = "interpreter.agent.json";
    private bool _isInteractive;
    private bool _reloadSettings;
    private bool _isDisposed;
    private string _configRoot;
    private string _historyRoot;
    private RenderingStyle _renderingStyle;
    private Settings _settings;
    private FileSystemWatcher _watcher;
    private ChatService _chatService;
    private CodeExecutionService _executionService;

    /// <inheritdoc/>
    public void Dispose()
    {
        if (_isDisposed)
        {
            return;
        }

        // This terminates any remaining processes used to run code.
        _executionService.Terminate();

        GC.SuppressFinalize(this);
        _watcher.Dispose();
        _isDisposed = true;
    }

    /// <inheritdoc/>
    public void Initialize(AgentConfig config)
    {
        _isInteractive = config.IsInteractive;
        _renderingStyle = config.RenderingStyle;

        _configRoot = config.ConfigurationRoot;
        _historyRoot = Path.Combine(_configRoot, "history");
        if (!Directory.Exists(_historyRoot))
        {
            Directory.CreateDirectory(_historyRoot);
        }

        SettingFile = Path.Combine(_configRoot, SettingFileName);
        _settings = ReadSettings();
        _executionService = new CodeExecutionService();
        _chatService = new ChatService(_isInteractive, _historyRoot, _settings, _executionService);

        if (_settings is null)
        {
            // Create the setting file with examples to serve as a template for user to update.
            NewExampleSettingFile();
        }

        UpdateDescription();
        _watcher = new FileSystemWatcher(_configRoot, SettingFileName)
        {
            NotifyFilter = NotifyFilters.LastWrite,
            EnableRaisingEvents = true,
        };
        _watcher.Changed += OnSettingFileChange;
    }

    /// <inheritdoc/>
    public Task RefreshChatAsync(IShell shell, bool force)
    {
        if (force)
        {
            // Reload the setting file if needed.
            ReloadSettings();
            // Reset the history so the subsequent chat can start fresh.
            _chatService.RefreshChat();
            // Shut down the execution service to start fresh.
            _executionService.Terminate();
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public bool CanAcceptFeedback(UserAction action) => false;

    /// <inheritdoc/>
    public void OnUserAction(UserActionPayload actionPayload) { }

    /// <inheritdoc/>
    public IEnumerable<CommandBase> GetCommands() => null;

    /// <inheritdoc/>
    public async Task<bool> ChatAsync(string input, IShell shell)
    {
        IHost host = shell.Host;
        CancellationToken token = shell.CancellationToken;

        // Reload the setting file if needed.
        ReloadSettings();

        bool checkPass = await SelfCheck(host, token);
        if (!checkPass)
        {
            host.MarkupWarningLine($"[[{Name}]]: Cannot serve the query due to the missing configuration. Please properly update the setting file.");
            return checkPass;
        }

        try
        {
            TaskCompletionChat _taskCompletionChat = new(_settings, _chatService, _executionService, host);
            await _taskCompletionChat.StartTask(input, _renderingStyle, token);
        }
        catch (OperationCanceledException)
        {
            // Ignore the exception.
        }

        return checkPass;
    }

    internal async Task<bool> SelfCheck(IHost host, CancellationToken token)
    {
        if (_settings is null)
        {
            return false;
        }

        bool checkPass = await _settings.SelfCheck(host, token);

        if (_settings.Dirty)
        {
            try
            {
                _watcher.EnableRaisingEvents = false;
                SaveSettings(_settings);
                _settings.MarkClean();
            }
            finally
            {
                _watcher.EnableRaisingEvents = true;
            }
        }

        return checkPass;
    }

    internal void UpdateDescription()
    {
        const string DefaultDescription = "An agent that specializes in completing code related tasks. Given a task, this agent will write a plan, generate code, execute code, and move on to the next step of the plan until the task is complete while correcting itself for any errors. This agent currently only supports PowerShell and Python languages.";

        if (_settings is null)
        {
            Description = $"""
                {DefaultDescription}

                The agent is not ready to serve queries, because AI service has not been configured. Please follow the steps below to setup the AI service for this agent:

                1. Run '/agent config' to open the setting file.
                2. Configure the settings. See details at
                     https://aka.ms/aish/interpreter
                3. Run '/refresh' to apply the new settings.
                """;

            return;
        }

        string deploymentOrModel = _settings.Type is EndpointType.OpenAI
            ? $"Model: [{_settings.ModelName}]"
            : $"Deployment: [{_settings.Deployment}]";

        Description = $"""
            {DefaultDescription}

            Service in use: [{_settings.Type}], {deploymentOrModel}
            """;
    }

    private void ReloadSettings()
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
            _chatService.RefreshSettings(_settings);
            UpdateDescription();
        }
    }

    private Settings ReadSettings()
    {
        FileInfo file = new(SettingFile);

        if (file.Exists)
        {
            try
            {
                using var stream = file.OpenRead();
                ConfigData data = JsonSerializer.Deserialize(stream, SourceGenerationContext.Default.ConfigData);
                return data.IsEmpty() ? null : new Settings(data);
            }
            catch (Exception e)
            {
                throw new InvalidDataException($"Parsing settings from '{SettingFile}' failed with the following error: {e.Message}", e);
            }
        }

        return null;
    }

    private void SaveSettings(Settings config)
    {
        using var stream = new FileStream(SettingFile, FileMode.Create, FileAccess.Write, FileShare.None);
        JsonSerializer.Serialize(stream, config.ToConfigData(), SourceGenerationContext.Default.ConfigData);
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
        string sample = $$"""
        {
          // To use the Azure OpenAI service:
          // - Set `Endpoint` to the endpoint of your Azure OpenAI service,
          //     or the endpoint to the Azure API Management service if you are using it as a gateway.
          // - Set `Deployment` to the deployment name of your Azure OpenAI service.
          // - Set `ModelName` to the name of the model used for your deployment, e.g. "gpt-4-0613".
          // - Set `Key` to the access key of your Azure OpenAI service,
          //     or the key of the Azure API Management service if you are using it as a gateway.
          "Endpoint": "",
          "Deployment": "",
          "ModelName": "",
          "Key": "",
          "AuthType": "ApiKey",
          "AutoExecution": false, // 'true' to allow the agent run code automatically; 'false' to always prompt before running code.
          "DisplayErrors": true   // 'true' to display the errors when running code; 'false' to hide the errors to be less verbose.

          // To use Azure OpenAI service with Entra ID authentication:
          // - Set `Endpoint` to the endpoint of your Azure OpenAI service.
          // - Set `Deployment` to the deployment name of your Azure OpenAI service.
          // - Set `ModelName` to the name of the model used for your deployment.
          // - Set `AuthType` to "EntraID" to use Azure AD credentials.
          /*
          "Endpoint": "<insert your Azure OpenAI endpoint>",
          "Deployment": "<insert your deployment name>",
          "ModelName": "<insert the model name>",
          "AuthType": "EntraID",
          "AutoExecution": false,
          "DisplayErrors": true
          */

          // To use the public OpenAI service:
          // - Ignore the `Endpoint` and `Deployment` keys.
          // - Set `ModelName` to the name of the model to be used. e.g. "gpt-4o".
          // - Set `Key` to be the OpenAI access token.
          /*
          "ModelName": "<insert the model name>",
          "Key": "<insert your key>",
          "AuthType": "ApiKey",
          "AutoExecution": false,
          "DisplayErrors": true
          */
        }
        """;
        File.WriteAllText(SettingFile, sample);
    }
}
