using System.Text.Json;
using System.Text.Json.Serialization;
using AIShell.Abstraction;

namespace AIShell.OpenAI.Agent;

internal class Settings
{
    private readonly List<GPT> _gpts;
    private readonly Dictionary<string, GPT> _gptDict;
    private bool _dirty;
    private GPT _active;

    internal bool Dirty => _dirty;

    public List<GPT> GPTs => _gpts;
    public GPT Active => _active;

    public Settings(ConfigData configData)
    {
        _dirty = false;
        _active = null;
        _gpts = configData.GPTs ?? [];
        _gptDict = new Dictionary<string, GPT>(StringComparer.OrdinalIgnoreCase);

        var dups = new HashSet<string>();
        foreach (var gpt in _gpts)
        {
            if (!_gptDict.TryAdd(gpt.Name, gpt))
            {
                dups.Add(gpt.Name);
            }
        }

        if (dups.Count > 0)
        {
            string message = $"Duplicate GPTs are found in the configuration: {string.Join(',', dups)}.";
            throw new InvalidOperationException(message);
        }

        string active = configData.Active;
        if (string.IsNullOrEmpty(active))
        {
            // Active GPT not specified, but there is only one GPT defined, then use it by default.
            _active = _gpts.Count is 1 ? _gpts[0] : null;
        }
        else if (!_gptDict.TryGetValue(active, out _active))
        {
            string message = $"The active GPT '{active}' specified in the configuration doesn't exist.";
            throw new InvalidOperationException(message);
        }
    }

    internal async Task<bool> SelfCheck(IHost host, CancellationToken token)
    {
        if (_gpts.Count is 0)
        {
            host.WriteErrorLine("No GPT instance is available to use. Please update the setting file to declare GPT instances and specify the active GPT to use.");
            return false;
        }

        if (_active is null)
        {
            try
            {
                host.MarkupWarningLine("The active GPT is not specified.");
                string name = await host.PromptForSelectionAsync(
                    title: "Choose from the [green]available GPTs[/] below:",
                    choices: _gpts.Select(gpt => gpt.Name),
                    cancellationToken: token);

                _dirty = true;
                _active = _gptDict[name];
            }
            catch (OperationCanceledException)
            {
                // User cancelled the prompt.
                host.MarkupLine("[red]^C[/]\n");
                return false;
            }
        }

        bool checkPassed = await _active.SelfCheck(host, token);
        _dirty |= _active.Dirty;

        return checkPassed;
    }

    internal void MarkClean()
    {
        _dirty = false;
        foreach (var gpt in _gpts)
        {
            gpt.Dirty = false;
        }
    }

    internal GPT GetGPTByName(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        if (_gptDict.TryGetValue(name, out GPT gpt))
        {
            return gpt;
        }

        throw new InvalidOperationException($"A GPT instance with the name '{name}' doesn't exist.");
    }

    internal void UseGPT(string name)
    {
        _active = GetGPTByName(name);
    }

    internal void UseGPT(GPT gpt)
    {
        _active = gpt;
    }

    internal void ListAllGPTs(IHost host)
    {
        host.RenderTable(
            GPTs,
            [
                new PropertyElement<GPT>(nameof(GPT.Name)),
                new CustomElement<GPT>(label: "Active", m => m.Name == Active?.Name ? "true" : string.Empty),
                new PropertyElement<GPT>(nameof(GPT.Description)),
            ]);
    }

    internal void ShowOneGPT(IHost host, string name)
    {
        var gpt = GetGPTByName(name);
        host.RenderList(
            gpt,
            [
                new PropertyElement<GPT>(nameof(GPT.Name)),
                new PropertyElement<GPT>(nameof(GPT.Description)),
                new PropertyElement<GPT>(nameof(GPT.Endpoint)),
                new PropertyElement<GPT>(nameof(GPT.Deployment)),
                new PropertyElement<GPT>(nameof(GPT.ModelName)),
                new PropertyElement<GPT>(nameof(GPT.AuthType)),
                new PropertyElement<GPT>(nameof(GPT.SystemPrompt)),
            ]);
    }

    internal ConfigData ToConfigData()
    {
        return new ConfigData()
        {
            GPTs = GPTs,
            Active = Active.Name,
        };
    }
}

internal class ConfigData
{
    public List<GPT> GPTs { get; set; }
    public string Active { get; set; }
}

/// <summary>
/// Use source generation to serialize and deserialize the setting file.
/// Both metadata-based and serialization-optimization modes are used to gain the best performance.
/// </summary>
[JsonSourceGenerationOptions(
    WriteIndented = true,
    AllowTrailingCommas = true,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(ConfigData))]
internal partial class SourceGenerationContext : JsonSerializerContext { }
