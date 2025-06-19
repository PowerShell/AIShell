using Microsoft.Extensions.AI;

namespace AIShell.Abstraction;

/// <summary>
/// The shell interface to interact with the AIShell.
/// </summary>
public interface IShell
{
    /// <summary>
    /// The host of the AIShell.
    /// </summary>
    IHost Host { get; }

    /// <summary>
    /// Indicates whether the bi-directional channel with an application (e.g. a PowerShell session) has been established.
    /// </summary>
    bool ChannelEstablished { get; }

    /// <summary>
    /// The token to indicate cancellation when `Ctrl+c` is pressed by user.
    /// </summary>
    CancellationToken CancellationToken { get; }

    /// <summary>
    /// Extracts code blocks that are surrounded by code fences from the passed-in markdown text.
    /// </summary>
    /// <param name="text">The markdown text.</param>
    /// <returns>A list of code blocks or null if there is no code block.</returns>
    List<CodeBlock> ExtractCodeBlocks(string text, out List<SourceInfo> sourceInfos);

    /// <summary>
    /// Get available <see cref="AIFunction"/> instances for LLM to use.
    /// </summary>
    /// <returns></returns>
    Task<List<AIFunction>> GetAIFunctions();

    /// <summary>
    /// Call an AI function.
    /// </summary>
    /// <param name="functionCall">A <see cref="FunctionCallContent"/> instance representing the function call request.</param>
    /// <param name="captureException">Whether or not to capture the exception thrown from calling the tool.</param>
    /// <param name="includeDetailedErrors">Whether or not to include the exception message to the message of the call result.</param>
    /// <param name="cancellationToken">The cancellation token to cancel the call.</param>
    /// <returns></returns>
    Task<FunctionResultContent> CallAIFunction(
        FunctionCallContent functionCall,
        bool captureException,
        bool includeDetailedErrors,
        CancellationToken cancellationToken);

    // TODO:
    // - methods to run code: python, command-line, powershell, node-js.
    // - methods to communicate with shell client.
}
