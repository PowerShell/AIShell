using System.CommandLine;
using System.Text;
using AIShell.Abstraction;

namespace Microsoft.Azure.Agent;

internal sealed class ReplaceCommand : CommandBase
{
    private readonly AzureAgent _agent;
    private readonly Dictionary<string, string> _values;
    private readonly HashSet<string> _productNames;
    private readonly HashSet<string> _environmentNames;

    public ReplaceCommand(AzureAgent agent)
        : base("replace", "Replace argument placeholders in the generated scripts with the real value.")
    {
        _agent = agent;
        _values = [];
        _productNames = [];
        _environmentNames = [];

        this.SetHandler(ReplaceAction);
    }

    private static string SyntaxHighlightAzCommand(string command, string parameter, string placeholder)
    {
        const string vtItalic = "\x1b[3m";
        const string vtCommand = "\x1b[93m";
        const string vtParameter = "\x1b[90m";
        const string vtVariable = "\x1b[92m";
        const string vtFgDefault = "\x1b[39m";
        const string vtReset = "\x1b[0m";

        StringBuilder cStr = new(capacity: command.Length + parameter.Length + placeholder.Length + 50);
        cStr.Append(vtItalic)
            .Append(vtCommand).Append("az").Append(vtFgDefault).Append(command.AsSpan(2)).Append(' ')
            .Append(vtParameter).Append(parameter).Append(vtFgDefault).Append(' ')
            .Append(vtVariable).Append(placeholder).Append(vtFgDefault)
            .Append(vtReset);

        return cStr.ToString();
    }

    private void ReplaceAction()
    {
        _values.Clear();
        _productNames.Clear();
        _environmentNames.Clear();

        IHost host = Shell.Host;
        ArgumentPlaceholder ap = _agent.ArgPlaceholder;

        if (ap is null)
        {
            host.WriteErrorLine("No argument placeholder to replace.");
            return;
        }

        DataRetriever dataRetriever = ap.DataRetriever;
        List<PlaceholderItem> items = ap.ResponseData.PlaceholderSet;
        string subText = items.Count > 1
            ? $"all {items.Count} argument placeholders"
            : "the argument placeholder";
        host.WriteLine($"\nWe'll provide assistance in replacing {subText} and regenerating the result. You can press 'Enter' to skip to the next parameter or press 'Ctrl+c' to exit the assistance.\n");
        host.RenderDivider("Input Values", DividerAlignment.Left);
        host.WriteLine();

        try
        {
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                var (command, parameter) = dataRetriever.GetMappedCommand(item.Name);

                string desc = item.Desc.TrimEnd('.');
                string coloredCmd = parameter is null ? null : SyntaxHighlightAzCommand(command, parameter, item.Name);
                string cmdPart = coloredCmd is null ? null : $" [{coloredCmd}]";

                host.WriteLine(item.Type is "string"
                    ? $"{i+1}. {desc}{cmdPart}"
                    : $"{i+1}. {desc}{cmdPart}. Value type: {item.Type}");

                // Get the task for creating the 'ArgumentInfo' object and show a spinner
                // if we have to wait for the task to complete.
                Task<ArgumentInfo> argInfoTask = dataRetriever.GetArgInfo(item.Name);
                ArgumentInfo argInfo = argInfoTask.IsCompleted
                    ? argInfoTask.Result
                    : host.RunWithSpinnerAsync(
                        () => WaitForArgInfoAsync(argInfoTask),
                        status: $"Requesting data for '{item.Name}' ...",
                        SpinnerKind.Processing).GetAwaiter().GetResult();

                argInfo ??= new ArgumentInfo(item.Name, item.Desc, Enum.Parse<ArgumentInfo.DataType>(item.Type));

                // Write out restriction for this argument if there is any.
                if (!string.IsNullOrEmpty(argInfo.Restriction))
                {
                    host.WriteLine(argInfo.Restriction);
                }

                ArgumentInfoWithNamingRule nameArgInfo = null;
                if (argInfo is ArgumentInfoWithNamingRule v)
                {
                    nameArgInfo = v;
                    SuggestForResourceName(nameArgInfo.NamingRule, nameArgInfo.Suggestions);
                }

                // Prompt for argument without printing captions again.
                string value = host.PromptForArgument(argInfo, printCaption: false);
                if (!string.IsNullOrEmpty(value))
                {
                    _values.Add(item.Name, value);
                    _agent.SaveUserValue(item.Name, value);

                    if (nameArgInfo is not null && nameArgInfo.NamingRule.TryMatchName(value, out string prodName, out string envName))
                    {
                        _productNames.Add(prodName.ToLower());
                        _environmentNames.Add(envName.ToLower());
                    }
                }

                // Write an extra new line.
                host.WriteLine();
            }
        }
        catch (OperationCanceledException)
        {
            bool proceed = false;
            if (_values.Count > 0)
            {
                host.WriteLine();
                proceed = host.PromptForConfirmationAsync(
                    "Would you like to regenerate with the provided values so far?",
                    defaultValue: false,
                    CancellationToken.None).GetAwaiter().GetResult();
                host.WriteLine();
            }

            if (!proceed)
            {
                host.WriteLine();
                return;
            }
        }

        if (_values.Count > 0)
        {
            host.RenderDivider("Summary", DividerAlignment.Left);
            host.WriteLine("\nThe following placeholders will be replace:");
            host.RenderList(_values);

            host.RenderDivider("Regenerate", DividerAlignment.Left);
            host.MarkupLine($"\nQuery: [teal]{ap.Query}[/]");

            try
            {
                string answer = host.RunWithSpinnerAsync(RegenerateAsync).GetAwaiter().GetResult();
                host.RenderFullResponse(answer);
            }
            catch (OperationCanceledException)
            {
                // User cancelled the operation.
            }
        }
        else
        {
            host.WriteLine("No value was specified for any of the argument placeholders.");
        }
    }

    private void SuggestForResourceName(NamingRule rule, IList<string> suggestions)
    {
        if (_productNames.Count is 0)
        {
            return;
        }

        foreach (string prodName in _productNames)
        {
            if (_environmentNames.Count is 0)
            {
                suggestions.Add($"{prodName}-{rule.Abbreviation}");
                continue;
            }

            foreach (string envName in _environmentNames)
            {
                suggestions.Add($"{prodName}-{rule.Abbreviation}-{envName}");
            }
        }
    }

    private async Task<ArgumentInfo> WaitForArgInfoAsync(Task<ArgumentInfo> argInfoTask)
    {
        var token = Shell.CancellationToken;
        var cts = CancellationTokenSource.CreateLinkedTokenSource(token);

        // Do not let the user wait for more than 2 seconds.
        var delayTask = Task.Delay(2000, cts.Token);
        var completedTask = await Task.WhenAny(argInfoTask, delayTask);

        if (completedTask == delayTask)
        {
            if (delayTask.IsCanceled)
            {
                // User cancelled the operation.
                throw new OperationCanceledException(token);
            }

            // Timed out. Last try to see if it finished. Otherwise, return null.
            return argInfoTask.IsCompletedSuccessfully ? argInfoTask.Result : null;
        }

        // Finished successfully, so we cancel the delay task and return the result.
        cts.Cancel();
        return argInfoTask.Result;
    }

    /// <summary>
    /// We use the pseudo values to regenerate the response data, so that real values will never go off the user's box.
    /// </summary>
    /// <returns></returns>
    private async Task<string> RegenerateAsync()
    {
        ArgumentPlaceholder ap = _agent.ArgPlaceholder;

        // We are doing the replacement locally, but want to fake the regeneration.
        await Task.Delay(2000, Shell.CancellationToken);

        ResponseData data = ap.ResponseData;
        _agent.ReplaceKnownPlaceholders(data);

        if (data.PlaceholderSet is null)
        {
            _agent.ArgPlaceholder.DataRetriever.Dispose();
            _agent.ArgPlaceholder = null;
        }

        return _agent.GenerateAnswer(data);
    }
}
