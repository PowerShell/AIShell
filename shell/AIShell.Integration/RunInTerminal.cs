namespace AIShell.Integration;

internal class RunCommandRequest : IDisposable
{
    internal string Id { get; }
    internal string Command { get; }
    internal ManualResetEventSlim Event { get; }
    internal RunCommandResult Result { get; set; }

    internal RunCommandRequest(string command, bool blockingCall)
    {
        ArgumentException.ThrowIfNullOrEmpty(command);

        Id = Guid.NewGuid().ToString();
        Command = command;
        Event = blockingCall ? new() : null;
        Result = null;
    }

    public void Dispose()
    {
        Event?.Dispose();
    }
}

internal class RunCommandResult
{
    internal bool HadErrors { get; }
    internal bool UserCancelled { get; }
    internal List<object> ErrorAndOutput { get; }

    internal RunCommandResult(bool hadErrors, bool userCancelled, List<object> errorAndOutput)
    {
        ArgumentNullException.ThrowIfNull(errorAndOutput);

        HadErrors = hadErrors;
        UserCancelled = userCancelled;
        ErrorAndOutput = errorAndOutput;
    }
}
