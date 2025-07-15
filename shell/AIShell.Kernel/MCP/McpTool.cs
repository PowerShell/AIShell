using System.Text.Json;
using AIShell.Abstraction;
using Microsoft.Extensions.AI;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace AIShell.Kernel.Mcp;

/// <summary>
/// A wrapper class of <see cref="McpClientTool"/> to make sure the call to the tool always go through the AIShell.
/// </summary>
internal class McpTool : AIFunction
{
    internal static readonly string[] UserChoices = ["Continue", "Cancel"];

    private readonly string _fullName;
    private readonly string _serverName;
    private readonly Host _host;
    private readonly McpClientTool _clientTool;

    internal McpTool(string serverName, McpClientTool clientTool, Host host)
    {
        ArgumentException.ThrowIfNullOrEmpty(serverName);
        ArgumentNullException.ThrowIfNull(clientTool);
        ArgumentNullException.ThrowIfNull(host);

        _host = host;
        _clientTool = clientTool;
        _fullName = $"{serverName}{McpManager.ServerToolSeparator}{clientTool.Name}";
        _serverName = serverName;
    }

    /// <summary>
    /// The server name for this tool.
    /// </summary>
    internal string ServerName => _serverName;

    /// <summary>
    /// The original tool name without the server name prefix.
    /// </summary>
    internal string OriginalName => _clientTool.Name;

    /// <summary>
    /// The fully qualified name of the tool in the form of '<server-name>.<tool-name>'
    /// </summary>
    public override string Name => _fullName;

    /// <inheritdoc />
    public override string Description => _clientTool.Description;

    /// <inheritdoc />
    public override JsonElement JsonSchema => _clientTool.JsonSchema;

    /// <inheritdoc />
    public override JsonSerializerOptions JsonSerializerOptions => _clientTool.JsonSerializerOptions;

    /// <inheritdoc />
    public override IReadOnlyDictionary<string, object> AdditionalProperties => _clientTool.AdditionalProperties;

    /// <summary>
    /// Overrides the base method with the call to <see cref="CallAsync"/>. The only difference in behavior is we will serialize
    /// the resulting <see cref="CallToolResponse"/> such that the <see cref="object" /> returned is a <see cref="JsonElement" />
    /// containing the serialized <see cref="CallToolResponse" />. This method is intended to be used polymorphically via the base
    /// class, typically as part of an <see cref="IChatClient" /> operation.
    /// </summary>
    protected override async ValueTask<object> InvokeCoreAsync(AIFunctionArguments arguments, CancellationToken cancellationToken)
    {
        return JsonSerializer.SerializeToElement(
            await CallAsync(
                arguments,
                progress: null,
                JsonSerializerOptions,
                cancellationToken).ConfigureAwait(false),
            McpJsonContext.Default.CallToolResponse);
    }

    /// <summary>
    /// Invokes the tool on the server if the user approves.
    /// </summary>
    /// <param name="arguments">
    /// An optional dictionary of arguments to pass to the tool.
    /// Each key represents a parameter name, and its associated value represents the argument value.
    /// </param>
    /// <param name="progress">
    /// An optional <see cref="IProgress{T}" /> to have progress notifications reported to it.
    /// Setting this to a non-<see langword="null" /> value will result in a progress token being included in the call,
    /// and any resulting progress notifications during the operation routed to this instance.
    /// </param>
    /// <param name="serializerOptions">
    /// The JSON serialization options governing argument serialization.
    /// If <see langword="null" />, the default serialization options will be used.
    /// </param>
    /// <param name="cancellationToken">
    /// The cancellation token to monitor for cancellation requests.
    /// The default is <see cref="CancellationToken.None" />.
    /// </param>
    /// <returns>
    /// A task containing the response from the tool execution. The response includes the tool's output content, which may be structured data, text, or an error message.
    /// </returns>
    /// <remarks>
    /// This method wraps the <see cref="McpClientTool.CallAsync"/> method to add the user interactions for displaying the too call request and prompting the user for approval.
    /// </remarks>
    /// <exception cref="OperationCanceledException">The user rejected the tool call.</exception>
    /// <exception cref="McpException">The server could not find the requested tool, or the server encountered an error while processing the request.</exception>
    internal async ValueTask<CallToolResponse> CallAsync(
        IReadOnlyDictionary<string, object> arguments = null,
        IProgress<ProgressNotificationValue> progress = null,
        JsonSerializerOptions serializerOptions = null,
        CancellationToken cancellationToken = default)
    {
        // Display the tool call request.
        string jsonArgs = arguments is { Count: > 0 }
            ? JsonSerializer.Serialize(arguments, serializerOptions ?? JsonSerializerOptions)
            : null;
        _host.RenderMcpToolCallRequest(this, jsonArgs);

        // Prompt for user's approval to call the tool.
        const string title = "\n\u26A0  MCP servers or malicious conversation content may attempt to misuse 'AIShell' through the installed tools. Please carefully review any requested actions to decide if you want to proceed.";
        string choice = await _host.PromptForSelectionAsync(
            title: title,
            choices: UserChoices,
            cancellationToken: cancellationToken);

        if (choice is "Cancel")
        {
            _host.MarkupLine($"\n    [red]\u2717[/] Cancelled '{OriginalName}'");
            throw new OperationCanceledException("The call was rejected by user.");
        }

        CallToolResponse response = await _host.RunWithSpinnerAsync(
            async () => await _clientTool.CallAsync(arguments, progress, serializerOptions, cancellationToken),
            status: $"Running '{OriginalName}'",
            spinnerKind: SpinnerKind.Processing);

        _host.MarkupLine($"\n    [green]\u2713[/] Ran '{OriginalName}'");
        return response;
    }
}
