using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Text;
using System.Management.Automation;
using System.Management.Automation.Host;
using System.Management.Automation.Runspaces;
using AIShell.Abstraction;
using Microsoft.PowerShell.Commands;
using System.Text.Json;

namespace AIShell.Integration;

public class Channel : IDisposable
{
    private const int MaxNamedPipeNameSize = 104;
    private const int ConnectionTimeout = 7000;

    private readonly string _shellPipeName;
    private readonly Type _psrlType;
    private readonly Runspace _runspace;
    private readonly EngineIntrinsics _intrinsics;
    private readonly MethodInfo _psrlInsert, _psrlRevertLine, _psrlAcceptLine;
    private readonly FieldInfo _psrlHandleResizing, _psrlReadLineReady;
    private readonly object _psrlSingleton;
    private readonly ManualResetEvent _connSetupWaitHandler;
    private readonly Predictor _predictor;
    private readonly ScriptBlock _onIdleAction;
    private readonly List<HistoryInfo> _commandHistory;

    private PathInfo _currentLocation;
    private ShellClientPipe _clientPipe;
    private ShellServerPipe _serverPipe;
    private bool? _setupSuccess;
    private Exception _exception;
    private Thread _serverThread;
    private CodePostData _pendingPostCodeData;

    private Channel(Runspace runspace, EngineIntrinsics intrinsics, Type psConsoleReadLineType)
    {
        ArgumentNullException.ThrowIfNull(runspace);
        ArgumentNullException.ThrowIfNull(psConsoleReadLineType);

        _runspace = runspace;
        _intrinsics = intrinsics;
        _psrlType = psConsoleReadLineType;
        _connSetupWaitHandler = new ManualResetEvent(false);
        _currentLocation = _intrinsics.SessionState.Path.CurrentLocation;
        _runspace.AvailabilityChanged += RunspaceAvailableAction;
        _intrinsics.InvokeCommand.LocationChangedAction += LocationChangedAction;

        _shellPipeName = new StringBuilder(MaxNamedPipeNameSize)
            .Append("pwsh_aish.")
            .Append(Environment.ProcessId)
            .Append('.')
            .Append(Path.GetFileNameWithoutExtension(Environment.ProcessPath))
            .ToString();

        BindingFlags methodFlags = BindingFlags.Static | BindingFlags.Public;
        _psrlInsert = _psrlType.GetMethod("Insert", methodFlags, [typeof(string)]);
        _psrlRevertLine = _psrlType.GetMethod("RevertLine", methodFlags);
        _psrlAcceptLine = _psrlType.GetMethod("AcceptLine", methodFlags);

        FieldInfo singletonInfo = _psrlType.GetField("_singleton", BindingFlags.Static | BindingFlags.NonPublic);
        _psrlSingleton = singletonInfo.GetValue(null);

        BindingFlags fieldFlags = BindingFlags.Instance | BindingFlags.NonPublic;
        _psrlReadLineReady = _psrlType.GetField("_readLineReady", fieldFlags);
        _psrlHandleResizing = _psrlType.GetField("_handlePotentialResizing", fieldFlags);

        _commandHistory = [];
        _predictor = new Predictor();
        _onIdleAction = ScriptBlock.Create("[AIShell.Integration.Channel]::Singleton.OnIdleHandler()");
    }

    public static Channel CreateSingleton(Runspace runspace, EngineIntrinsics intrinsics, Type psConsoleReadLineType)
    {
        return Singleton ??= new Channel(runspace, intrinsics, psConsoleReadLineType);
    }

    public static Channel Singleton { get; private set; }

    internal string PSVersion => _runspace.Version.ToString();

    internal bool Connected => CheckConnection(blocking: true, out _);

    internal bool CheckConnection(bool blocking, out bool setupInProgress)
    {
        setupInProgress = false;
        if (_serverPipe is null)
        {
            // We haven't setup the channel yet.
            return false;
        }

        // We have started the setup, now wait for it to finish.
        int timeout = blocking ? -1 : 0;
        bool signalled = _connSetupWaitHandler.WaitOne(timeout);

        if (signalled)
        {
            return _setupSuccess.Value && _serverPipe.Connected && _clientPipe.Connected;
        }

        setupInProgress = true;
        return false;
    }

    public string StartChannelSetup()
    {
        if (_serverPipe is not null)
        {
            if (Connected)
            {
                throw new InvalidOperationException("A connected channel already exists.");
            }

            // Channel is not in the connected state, so we can refresh everything and try setting it up again.
            Reset();
        }

        _serverPipe = new ShellServerPipe(_shellPipeName);
        _serverPipe.OnAskConnection += OnAskConnection;
        _serverPipe.OnAskContext += OnAskContext;
        _serverPipe.OnPostCode += OnPostCode;

        _serverThread = new Thread(ThreadProc)
            {
                IsBackground = true,
                Name = "pwsh channel thread"
            };

        _serverThread.Start();
        return _shellPipeName;
    }

    private async void ThreadProc()
    {
        await _serverPipe.StartProcessingAsync(ConnectionTimeout, CancellationToken.None);
    }

    private void LocationChangedAction(object sender, LocationChangedEventArgs e)
    {
        _currentLocation = e.NewPath;
    }

    private void RunspaceAvailableAction(object sender, RunspaceAvailabilityEventArgs e)
    {
        if (sender is null || e.RunspaceAvailability is not RunspaceAvailability.Available)
        {
            return;
        }

        // It's safe to get states of the PowerShell Runspace now because it's available and this event
        // is handled synchronously.
        // We may want to invoke command or script here, and we have to unregister ourself before doing
        // that, because the invocation would change the availability of the Runspace, which will cause
        // the 'AvailabilityChanged' to be fired again and re-enter our handler.
        // We register ourself back after we are done with the processing.
        var pwshRunspace = (Runspace)sender;
        pwshRunspace.AvailabilityChanged -= RunspaceAvailableAction;

        try
        {
            using var ps = PowerShell.Create();
            ps.Runspace = pwshRunspace;

            var results = ps
                .AddCommand("Get-History")
                .AddParameter("Count", 5)
                .InvokeAndCleanup<HistoryInfo>();

            if (results.Count is 0 ||
                (_commandHistory.Count > 0 && _commandHistory[^1].Id == results[^1].Id))
            {
                // No command history yet, or no change since the last update.
                return;
            }

            lock (_commandHistory)
            {
                _commandHistory.Clear();
                _commandHistory.AddRange(results);
            }
        }
        finally
        {
            pwshRunspace.AvailabilityChanged += RunspaceAvailableAction;
        }
    }

    private string CaptureScreen()
    {
        if (!OperatingSystem.IsWindows())
        {
            return null;
        }

        try
        {
            PSHostRawUserInterface rawUI = _intrinsics.Host.UI.RawUI;
            Coordinates start = new(0, 0), end = rawUI.CursorPosition;
            end.X = rawUI.BufferSize.Width - 1;

            BufferCell[,] content = rawUI.GetBufferContents(new Rectangle(start, end));
            StringBuilder line = new(), buffer = new();

            int rows = content.GetLength(0);
            int columns = content.GetLength(1);

            for (int row = 0; row < rows; row++)
            {
                line.Clear();
                for (int column = 0; column < columns; column++)
                {
                    line.Append(content[row, column].Character);
                }

                line.TrimEnd();
                buffer.Append(line).Append('\n');
            }

            return buffer.Length is 0 ? string.Empty : buffer.ToString();
        }
        catch
        {
            return null;
        }
    }

    internal void PostQuery(PostQueryMessage message)
    {
        ThrowIfNotConnected();
        _clientPipe.PostQuery(message);
    }

    public void Dispose()
    {
        Reset();
        _connSetupWaitHandler.Dispose();
        _predictor.Unregister();
        _runspace.AvailabilityChanged -= RunspaceAvailableAction;
        _intrinsics.InvokeCommand.LocationChangedAction -= LocationChangedAction;
        GC.SuppressFinalize(this);
    }

    private void Reset()
    {
        _serverPipe?.Dispose();
        _clientPipe?.Dispose();
        _connSetupWaitHandler.Reset();

        if (_serverPipe is not null)
        {
            _serverPipe.OnAskConnection -= OnAskConnection;
            _serverPipe.OnAskContext -= OnAskContext;
            _serverPipe.OnPostCode -= OnPostCode;
        }

        _serverPipe = null;
        _clientPipe = null;
        _exception = null;
        _serverThread = null;
        _setupSuccess = null;
    }

    private void ThrowIfNotConnected()
    {
        if (!Connected)
        {
            if (_setupSuccess is null)
            {
                throw new NotSupportedException("Channel has not been setup yet.");
            }

            if (_setupSuccess.Value)
            {
                throw new NotSupportedException($"Both the client and server pipes may have been closed. Pipe connection status: client({_clientPipe.Connected}), server({_serverPipe.Connected}).");
            }

            Debug.Assert(_exception is not null, "Error should be set when connection failed.");
            throw new NotSupportedException($"Bi-directional channel could not be established: {_exception.Message}", _exception);
        }
    }

    [Hidden()]
    public void OnIdleHandler()
    {
        if (_pendingPostCodeData is not null)
        {
            PSRLInsert(_pendingPostCodeData.CodeToInsert);
            _predictor.SetCandidates(_pendingPostCodeData.PredictionCandidates);
            _pendingPostCodeData = null;
        }
    }

    private void OnPostCode(PostCodeMessage postCodeMessage)
    {
        // Ignore 'code post' request when a posting operation is on-going.
        // This most likely would happen when user run 'code post' mutliple times to post the same code, which is safe to ignore.
        if (_pendingPostCodeData is not null || postCodeMessage.CodeBlocks.Count is 0)
        {
            return;
        }

        string codeToInsert;
        List<string> codeBlocks = postCodeMessage.CodeBlocks;
        List<PredictionCandidate> predictionCandidates = null;

        if (codeBlocks.Count is 1)
        {
            codeToInsert = codeBlocks[0];
        }
        else if (Predictor.TryProcessForPrediction(codeBlocks, out predictionCandidates))
        {
            codeToInsert = predictionCandidates[0].Code;
        }
        else
        {
            // Use LF as line ending to be consistent with the response from LLM.
            StringBuilder sb = new(capacity: 50);
            for (int i = 0; i < codeBlocks.Count; i++)
            {
                if (i > 0)
                {
                    sb.Append('\n');
                }

                sb.Append(codeBlocks[i]).Append('\n');
            }

            codeToInsert = sb.ToString();
        }

        // When PSReadLine is actively running, its '_readLineReady' field should be set to 'true'.
        // When the value is 'false', it means PowerShell is still busy running scripts or commands.
        if (_psrlReadLineReady.GetValue(_psrlSingleton) is true)
        {
            PSRLRevertLine();
            PSRLInsert(codeToInsert);
            _predictor.SetCandidates(predictionCandidates);
        }
        else
        {
            _pendingPostCodeData = new CodePostData(codeToInsert, predictionCandidates);
            // We use script block handler instead of a delegate handler because the latter will run
            // in a background thread, while the former will run in the pipeline thread, which is way
            // more predictable.
            _runspace.Events.SubscribeEvent(
                source: null,
                eventName: null,
                sourceIdentifier: PSEngineEvent.OnIdle,
                data: null,
                action: _onIdleAction,
                supportEvent: true,
                forwardEvent: false,
                maxTriggerCount: 1);
        }
    }

    private PostContextMessage OnAskContext(AskContextMessage askContextMessage)
    {
        const string RedactedValue = "***<sensitive data redacted>***";

        ContextType type = askContextMessage.ContextType;
        string[] arguments = askContextMessage.Arguments;

        string contextInfo;
        switch (type)
        {
            case ContextType.CurrentLocation:
                contextInfo = JsonSerializer.Serialize(
                    new { Provider = _currentLocation.Provider.Name, _currentLocation.Path });
                break;

            case ContextType.CommandHistory:
                lock (_commandHistory)
                {
                    contextInfo = JsonSerializer.Serialize(
                        _commandHistory.Select(o => new { o.Id, o.CommandLine }));
                }
                break;

            case ContextType.TerminalContent:
                contextInfo = CaptureScreen();
                break;

            case ContextType.EnvironmentVariables:
                if (arguments is { Length: > 0 })
                {
                    var varsCopy = new Dictionary<string, string>();
                    foreach (string name in arguments)
                    {
                        if (!string.IsNullOrEmpty(name))
                        {
                            varsCopy.Add(name, Environment.GetEnvironmentVariable(name) is string value
                                ? EnvVarMayBeSensitive(name) ? RedactedValue : value
                                : $"[env variable '{arguments}' is undefined]");
                        }
                    }

                    contextInfo = varsCopy.Count > 0
                        ? JsonSerializer.Serialize(varsCopy)
                        : "The specified environment variable names are invalid";
                }
                else
                {
                    var vars = Environment.GetEnvironmentVariables();
                    var varsCopy = new Dictionary<string, string>();

                    foreach (string key in vars.Keys)
                    {
                        varsCopy.Add(key, EnvVarMayBeSensitive(key) ? RedactedValue : (string)vars[key]);
                    }

                    contextInfo = JsonSerializer.Serialize(varsCopy);
                }
                break;

            default:
                throw new InvalidDataException($"Unknown context type '{type}'");
        }

        return new PostContextMessage(contextInfo);

        static bool EnvVarMayBeSensitive(string key)
        {
            return key.Contains("key", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("token", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("pass", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("secret", StringComparison.OrdinalIgnoreCase);
        }
    }

    private void OnAskConnection(ShellClientPipe clientPipe, Exception exception)
    {
        if (clientPipe is not null)
        {
            _clientPipe = clientPipe;
            _setupSuccess = true;
        }
        else
        {
            _setupSuccess = false;
            _exception = exception;
        }

        _connSetupWaitHandler.Set();
    }

    private void PSRLInsert(string text)
    {
        using var _ = new NoWindowResizingCheck();
        _psrlInsert.Invoke(null, [text]);
    }

    private void PSRLRevertLine()
    {
        using var _ = new NoWindowResizingCheck();
        _psrlRevertLine.Invoke(null, [null, null]);
    }

    private void PSRLAcceptLine()
    {
        using var _ = new NoWindowResizingCheck();
        _psrlAcceptLine.Invoke(null, [null, null]);
    }

    /// <summary>
    /// We assume the terminal window will not resize during the code-post operation and hence disable the window resizing check on macOS.
    /// This is to avoid reading console cursor positions while PSReadLine is already blocked on 'Console.ReadKey', because on Unix system,
    /// when we are already blocked on key input, reading cursor position on another thread will be blocked too until a key is pressed.
    ///
    /// We do need window resizing check on Windows due to how 'Start-AIShell' works differently:
    ///  - On Windows, 'Start-AIShell' returns way BEFORE the current tab gets splitted for the sidecar pane, and PowerShell has already
    ///    called into PSReadLine when the splitting actually happens. So, it's literally a window resizing for PSReadLine at that point
    ///    and hence we need the window resizing check to correct the initial coordinates ('_initialX' and '_initialY').
    ///  - On macOS, however, 'Start-AIShell' returns AFTER the current tab gets splitted for the sidecar pane. So, window resizing will
    ///    be done before PowerShell calls into PSReadLine and hence there is no need for window resizing check on macOS.
    /// Also, On Windows we can read cursor position without blocking even if another thread is blocked on calling 'ReadKey'.
    /// </summary>
    private class NoWindowResizingCheck : IDisposable
    {
        private readonly object _originalValue;

        internal NoWindowResizingCheck()
        {
            if (OperatingSystem.IsMacOS())
            {
                Channel channel = Singleton;
                _originalValue = channel._psrlHandleResizing.GetValue(channel._psrlSingleton);
                channel._psrlHandleResizing.SetValue(channel._psrlSingleton, false);
            }
        }

        public void Dispose()
        {
            if (OperatingSystem.IsMacOS())
            {
                Channel channel = Singleton;
                channel._psrlHandleResizing.SetValue(channel._psrlSingleton, _originalValue);
            }
        }
    }
}

internal record CodePostData(string CodeToInsert, List<PredictionCandidate> PredictionCandidates);

internal static class ExtensionMethods
{
    internal static Collection<T> InvokeAndCleanup<T>(this PowerShell ps)
    {
        var results = ps.Invoke<T>();
        ps.Commands.Clear();

        return results;
    }

    internal static void InvokeAndCleanup(this PowerShell ps)
    {
        ps.Invoke();
        ps.Commands.Clear();
    }

    internal static void TrimEnd(this StringBuilder sb)
    {
        // end will point to the first non-trimmed character on the right.
        int end = sb.Length - 1;
        for (; end >= 0; end--)
        {
            if (!char.IsWhiteSpace(sb[end]))
            {
                break;
            }
        }

        int index = end + 1;
        if (index < sb.Length)
        {
            sb.Remove(index, sb.Length - index);
        }
    }
}
