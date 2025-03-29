using System.ClientModel;
using System.Text;
using System.Text.Json;
using AIShell.Abstraction;
using OpenAI.Chat;

namespace AIShell.OpenAI.Agent;

public sealed class OpenAIAgent : ILLMAgent
{
    public string Name => "openai-gpt";
    public string SettingFile { private set; get; }
    public string Description
    {
        get
        {
            // Changes in setting could affect the agent description.
            ReloadSettings();
            return _description;
        }
    }

    private const string SettingFileName = "openai.agent.json";
    private bool _reloadSettings;
    private bool _isDisposed;
    private string _configRoot;
    private string _historyRoot;
    private string _description;
    private Settings _settings;
    private FileSystemWatcher _watcher;
    private ChatService _chatService;

    /// <summary>
    /// Gets the settings.
    /// </summary>
    internal Settings Settings => _settings;

    /// <inheritdoc/>
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

    /// <inheritdoc/>
    public void Initialize(AgentConfig config)
    {
        _configRoot = config.ConfigurationRoot;
        _historyRoot = Path.Combine(_configRoot, "history");
        if (!Directory.Exists(_historyRoot))
        {
            Directory.CreateDirectory(_historyRoot);
        }

        SettingFile = Path.Combine(_configRoot, SettingFileName);
        _settings = ReadSettings();
        _chatService = new ChatService(_historyRoot, _settings);

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
    public IEnumerable<CommandBase> GetCommands() => [new GPTCommand(this)];

    /// <inheritdoc/>
    public bool CanAcceptFeedback(UserAction action) => false;

    /// <inheritdoc/>
    public void OnUserAction(UserActionPayload actionPayload) { }

    /// <inheritdoc/>
    public Task RefreshChatAsync(IShell shell, bool force)
    {
        if (force)
        {
            // Reload the setting file if needed.
            ReloadSettings();
            // Reset the history so the subsequent chat can start fresh.
            _chatService.ChatHistory.Clear();
        }

        return Task.CompletedTask;
    }

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

        IAsyncEnumerator<StreamingChatCompletionUpdate> response = await host
            .RunWithSpinnerAsync(
                () => _chatService.GetStreamingChatResponseAsync(input, token)
            ).ConfigureAwait(false);

        if (response is not null)
        {
            StreamingChatCompletionUpdate update = null;
            using var streamingRender = host.NewStreamRender(token);

            try
            {
                do
                {
                    update = response.Current;
                    if (update.ContentUpdate.Count > 0)
                    {
                        streamingRender.Refresh(update.ContentUpdate[0].Text);
                    }
                }
                while (await response.MoveNextAsync().ConfigureAwait(continueOnCapturedContext: false));
            }
            catch (OperationCanceledException)
            {
                update = null;
            }

            if (update is null)
            {
                _chatService.CalibrateChatHistory(usage: null, response: null);
            }
            else
            {
                string responseContent = streamingRender.AccumulatedContent;
                _chatService.CalibrateChatHistory(update.Usage, new AssistantChatMessage(responseContent));
            }
        }

        return checkPass;
    }

    internal void UpdateDescription()
    {
        const string DefaultDescription = """
            This agent is designed to provide a flexible platform for interacting with OpenAI services (Azure OpenAI or the public OpenAI) through one or more customly defined GPT instances.

            {0}:

            1. Run '/agent config' to open the setting file.
            2. {1}. See details at
                 https://aka.ms/aish/openai
            3. Run '/refresh' to apply the new settings.

            If you would like to learn more about deploying your own Azure OpenAI Service please see https://aka.ms/AIShell/DeployAOAI.
            """;

        if (_settings is null || _settings.GPTs.Count is 0)
        {
            string error = "The agent is currently not ready to serve queries, because there is no GPT defined. Please follow the steps below to configure the setting file properly before using this agent";
            string action = "Define the GPT(s)";
            _description = string.Format(DefaultDescription, error, action);
            return;
        }

        if (_settings.Active is null)
        {
            string error = "Multiple GPTs are defined but the active GPT is not specified. You will be prompted to choose from the available GPTs when sending the first query. Or, if you want to set the active GPT in configuration, please follow the steps below";
            string action = "Set the 'Active' key";
            _description = string.Format(DefaultDescription, error, action);
            return;
        }

        GPT active = _settings.Active;
        _description = $"Active GPT: {active.Name}. {active.Description}";
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
            _chatService.RefreshSettings(_settings);
            UpdateDescription();
        }
    }

    private async Task<bool> SelfCheck(IHost host, CancellationToken token)
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
        string SampleContent = $$"""
        {
          // Declare GPT instances.
          "GPTs": [
              /* --- uncomment the examples below and update as appropriate ---
              //
              // To use the Azure OpenAI service:
              // - Set `Endpoint` to the endpoint of your Azure OpenAI service,
              //     or the endpoint to the Azure API Management service if you are using it as a gateway.
              // - Set `Deployment` to the deployment name of your Azure OpenAI service.
              // - Set `ModelName` to the name of the model used for your deployment, e.g. "gpt-4-0613".
              // - Set `Key` to the access key of your Azure OpenAI service,
              //     or the key of the Azure API Management service if you are using it as a gateway.
              // For example:
              {
                "Name": "ps-az-gpt4",
                "Description": "A GPT instance with expertise in PowerShell scripting and command line utilities. Use gpt-4 running in Azure.",
                "Endpoint": "<insert your Azure OpenAI endpoint>",
                "Deployment": "<insert your deployment name>",
                "ModelName": "<insert the model name>",   // required field to infer properties of the service, such as token limit.
                "Key": "<insert your key>",
                "SystemPrompt": "1. You are a helpful and friendly assistant with expertise in PowerShell scripting and command line.\n2. Assume user is using the operating system `{{Utils.OS}}` unless otherwise specified.\n3. Use the `code block` syntax in markdown to encapsulate any part in responses that is code, YAML, JSON or XML, but not table.\n4. When encapsulating command line code, use '```powershell' if it's PowerShell command; use '```sh' if it's non-PowerShell CLI command.\n5. When generating CLI commands, never ever break a command into multiple lines. Instead, always list all parameters and arguments of the command on the same line.\n6. Please keep the response concise but to the point. Do not overexplain."
              },

              // To use the public OpenAI service:
              // - Ignore the `Endpoint` and `Deployment` keys.
              // - Set `ModelName` to the name of the model to be used.
              // - Set `Key` to be the OpenAI access token.
              // For example:
              {
                "Name": "ps-gpt4o",
                "Description": "A GPT instance with expertise in PowerShell scripting and command line utilities. Use gpt-4o running in OpenAI.",
                "ModelName": "gpt-4o",
                "Key": "<insert your key>",
                "SystemPrompt": "1. You are a helpful and friendly assistant with expertise in PowerShell scripting and command line.\n2. Assume user is using the operating system `Windows 11` unless otherwise specified.\n3. Use the `code block` syntax in markdown to encapsulate any part in responses that is code, YAML, JSON or XML, but not table.\n4. When encapsulating command line code, use '```powershell' if it's PowerShell command; use '```sh' if it's non-PowerShell CLI command.\n5. When generating CLI commands, never ever break a command into multiple lines. Instead, always list all parameters and arguments of the command on the same line.\n6. Please keep the response concise but to the point. Do not overexplain."
              },

              // To use Azure OpenAI service with Entra ID authentication:
              // - Set `Endpoint` to the endpoint of your Azure OpenAI service.
              // - Set `Deployment` to the deployment name of your Azure OpenAI service.
              // - Set `ModelName` to the name of the model used for your deployment, e.g. "gpt-4o".
              // - Set `AuthType` to "EntraID" to use Azure AD credentials.
              // For example:
              {
                "Name": "ps-az-entraId",
                "Description": "A GPT instance with expertise in PowerShell scripting using Entra ID authentication.",
                "Endpoint": "<insert your Azure OpenAI endpoint>",
                "Deployment": "<insert your deployment name>",
                "ModelName": "gpt-4o",
                "AuthType": "EntraID",
                "SystemPrompt": "You are a helpful and friendly assistant with expertise in PowerShell scripting and command line."
              }
              */
          ],

          // Specify the default GPT instance to use for user query.
          // For example: "ps-az-gpt4"
          "Active": null
        }
        """;
        File.WriteAllText(SettingFile, SampleContent, Encoding.UTF8);
    }
}
