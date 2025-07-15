namespace AIShell.Integration.Commands;

using System.Management.Automation;

[Alias("airun")]
[Cmdlet(VerbsLifecycle.Invoke, "AICommand")]
public sealed class InvokeAICommand : PSCmdlet, IDisposable
{
    private readonly PowerShell _pwsh;
    private readonly PSDataCollection<PSObject> _output;

    private bool _disposed, _hadErrors, _cancelled;
    private List<object> _capturedContent;

    [Parameter(Mandatory = true, Position = 0)]
    public ScriptBlock Command { get; set; }

    public InvokeAICommand()
    {
        _pwsh = PowerShell.Create(RunspaceMode.CurrentRunspace);
        _pwsh.Streams.Error.DataAdding += DataAddingHandler;

        _output = [];
        _output.DataAdding += DataAddingHandler;
        _capturedContent = null;
    }

    /// <summary>
    /// The handler for both 'OutputDataAdding' and 'ErrorDataAdding' events.
    /// </summary>
    /// <remarks>
    /// The handler is called on the pipeline thread, so it's safe to call 'WriteObject' in it.
    /// </remarks>
    private void DataAddingHandler(object sender, DataAddingEventArgs e)
    {
        object item = e.ItemAdded;
        _capturedContent?.Add(item);
        WriteObject(item);
    }

    protected override void EndProcessing()
    {
        string commandToRun = Command.ToString();
        string requestedCommand = Channel.Singleton.GetRunCommandRequest();

        if (requestedCommand is not null && commandToRun.Contains(requestedCommand))
        {
            // Only capture output when this is a tool call invoked by AI.
            _capturedContent = [];
        }

        try
        {
            _pwsh.AddScript(commandToRun, useLocalScope: false);
            _pwsh.Invoke(input: null, _output, settings: null);
        }
        finally
        {
            _hadErrors = _pwsh.HadErrors;
        }
    }

    protected override void StopProcessing()
    {
        _pwsh.Stop();
        _cancelled = true;
    }

    /// <summary>
    /// Dispose the resources.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        if (_capturedContent is { })
        {
            Channel.Singleton.SetRunCommandResult(_hadErrors, _cancelled, _capturedContent);
        }

        _output.DataAdding -= DataAddingHandler;
        _output.Dispose();
        _pwsh.Streams.Error.DataAdding -= DataAddingHandler;
        _pwsh.Dispose();
        _disposed = true;
    }
}
