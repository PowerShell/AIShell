using ModelContextProtocol.Client;

namespace AIShell.Kernel.Mcp;

internal class McpServer : IDisposable
{
    private readonly McpServerConfig _config;
    private readonly McpServerInitContext _context;
    private readonly Dictionary<string, McpTool> _tools;
    private readonly Task _initTask;

    private string _serverInfo;
    private IMcpClient _client;
    private Exception _error;

    /// <summary>
    /// Name of the server declared in mcp.json.
    /// </summary>
    internal string Name => _config.Name;

    /// <summary>
    /// Gets whether the initialization is done.
    /// </summary>
    internal bool IsInitFinished => _initTask.IsCompleted;

    /// <summary>
    /// Gets whether the server is operational.
    /// </summary>
    internal bool IsOperational => _initTask.IsCompleted && _error is null;

    /// <summary>
    /// Full name and version of the server.
    /// </summary>
    internal string ServerInfo
    {
        get
        {
            WaitForInit();
            return _serverInfo;
        }
    }

    /// <summary>
    /// The client connected to the server.
    /// </summary>
    internal IMcpClient Client
    {
        get
        {
            WaitForInit();
            return _client;
        }
    }

    /// <summary>
    /// Exposed tools from the server.
    /// </summary>
    internal Dictionary<string, McpTool> Tools
    {
        get
        {
            WaitForInit();
            return _tools;
        }
    }

    internal Exception Error
    {
        get
        {
            WaitForInit();
            return _error;
        }
    }

    internal McpServer(McpServerConfig config, McpServerInitContext context)
    {
        _config = config;
        _context = context;
        _tools = new(StringComparer.OrdinalIgnoreCase);
        _initTask = Initialize();
    }

    private async Task Initialize()
    {
        try
        {
            await _context.ThrottleSemaphore.WaitAsync();

            IClientTransport transport = _config.ToClientTransport();
            _client = await McpClientFactory.CreateAsync(transport, _context.ClientOptions);

            var serverInfo = _client.ServerInfo;
            // An MCP server may have the name included in the version info.
            _serverInfo = serverInfo.Version.Contains(serverInfo.Name, StringComparison.OrdinalIgnoreCase)
                ? serverInfo.Version
                : $"{serverInfo.Name} {serverInfo.Version}";

            await foreach (McpClientTool tool in _client.EnumerateToolsAsync())
            {
                _tools.TryAdd(tool.Name, new McpTool(Name, tool, _context.Shell.Host));
            }
        }
        catch (Exception e)
        {
            _error = e;
            _tools.Clear();
            if (_client is { })
            {
                await _client.DisposeAsync();
                _client = null;
            }
        }
        finally
        {
            _context.ThrottleSemaphore.Release();
        }
    }

    internal void WaitForInit(CancellationToken cancellationToken = default)
    {
        _initTask.Wait(cancellationToken);
    }

    internal async Task WaitForInitAsync(CancellationToken cancellationToken = default)
    {
        await _initTask.WaitAsync(cancellationToken);
    }

    public void Dispose()
    {
        _tools.Clear();
        _client?.DisposeAsync().AsTask().Wait();
    }
}
