using AIShell.Abstraction;
using Microsoft.Extensions.AI;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using System.Threading;

namespace AIShell.Kernel.Mcp;

internal class McpManager
{
    private readonly Task _initTask;
    private readonly McpServerInitContext _context;
    private readonly Dictionary<string, McpServer> _mcpServers;
    private readonly TaskCompletionSource<McpConfig> _parseMcpJsonTaskSource;

    private McpConfig _mcpConfig;
    private Dictionary<string, BuiltInTool> _builtInTools;

    internal const string BuiltInServerName = "AIShell";
    internal const string ServerToolSeparator = "___";
    internal Task<McpConfig> ParseMcpJsonTask => _parseMcpJsonTaskSource.Task;

    internal Dictionary<string, McpServer> McpServers
    {
        get
        {
            _initTask.Wait();
            return _mcpServers;
        }
    }

    internal Dictionary<string, BuiltInTool> BuiltInTools
    {
        get
        {
            _initTask.Wait();
            return _builtInTools;
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

        _builtInTools = BuiltInTool.GetBuiltInTools(_context.Shell);

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
        if (_builtInTools is { Count: > 0 })
        {
            (tools ??= []).AddRange(_builtInTools.Values);
        }

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
        int dotIndex = functionName.IndexOf(ServerToolSeparator);
        if (dotIndex > 0)
        {
            serverName = functionName[..dotIndex];
            toolName = functionName[(dotIndex + 1)..];
        }

        await _initTask;

        AIFunction tool = await ResolveToolAsync(serverName, toolName, cancellationToken);
        if (tool is null)
        {
            return new FunctionResultContent(
                functionCall.CallId,
                $"Error: Requested function \"{functionName}\" not found.");
        }

        FunctionResultContent resultContent = new(functionCall.CallId, result: null);

        try
        {
            var args = new AIFunctionArguments(arguments: functionCall.Arguments);
            if (tool is McpTool mcpTool)
            {
                CallToolResponse response = await mcpTool.CallAsync(args, cancellationToken: cancellationToken);
                resultContent.Result = (object)response ?? "Success: Function completed.";
            }
            else
            {
                var builtInTool = (BuiltInTool)tool;
                PipeMessage response = await builtInTool.CallAsync(args, cancellationToken);
                resultContent.Result = (object)response ?? "Success: Function completed.";
            }
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

    private async Task<AIFunction> ResolveToolAsync(string serverName, string toolName, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(serverName) || string.IsNullOrEmpty(toolName))
        {
            return null;
        }

        if (BuiltInServerName.Equals(serverName, StringComparison.OrdinalIgnoreCase))
        {
            return _builtInTools.TryGetValue(toolName, out BuiltInTool builtInTool) ? builtInTool : null;
        }

        McpTool mcpTool = null;
        if (_mcpServers.TryGetValue(serverName, out McpServer server))
        {
            await server.WaitForInitAsync(cancellationToken);
            server.Tools.TryGetValue(toolName, out mcpTool);
        }

        return mcpTool;
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
