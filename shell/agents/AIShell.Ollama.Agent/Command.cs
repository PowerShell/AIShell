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
        catch (Exception ex)
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
                ? await host.PromptForSelectionAsync(
                    title: "[orange1]Please select a [Blue]Preset[/] to use[/]:",
                    choices: setting.Presets,
                    converter: PresetName,
                    CancellationToken.None)
                : setting.Presets.FirstOrDefault(c => c.Name == name)) ?? throw new InvalidOperationException($"The preset '{name}' doesn't exist.");
            await setting.UsePreset(host, chosenPreset);
            host.MarkupLine($"Using the preset [green]{chosenPreset.Name}[/]:");
        }
        catch (Exception ex)
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
        catch (Exception ex)
        {
            host.WriteErrorLine(ex.Message);
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
        catch (Exception ex)
        {
            host.WriteErrorLine(ex.Message);
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

    private async Task ListModelAction(string name)
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
                await settings.ListAllModels(host);
                return;
            }

            await settings.ShowOneModel(host, name);
        }
        catch (Exception ex)
        {
            host.WriteErrorLine(ex.Message);
        }
    }

    private async Task UseModelAction(string name)
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
            bool success = await settings.PerformSelfcheck(host, checkEndpointOnly: true);
            if (!success)
            {
                return;
            }

            var allModels = await settings.GetAllModels();
            if (allModels.Count is 0)
            {
                host.WriteErrorLine($"No models found from '{settings.Endpoint}'.");
                return;
            }

            if (string.IsNullOrEmpty(name))
            {
                name = await host.PromptForSelectionAsync(
                    title: "[orange1]Please select a [Blue]Model[/] to use[/]:",
                    choices: allModels,
                    CancellationToken.None);
            }

            await settings.UseModel(host, name);
            host.MarkupLine($"Using the model [green]{name}[/]");
        }
        catch (Exception ex)
        {
            host.WriteErrorLine(ex.Message);
        }
    }

    private IEnumerable<string> ModelNameCompleter(CompletionContext context)
    {
        try
        {
            // Model retrieval may throw.
            var results = _agnet.Settings?.GetAllModels().Result;
            if (results is not null)
            {
                return results;
            }
        }
        catch (Exception) { }

        return [];
    }
}
