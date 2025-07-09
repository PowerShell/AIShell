using System.Diagnostics;
using System.Text;
using AIShell.Abstraction;

namespace AIShell.Kernel;

internal class Channel : IDisposable
{
    private const int MaxNamedPipeNameSize = 104;
    private const int ConnectionTimeout = 3000;

    private readonly string _aishPipeName;
    private readonly AishClientPipe _clientPipe;
    private readonly AishServerPipe _serverPipe;
    private readonly ManualResetEvent _connSetupWaitHandler;
    private readonly CancellableReadKey _readkeyProxy;
    private readonly Queue<PostQueryMessage> _queries;
    private readonly Shell _shell;

    private bool _setupSuccess;
    private bool _disposed;
    private Exception _exception;
    private Thread _serverThread;

    internal Channel(string pipeName, Shell shell)
    {
        ArgumentException.ThrowIfNullOrEmpty(pipeName);

        _shell = shell;
        _aishPipeName = new StringBuilder(MaxNamedPipeNameSize)
            .Append(Utils.AppName.Replace(' ', '_'))
            .Append('.')
            .Append(Environment.ProcessId)
            .Append('.')
            .Append(Utils.DefaultAppName)
            .ToString();

        _clientPipe = new AishClientPipe(pipeName);
        _serverPipe = new AishServerPipe(_aishPipeName);
        _serverPipe.OnPostQuery += OnPostQuery;
        _serverPipe.OnConnectedOrFailed += OnConnectedOrFailed;
        _connSetupWaitHandler = new ManualResetEvent(false);

        // Initialize the connection on a background thread.
        Task.Run(InitializeConnection);
        // We need to cancel the readline call when a query arrives from the command-line shell,
        // so we need to override the 'ReadKey' with a cancellable read-key method.
        _readkeyProxy = new CancellableReadKey();
        _queries = new Queue<PostQueryMessage>();
    }

    private async void InitializeConnection()
    {
        try
        {
            await _clientPipe.ConnectAsync(ConnectionTimeout, CancellationToken.None);
            _serverThread = new Thread(ServerThreadProc) { IsBackground = true, Name = "aish channel thread" };
            _serverThread.Start();
            _clientPipe.AskConnection(new AskConnectionMessage(_aishPipeName));
        }
        catch (Exception ex)
        {
            _setupSuccess = false;
            _exception = ex;
            _connSetupWaitHandler.Set();
        }
    }

    private async void ServerThreadProc()
    {
        await _serverPipe.StartProcessingAsync(ConnectionTimeout, CancellationToken.None);
    }

    private void ThrowIfNotConnected()
    {
        if (!Connected)
        {
            if (_setupSuccess)
            {
                throw new NotSupportedException($"Both the client and server pipes may have been closed. Pipe connection status: client({_clientPipe.Connected}), server({_serverPipe.Connected}).");
            }

            Debug.Assert(_exception is not null, "Error should be set when connection failed.");
            throw new NotSupportedException($"Bi-directional channel could not be established: {_exception.Message}", _exception);
        }
    }

    private void OnPostQuery(PostQueryMessage message)
    {
        lock (this)
        {
            _queries.Enqueue(message);
            _readkeyProxy.CancellationSource.Cancel();
        }
    }

    private void OnConnectedOrFailed(Exception exception)
    {
        if (exception is null)
        {
            _setupSuccess = true;
        }
        else if (_exception is null)
        {
            // _exception may already be set because '_clientPipe.AskConnection' failed.
            // We don't want to overwrite the true exception with a timeout exception in that case.
            _setupSuccess = false;
            _exception = exception;
        }

        _connSetupWaitHandler.Set();
    }

    internal bool TryDequeueQuery(out string query)
    {
        if (_queries.TryPeek(out PostQueryMessage message))
        {
            bool needSwitchingAgent = false;
            string agent = string.IsNullOrWhiteSpace(message.Agent) ? null : message.Agent.Trim();

            if (agent is not null)
            {
                // Wait for Shell to finish initialization.
                _shell.InitEventHandler.WaitOne();
                if (!agent.Equals(_shell.ActiveAgent?.Impl.Name, StringComparison.OrdinalIgnoreCase))
                {
                    needSwitchingAgent = true;
                }
            }

            if (needSwitchingAgent)
            {
                // Try switching the agent, and unset the 'Ágent' property for the message.
                query = $"/agent use {agent}";
                message.Agent = null;

                // When agent is specified, we send query only if the agent exists.
                bool agentExists = false;
                foreach (var a in _shell.Agents)
                {
                    if (agent.Equals(a.Impl.Name, StringComparison.OrdinalIgnoreCase))
                    {
                        agentExists = true;
                        break;
                    }
                }

                if (!agentExists)
                {
                    // The specified agent doesn't exist, so we will just let `/agent use` to fail
                    // and not going to send the actual query.
                    _queries.Dequeue();
                }
            }
            else
            {
                _queries.Dequeue();
                query = message.Context is null
                    ? message.Query
                    : $"{message.Query}\n\n# Context for the query:\n{message.Context}";
            }

            return true;
        }

        query = null;
        return false;
    }

    internal bool Connected => CheckConnection(blocking: true, out _);

    internal bool CheckConnection(bool blocking, out bool setupInProgress)
    {
        setupInProgress = false;
        // We have started the setup, now wait for it to finish.
        int timeout = blocking ? -1 : 0;
        bool signalled = _connSetupWaitHandler.WaitOne(timeout);

        if (signalled)
        {
            return _setupSuccess && _clientPipe.Connected && _serverPipe.Connected;
        }

        setupInProgress = true;
        return false;
    }

    internal CancellationToken ReadLineCancellationToken
    {
        get
        {
            _readkeyProxy.RefreshCancellationSourceIfNeeded();
            return _readkeyProxy.CancellationSource.Token;
        }
    }

    /// <summary>
    /// Post code blocks to the connected command-line shell.
    /// </summary>
    internal void PostCode(PostCodeMessage message)
    {
        ThrowIfNotConnected();
        _clientPipe.PostCode(message);
    }

    /// <summary>
    /// Ask context information from the shell.
    /// </summary>
    internal async Task<PostContextMessage> AskContext(AskContextMessage message, CancellationToken cancellationToken)
    {
        ThrowIfNotConnected();
        return await _clientPipe.AskContext(message, cancellationToken);
    }

    /// <summary>
    /// Run command in the connected shell.
    /// </summary>
    internal async Task<PostResultMessage> RunCommand(RunCommandMessage message, CancellationToken cancellationToken)
    {
        ThrowIfNotConnected();
        return await _clientPipe.RunCommand(message, cancellationToken);
    }

    /// <summary>
    /// Ask for the output of a command that was previously run in the connected shell.
    /// </summary>
    internal async Task<PostResultMessage> AskCommandOutput(AskCommandOutputMessage message, CancellationToken cancellationToken)
    {
        ThrowIfNotConnected();
        return await _clientPipe.AskCommandOutput(message, cancellationToken);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _clientPipe.Dispose();
        _serverPipe.Dispose();
        _serverPipe.OnPostQuery -= OnPostQuery;
        _serverPipe.OnConnectedOrFailed -= OnConnectedOrFailed;
        _connSetupWaitHandler.Dispose();
        _disposed = true;

        GC.SuppressFinalize(this);
    }
}
