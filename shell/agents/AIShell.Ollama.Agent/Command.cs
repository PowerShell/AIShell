using System.CommandLine;
using System.CommandLine.Completions;
using System.Threading.Tasks;
using AIShell.Abstraction;

namespace AIShell.Ollama.Agent;

internal sealed class PresetCommand : CommandBase
{
    private readonly OllamaAgent _agnet;
    
    public PresetCommand(OllamaAgent agent)
        : base("preset", "Command for preset management within the 'ollama' agent.")
    {
        _agnet = agent;

        var use = new Command("use", "Specify a preset to use.");
        var usePreset = new Argument<string>(
            name: "Preset",
            getDefaultValue: () => null,
            description: "Name of a preset.").AddCompletions(PresetNameCompleter);
        use.AddArgument(usePreset);
        use.SetHandler(UsePresetAction, usePreset);

        var list = new Command("list", "List a specific preset, or all configured presets.");
        var listPreset = new Argument<string>(
            name: "Preset",
            getDefaultValue: () => null,
            description: "Name of a preset.").AddCompletions(PresetNameCompleter);
        list.AddArgument(listPreset);
        list.SetHandler(ListPresetAction, listPreset);

        AddCommand(list);
        AddCommand(use);
    }

    private void ListPresetAction(string name)
    {
        IHost host = Shell.Host;

        // Reload the setting file if needed.
        _agnet.ReloadSettings();

        Settings settings = _agnet.Settings;

        if (settings is null)
        {
            host.WriteErrorLine("Error loading the configuration.");
            return;
        }

        try
        {
            if (string.IsNullOrEmpty(name))
            {
                settings.ListAllPresets(host);
                return;
            }

            settings.ShowOnePreset(host, name);
        }
        catch (InvalidOperationException ex)
        {
            string availablePresetNames = PresetNamesAsString();
            host.WriteErrorLine($"{ex.Message} Available preset(s): {availablePresetNames}.");
        }
    }

    private async Task UsePresetAction(string name)
    {
        // Reload the setting file if needed.
        _agnet.ReloadSettings();

        var setting = _agnet.Settings;
        var host = Shell.Host;

        if (setting is null)
        {
            host.WriteErrorLine("Error loading the configuration.");
            return;
        }

        if (setting.Presets.Count is 0)
        {
            host.WriteErrorLine("There are no presets configured.");
            return;
        }

        try
        {
            ModelConfig chosenPreset = (string.IsNullOrEmpty(name)
                ? host.PromptForSelectionAsync(
                    title: "[orange1]Please select a [Blue]Preset[/] to use[/]:",
                    choices: setting.Presets,
                    converter: PresetName,
                    CancellationToken.None).GetAwaiter().GetResult()
                : setting.Presets.FirstOrDefault(c => c.Name == name)) ?? throw new InvalidOperationException($"The preset '{name}' doesn't exist.");
            await setting.UsePreset(host, chosenPreset);
            host.MarkupLine($"Using the preset [green]{chosenPreset.Name}[/]:");
        }
        catch (InvalidOperationException ex)
        {
            string availablePresetNames = PresetNamesAsString();
            host.WriteErrorLine($"{ex.Message} Available presets: {availablePresetNames}.");
        }
    }

    private static string PresetName(ModelConfig preset) => preset.Name.Any(Char.IsWhiteSpace) ? $"\"{preset.Name}\"" : preset.Name;
    private IEnumerable<string> PresetNameCompleter(CompletionContext context) => _agnet.Settings?.Presets?.Select(PresetName) ?? [];
    private string PresetNamesAsString() => string.Join(", ", PresetNameCompleter(null));
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
            host.WriteErrorLine("Error loading the configuration.");
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
            host.WriteErrorLine("Error loading the configuration.");
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
            host.WriteErrorLine("Error loading the configuration.");
            return;
        }
        try
        {
            if (string.IsNullOrEmpty(name))
            {
                settings.ListAllModels(host).GetAwaiter().GetResult();
                return;
            }

            settings.ShowOneModel(host, name).GetAwaiter().GetResult();
        }
        catch (InvalidOperationException ex)
        {
            host.WriteErrorLine(ex.Message);
        }
    }

    private void UseModelAction(string name)
    {
        // Reload the setting file if needed.
        _agnet.ReloadSettings();

        var settings = _agnet.Settings;
        var host = Shell.Host;

        if (settings is null)
        {
            host.WriteErrorLine("Error loading the configuration.");
            return;
        }

        try
        {
            if (!settings.PerformSelfcheck(host))
            {
                return;
            }

            if (settings.GetAllModels().GetAwaiter().GetResult().Count is 0)
            {
                host.WriteErrorLine("No models found.");
                return;
            }

            if (string.IsNullOrEmpty(name))
            {
                name = host.PromptForSelectionAsync(
                    title: "[orange1]Please select a [Blue]Model[/] to use[/]:",
                    choices: settings.GetAllModels(host).GetAwaiter().GetResult(),
                    CancellationToken.None).GetAwaiter().GetResult();
            }

            settings.UseModel(host, name).GetAwaiter().GetResult();
            host.MarkupLine($"Using the model [green]{name}[/]");
        }
        catch (InvalidOperationException ex)
        {
            host.WriteErrorLine(ex.Message);
        }
    }

    private IEnumerable<string> ModelNameCompleter(CompletionContext context) => _agnet.Settings?.GetAllModels().GetAwaiter().GetResult() ?? [];
}
