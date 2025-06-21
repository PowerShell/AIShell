using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace AIShell.Kernel.Mcp;

internal class McpManager
{
    private readonly Task _initTask;
    private readonly McpServerInitContext _context;
    private readonly Dictionary<string, McpServer> _mcpServers;
    private readonly TaskCompletionSource<McpConfig> _parseMcpJsonTaskSource;

    private McpConfig _mcpConfig;

    internal Task<McpConfig> ParseMcpJsonTask => _parseMcpJsonTaskSource.Task;

    internal Dictionary<string, McpServer> McpServers
    {
        get
        {
            _initTask.Wait();
            return _mcpServers;
        }
    }

    internal McpManager(Shell shell)
    {
        _context = new(shell);
        _parseMcpJsonTaskSource = new();
        _mcpServers = new(StringComparer.OrdinalIgnoreCase);

        _initTask = Task.Run(Initialize);
    }

    private void Initialize()
    {
        try
        {
            _mcpConfig = McpConfig.Load();
            _parseMcpJsonTaskSource.SetResult(_mcpConfig);
        }
        catch (Exception e)
        {
            _parseMcpJsonTaskSource.SetException(e);
        }

        if (_mcpConfig is null)
        {
            return;
        }

        foreach (var (name, config) in _mcpConfig.Servers)
        {
            _mcpServers.Add(name, new McpServer(config, _context));
        }
    }

    /// <summary>
    /// Lists tools that are available at the time of the call.
    /// Servers that are still initializing or failed will be skipped.
    /// </summary>
    internal async Task<List<AIFunction>> ListAvailableTools()
    {
        await _initTask;

        List<AIFunction> tools = null;
        foreach (var (name, server) in _mcpServers)
        {
            if (server.IsOperational)
            {
                (tools ??= []).AddRange(server.Tools.Values);
            }
        }

        return tools;
    }

    /// <summary>
    /// Make a tool call using the given function call data.
    /// </summary>
    /// <param name="functionCall">The function call request.</param>
    /// <param name="captureException">Whether or not to capture the exception thrown from calling the tool.</param>
    /// <param name="includeDetailedErrors">Whether or not to include the exception message to the message of the call result.</param>
    /// <param name="cancellationToken">The cancellation token to cancel the call.</param>
    /// <returns></returns>
    internal async Task<FunctionResultContent> CallToolAsync(
        FunctionCallContent functionCall,
        bool captureException = false,
        bool includeDetailedErrors = false,
        CancellationToken cancellationToken = default)
    {
        string serverName = null, toolName = null;

        string functionName = functionCall.Name;
        int dotIndex = functionName.IndexOf(McpTool.ServerToolSeparator);
        if (dotIndex > 0)
        {
            serverName = functionName[..dotIndex];
            toolName = functionName[(dotIndex + 1)..];
        }

        await _initTask;

        McpTool tool = null;
        if (!string.IsNullOrEmpty(serverName)
            && !string.IsNullOrEmpty(toolName)
            && _mcpServers.TryGetValue(serverName, out McpServer server))
        {
            await server.WaitForInitAsync(cancellationToken);
            server.Tools.TryGetValue(toolName, out tool);
        }

        if (tool is null)
        {
            return new FunctionResultContent(
                functionCall.CallId,
                $"Error: Requested function \"{functionName}\" not found.");
        }

        FunctionResultContent resultContent = new(functionCall.CallId, result: null);

        try
        {
            CallToolResponse response = await tool.CallAsync(
                new AIFunctionArguments(arguments: functionCall.Arguments),
                cancellationToken: cancellationToken);

            resultContent.Result = (object)response ?? "Success: Function completed.";
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested)
        {
            if (!captureException)
            {
                throw;
            }

            string message = "Error: Function failed.";
            resultContent.Exception = e;
            resultContent.Result = includeDetailedErrors ? $"{message} Exception: {e.Message}" : message;
        }

        return resultContent;
    }
}

internal class McpServerInitContext
{
    /// <summary>
    /// The throttle limit defines the maximum number of servers that can be initiated concurrently.
    /// </summary>
    private const int ThrottleLimit = 5;

    internal McpServerInitContext(Shell shell)
    {
        Shell = shell;
        ThrottleSemaphore = new SemaphoreSlim(ThrottleLimit, ThrottleLimit);
        ClientOptions = new()
        {
            ClientInfo = new() { Name = "AIShell", Version = shell.Version },
            InitializationTimeout = TimeSpan.FromSeconds(30),
        };
    }

    internal Shell Shell { get; }
    internal SemaphoreSlim ThrottleSemaphore { get; }
    internal McpClientOptions ClientOptions { get; }
}
