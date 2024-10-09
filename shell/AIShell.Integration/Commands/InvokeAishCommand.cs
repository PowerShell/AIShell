using System.Collections.ObjectModel;
using System.Management.Automation;
using AIShell.Abstraction;

namespace AIShell.Integration.Commands;

[Alias("askai")]
[Cmdlet(VerbsLifecycle.Invoke, "AIShell", DefaultParameterSetName = "Default")]
public class InvokeAIShellCommand : PSCmdlet
{
    private const string DefaultSet = "Default";
    private const string ClipboardSet = "Clipboard";
    private const string PostCodeSet = "PostCode";
    private const string CopyCodeSet = "CopyCode";
    private const string ExitSet = "Exit";

    /// <summary>
    /// Sets and gets the query to be sent to AIShell
    /// </summary>
    [Parameter(Mandatory = true, ValueFromRemainingArguments = true, ParameterSetName = DefaultSet)]
    [Parameter(Mandatory = true, ValueFromRemainingArguments = true, ParameterSetName = ClipboardSet)]
    public string[] Query { get; set; }

    /// <summary>
    /// Sets and gets the agent to use for the query.
    /// </summary>
    [Parameter(ParameterSetName = DefaultSet)]
    [Parameter(ParameterSetName = ClipboardSet)]
    [ValidateNotNullOrEmpty]
    public string Agent { get; set; }

    /// <summary>
    /// Sets and gets the context information for the query.
    /// </summary>
    [Parameter(ValueFromPipeline = true, ParameterSetName = DefaultSet)]
    public PSObject Context { get; set; }

    /// <summary>
    /// Indicates getting context information from clipboard.
    /// </summary>
    [Parameter(Mandatory = true, ParameterSetName = ClipboardSet)]
    public SwitchParameter ContextFromClipboard { get; set; }

    /// <summary>
    /// Indicates running '/code post' from the AIShell.
    /// </summary>
    [Parameter(ParameterSetName = PostCodeSet)]
    public SwitchParameter PostCode { get; set; }

    /// <summary>
    /// Indicates running '/code copy' from the AIShell.
    /// </summary>
    [Parameter(ParameterSetName = CopyCodeSet)]
    public SwitchParameter CopyCode { get; set; }

    /// <summary>
    /// Indicates running '/exit' from the AIShell.
    /// </summary>
    [Parameter(ParameterSetName = ExitSet)]
    public SwitchParameter Exit { get; set; }

    private List<PSObject> _contextObjects;

    protected override void ProcessRecord()
    {
        if (Context is null)
        {
            return;
        }

        _contextObjects ??= [];
        _contextObjects.Add(Context);
    }

    protected override void EndProcessing()
    {
        string message, context = null;

        switch (ParameterSetName)
        {
            case PostCodeSet:
                message = "/code post";
                break;
            case CopyCodeSet:
                message = "/code copy";
                break;
            case ExitSet:
                message = "/exit";
                break;
            default:
                Collection<string> results = null;
                if (_contextObjects is not null)
                {
                    using PowerShell pwsh = PowerShell.Create(RunspaceMode.CurrentRunspace);
                    results = pwsh
                        .AddCommand("Out-String")
                        .AddParameter("InputObject", _contextObjects)
                        .Invoke<string>();
                }
                else if (ContextFromClipboard)
                {
                    using PowerShell pwsh = PowerShell.Create(RunspaceMode.CurrentRunspace);
                    results = pwsh
                        .AddCommand("Get-Clipboard")
                        .AddParameter("Raw")
                        .Invoke<string>();
                }

                context = results?.Count > 0 ? results[0] : null;
                message = string.Join(' ', Query);
                break;
        }

        Channel.Singleton.PostQuery(new PostQueryMessage(message, context, Agent));
    }
}
