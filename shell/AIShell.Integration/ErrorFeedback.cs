using System.Management.Automation;
using System.Management.Automation.Subsystem;
using System.Management.Automation.Subsystem.Feedback;
using System.Text;
using AIShell.Abstraction;

namespace AIShell.Integration;

public sealed class ErrorFeedback : IFeedbackProvider
{
    internal const string GUID = "10A13623-CE5E-4808-8346-1DEC831C24BB";

    private readonly Guid _guid;

    internal ErrorFeedback()
    {
        _guid = new Guid(GUID);
        SubsystemManager.RegisterSubsystem(SubsystemKind.FeedbackProvider, this);
    }

    Dictionary<string, string> ISubsystem.FunctionsToDefine => null;

    public Guid Id => _guid;

    public string Name => "AIShell";

    public string Description => "Provide feedback for errors by leveraging AI agents running in AIShell.";

    public FeedbackTrigger Trigger => FeedbackTrigger.Error;

    public FeedbackItem GetFeedback(FeedbackContext context, CancellationToken token)
    {
        // The trigger we listen to is 'Error', so 'LastError' won't be null.
        Channel channel = Channel.Singleton;
        if (channel.CheckConnection(blocking: false, out _))
        {
            string query = CreateQueryForError(context.LastError);
            PostQueryMessage message = new(query, context: null, agent: null);
            channel.PostQuery(message);

            return new FeedbackItem(header: "Check the sidecar for suggestions from AI.", actions: null);
        }

        return null;
    }

    internal static string CreateQueryForError(ErrorRecord lastError)
    {
        Exception exception = lastError.Exception;
        StringBuilder sb = new StringBuilder(capacity: 100)
            .Append("Please troubleshoot the command-line error that happend in the connected PowerShell session, and suggest the fix. The error details are given below:\n\n---\n\n")
            .Append("## Command Line\n")
            .Append("```\n")
            .Append(lastError.InvocationInfo.Line).Append('\n')
            .Append("```\n\n")
            .Append("## Exception Messages\n")
            .Append($"{exception.GetType().FullName}: {exception.Message}\n");

        exception = exception.InnerException;
        if (exception is not null)
        {
            sb.Append("\nInner Exceptions:\n");
            do
            {
                sb.Append($"- {exception.GetType().FullName}: {exception.Message}\n");
                exception = exception.InnerException;
            }
            while (exception is not null);
        }

        string positionMessage = lastError.InvocationInfo.PositionMessage;
        if (!string.IsNullOrEmpty(positionMessage))
        {
            sb.Append("\n## Error Position\n")
              .Append("```\n")
              .Append(positionMessage).Append('\n')
              .Append("```\n");
        }

        return sb.ToString();
    }

    internal void Unregister()
    {
        SubsystemManager.UnregisterSubsystem<IFeedbackProvider>(_guid);
    }
}
