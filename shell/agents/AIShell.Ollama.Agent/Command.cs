using System.CommandLine;
using System.CommandLine.Completions;
using System.Threading.Tasks;
using AIShell.Abstraction;

namespace AIShell.Ollama.Agent;

internal sealed class ConfigCommand : CommandBase
{
    private readonly OllamaAgent _agnet;
     public ConfigCommand(OllamaAgent agent)
        : base("config", "Command for config management within the 'ollama' agent.")
    {
        _agnet = agent;

        var use = new Command("use", "Specify a config to use.");
        var useConfig = new Argument<string>(
            name: "Config",
            getDefaultValue: () => null,
            description: "Name of a configuration.").AddCompletions(ConfigNameCompleter);
        use.AddArgument(useConfig);
        use.SetHandler(UseConfigAction, useConfig);

        var list = new Command("list", "List a specific config, or all available configs.");
        var listConfig = new Argument<string>(
            name: "Config",
            getDefaultValue: () => null,
            description: "Name of a configuration.").AddCompletions(ConfigNameCompleter);
        list.AddArgument(listConfig);
        list.SetHandler(ListConfigAction, listConfig);

        AddCommand(list);
        AddCommand(use);
    }

    private void ListConfigAction(string name)
    {
        IHost host = Shell.Host;

        // Reload the setting file if needed.
        _agnet.ReloadSettings();

        Settings settings = _agnet.Settings;

        if (settings is null)
        {
            host.WriteErrorLine("Invalid configuration.");
            return;
        }

        if (string.IsNullOrEmpty(name))
        {
            settings.ListAllConfigs(host);
            return;
        }

        try
        {
            settings.ShowOneConfig(host, name);
        }
        catch (InvalidOperationException ex)
        {
            string availableConfigNames = ConfigNamesAsString();
            host.WriteErrorLine($"{ex.Message} Available cofiguration(s): {availableConfigNames}.");
        }
    }

    private async Task UseConfigAction(string name)
    {
        // Reload the setting file if needed.
        _agnet.ReloadSettings();

        var setting = _agnet.Settings;
        var host = Shell.Host;

        if (setting is null || setting.Configs.Count is 0)
        {
            host.WriteErrorLine("No configs configured.");
            return;
        }

        try
        {
            ModelConfig chosenConfig = (string.IsNullOrEmpty(name)
                ? host.PromptForSelectionAsync(
                    title: "[orange1]Please select a [Blue]Configuration[/] to use[/]:",
                    choices: setting.Configs,
                    converter: ConfigName,
                    CancellationToken.None).GetAwaiter().GetResult()
                : setting.Configs.FirstOrDefault(c => c.Name == name)) ?? throw new InvalidOperationException($"The configuration '{name}' doesn't exist.");
            await setting.UseConfg(host, chosenConfig);
            host.MarkupLine($"Using the config [green]{chosenConfig.Name}[/]:");
        }
        catch (InvalidOperationException ex)
        {
            string availableConfigNames = ConfigNamesAsString();
            host.WriteErrorLine($"{ex.Message} Available configurations: {availableConfigNames}.");
        }
    }

    private static string ConfigName(ModelConfig config) => config.Name.Any(Char.IsWhiteSpace) ? $"\"{config.Name}\"" : config.Name;
    private IEnumerable<string> ConfigNameCompleter(CompletionContext context) => _agnet.Settings?.Configs?.Select(ConfigName) ?? [];
    private string ConfigNamesAsString() => string.Join(", ", ConfigNameCompleter(null));
}

internal sealed class SystemPromptCommand : CommandBase
{
    private readonly OllamaAgent _agnet;

    public SystemPromptCommand(OllamaAgent agent)
        : base("system-prompt", "Command for system prompt management within the 'ollama' agent.")
    {
        _agnet = agent;

        var show = new Command("show", "Show the current system prompt.");
        show.SetHandler(ShowSystemPromptAction);

        var set = new Command("set", "Sets the system prompt.");
        var systemPromptModel = new Argument<string>(
            name: "System-Prompt",
            getDefaultValue: () => null,
            description: "The system prompt");
        set.AddArgument(systemPromptModel);
        set.SetHandler(SetSystemPromptAction, systemPromptModel);

        AddCommand(show);
        AddCommand(set);
    }

    private void ShowSystemPromptAction()
    {
        IHost host = Shell.Host;

        // Reload the setting file if needed.
        _agnet.ReloadSettings();

        Settings settings = _agnet.Settings;

        if (settings is null)
        {
            host.WriteErrorLine("Invalid configuration.");
            return;
        }

        try
        {
            settings.ShowSystemPrompt(host);
        }
        catch (InvalidOperationException ex)
        {
            host.WriteErrorLine($"{ex.Message}");
        }
    }

    private void SetSystemPromptAction(string prompt)
    {
        IHost host = Shell.Host;

        // Reload the setting file if needed.
        _agnet.ReloadSettings();
        _agnet.ResetContext();

        Settings settings = _agnet.Settings;

        if (settings is null)
        {
            host.WriteErrorLine("Invalid configuration.");
            return;
        }

        try
        {
            settings.SetSystemPrompt(host, prompt);
        }
        catch (InvalidOperationException ex)
        {
            host.WriteErrorLine($"{ex.Message}.");
        }
    }
}

internal sealed class ModelCommand : CommandBase
{
    private readonly OllamaAgent _agnet;

    public ModelCommand(OllamaAgent agent)
        : base("model", "Command for model management within the 'ollama' agent.")
    {
        _agnet = agent;

        var use = new Command("use", "Specify a model to use, or choose one from the available models.");
        var useModel = new Argument<string>(
            name: "Model",
            getDefaultValue: () => null,
            description: "Name of a model.").AddCompletions(ModelNameCompleter);
        use.AddArgument(useModel);
        use.SetHandler(UseModelAction, useModel);

        var list = new Command("list", "List a specific model, or all available models.");
        var listModel = new Argument<string>(
            name: "Model",
            getDefaultValue: () => null,
            description: "Name of a model.").AddCompletions(ModelNameCompleter);
        list.AddArgument(listModel);
        list.SetHandler(ListModelAction, listModel);

        AddCommand(list);
        AddCommand(use);
    }

    private void ListModelAction(string name)
    {
        IHost host = Shell.Host;

        // Reload the setting file if needed.
        _agnet.ReloadSettings();

        Settings settings = _agnet.Settings;

        if (settings is null)
        {
            host.WriteErrorLine("Invalid configuration.");
            return;
        }

        if (string.IsNullOrEmpty(name))
        {
            settings.ListAllModels(host).GetAwaiter().GetResult();
            return;
        }

        try
        {
            settings.ShowOneModel(host, name).GetAwaiter().GetResult();
        }
        catch (InvalidOperationException ex)
        {
            string availableModelNames = ModelNamesAsString();
            host.WriteErrorLine($"{ex.Message} Available Models(s): {availableModelNames}.");
        }
    }

    private void UseModelAction(string name)
    {
        // Reload the setting file if needed.
        _agnet.ReloadSettings();

        var setting = _agnet.Settings;
        var host = Shell.Host;

        if (setting is null || setting.GetAllModels().GetAwaiter().GetResult().Count is 0)
        {
            host.WriteErrorLine("No models configured.");
            return;
        }

        try
        {
            OllamaModel chosenModel = string.IsNullOrEmpty(name)
                ? host.PromptForSelectionAsync(
                    title: "[orange1]Please select a [Blue]Model[/] to use[/]:",
                    choices: setting.GetAllModels().GetAwaiter().GetResult(),
                    converter: ModelName,
                    CancellationToken.None).GetAwaiter().GetResult()
                : setting.GetModelByName(name).GetAwaiter().GetResult();

            setting.UseModel(chosenModel);
            host.MarkupLine($"Using the model [green]{chosenModel.Name}[/]:");
        }
        catch (InvalidOperationException ex)
        {
            string availableModelNames = ModelNamesAsString();
            host.WriteErrorLine($"{ex.Message} Available Modless: {availableModelNames}.");
        }
    }

    private static string ModelName(OllamaModel model) => model.Name;
    private IEnumerable<string> ModelNameCompleter(CompletionContext context) => _agnet.Settings?.GetAllModels().GetAwaiter().GetResult().Select(ModelName) ?? [];
    private string ModelNamesAsString() => string.Join(", ", ModelNameCompleter(null));
}
