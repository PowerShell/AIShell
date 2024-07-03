﻿/********************************************************************++
Copyright (c) Microsoft Corporation.  All rights reserved.
--********************************************************************/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.PowerShell.Internal;
using Microsoft.PowerShell.PSReadLine;

namespace Microsoft.PowerShell
{
    class ExitException : Exception { }

    public partial class PSConsoleReadLine
    {
        private const int ConsoleExiting = 1;

        // *must* be initialized in the static ctor
        // because the static member _clipboard depends upon it
        // for its own initialization
        private static readonly PSConsoleReadLine _singleton;

        // This is used by PowerShellEditorServices (the backend of the PowerShell VSCode extension)
        // so that it can call PSReadLine from a delegate and not hit nested pipeline issues.
        #pragma warning disable CS0649
        private static Action<CancellationToken> _handleIdleOverride;
        #pragma warning restore CS0649

        private bool _delayedOneTimeInitCompleted;

        private IConsole _console;
        private ICharMap _charMap;
        private Encoding _initialOutputEncoding;
        private bool _skipOutputEncodingChange;
        private Thread _readKeyThread;
        private AutoResetEvent _readKeyWaitHandle;
        private AutoResetEvent _keyReadWaitHandle;
        private CancellationToken _cancelReadCancellationToken;
        internal ManualResetEvent _closingWaitHandle;
        private WaitHandle[] _threadProcWaitHandles;
        private WaitHandle[] _requestKeyWaitHandles;

        private readonly StringBuilder _buffer;
        private readonly StringBuilder _statusBuffer;
        private bool _statusIsErrorMessage;
        private string _statusLinePrompt;
        private string _acceptedCommandLine;
        private List<EditItem> _edits;
        private int _editGroupStart;
        private int _undoEditIndex;
        private int _mark;
        private bool _inputAccepted;
        private readonly Queue<PSKeyInfo> _queuedKeys;
        private Stopwatch _lastRenderTime;
        private static readonly Stopwatch _readkeyStopwatch = new Stopwatch();

        // Save a fixed # of keys so we can reconstruct a repro after a crash
        private static readonly HistoryQueue<PSKeyInfo> _lastNKeys = new HistoryQueue<PSKeyInfo>(200);

        private void ReadOneOrMoreKeys()
        {
            _readkeyStopwatch.Restart();
            while (_console.KeyAvailable)
            {
                // _charMap is only guaranteed to accumulate input while KeyAvailable
                // returns false. Make sure to check KeyAvailable after every ProcessKey call,
                // and clear it in a loop in case the input was something like ^[[1 which can
                // be 3, 2, or part of 1 key depending on timing.
                _charMap.ProcessKey(_console.ReadKey());
                while (_charMap.KeyAvailable)
                {
                    var key = PSKeyInfo.FromConsoleKeyInfo(_charMap.ReadKey());
                    _lastNKeys.Enqueue(key);
                    _queuedKeys.Enqueue(key);
                }
                if (_readkeyStopwatch.ElapsedMilliseconds > 2)
                {
                    // Don't spend too long in this loop if there are lots of queued keys
                    break;
                }
            }

            if (_queuedKeys.Count == 0)
            {
                while (!_charMap.KeyAvailable)
                {
                    // Don't want to block when there is an escape sequence being read.
                    if (_charMap.InEscapeSequence)
                    {
                        if (_console.KeyAvailable)
                        {
                            _charMap.ProcessKey(_console.ReadKey());
                        }
                        else
                        {
                            // We don't want to sleep for the whole escape timeout
                            // or the user will have a laggy console, but there's
                            // nothing to block on at this point either, so do a
                            // small sleep to yield the CPU while we're waiting
                            // to decide what the input was. This will only run
                            // if there are no keys waiting to be read.
                            Thread.Sleep(5);
                        }
                    }
                    else
                    {
                        _charMap.ProcessKey(_console.ReadKey());
                    }
                }
                while (_charMap.KeyAvailable)
                {
                    var key = PSKeyInfo.FromConsoleKeyInfo(_charMap.ReadKey());
                    _lastNKeys.Enqueue(key);
                    _queuedKeys.Enqueue(key);
                }
            }
        }

        private void ReadKeyThreadProc()
        {
            while (true)
            {
                // Wait until ReadKey tells us to read a key (or it's time to exit).
                int handleId = WaitHandle.WaitAny(_threadProcWaitHandles);
                if (handleId == 1) // It was the _closingWaitHandle that was signaled.
                    break;

                ReadOneOrMoreKeys();

                // One or more keys were read - let ReadKey know we're done.
                _keyReadWaitHandle.Set();
            }
        }

        internal static PSKeyInfo ReadKey()
        {
            // Reading a key is handled on a different thread.  During process shutdown,
            // PowerShell will wait in it's ConsoleCtrlHandler until the pipeline has completed.
            // If we're running, we're most likely blocked waiting for user input.
            // This is a problem for two reasons.  First, exiting takes a long time (5 seconds
            // on Win8) because PowerShell is waiting forever, but the OS will forcibly terminate
            // the console.  Also - if there are any event handlers for the engine event
            // PowerShell.Exiting, those handlers won't get a chance to run.
            //
            // By waiting for a key on a different thread, our pipeline execution thread
            // (the thread ReadLine is called from) avoid being blocked in code that can't
            // be unblocked and instead blocks on events we control.

            // First, set an event so the thread to read a key actually attempts to read a key.
            _singleton._readKeyWaitHandle.Set();

            int handleId;

            while (true)
            {
                // Next, wait for one of three things:
                //   - a key is pressed
                //   - the console is exiting
                //   - 300ms timeout - to process events if we're idle
                handleId = WaitHandle.WaitAny(_singleton._requestKeyWaitHandles, 300);
                if (handleId != WaitHandle.WaitTimeout)
                {
                    break;
                }

                if (_handleIdleOverride is not null)
                {
                    _handleIdleOverride(_singleton._cancelReadCancellationToken);
                    continue;
                }

                // TODO: If we timed out, check for terminal resizing, and update the console setting as needed.
            }

            if (handleId == ConsoleExiting)
            {
                // The console is exiting - throw an exception to unwind the stack to the point
                // where we can return from ReadLine.
                if (_singleton.Options.HistorySaveStyle == HistorySaveStyle.SaveAtExit)
                {
                    _singleton.SaveHistoryAtExit();
                }
                _singleton._historyFileMutex.Dispose();

                throw new OperationCanceledException();
            }

            if (_singleton._cancelReadCancellationToken.IsCancellationRequested)
            {
                // ReadLine was cancelled. Dequeue the dummy input sent by the host, save the current
                // line to be restored next time ReadLine is called, clear the buffer and throw an
                // exception so we can return an empty string.
                _singleton._queuedKeys.Dequeue();
                _singleton.SaveCurrentLine();
                _singleton._getNextHistoryIndex = _singleton._history.Count;
                _singleton._current = 0;
                _singleton._buffer.Clear();
                _singleton.Render();
                throw new OperationCanceledException();
            }

            var key = _singleton._queuedKeys.Dequeue();
            return key;
        }

        private void PrependQueuedKeys(PSKeyInfo key)
        {
            if (_queuedKeys.Count > 0)
            {
                // This should almost never happen so being inefficient is fine.
                var list = new List<PSKeyInfo>(_queuedKeys);
                _queuedKeys.Clear();
                _queuedKeys.Enqueue(key);
                list.ForEach(k => _queuedKeys.Enqueue(k));
            }
            else
            {
                _queuedKeys.Enqueue(key);
            }
        }

        /// <summary>
        /// Entry point.
        /// </summary>
        /// <returns>The complete command line.</returns>
        public static string ReadLine()
        {
            return ReadLine(CancellationToken.None);
        }

        /// <summary>
        /// Entry point with cancellation.
        /// </summary>
        /// <returns>The complete command line.</returns>
        public static string ReadLine(CancellationToken cancellationToken)
        {
            var console = _singleton._console;

            if (Console.IsInputRedirected || Console.IsOutputRedirected)
            {
                // System.Console doesn't handle redirected input. It matches the behavior on Windows
                // by throwing an "InvalidOperationException".
                throw new NotSupportedException();
            }

            var oldControlCAsInput = false;
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                PlatformWindows.Init(ref _singleton._charMap);
            }
            else
            {
                try
                {
                    oldControlCAsInput = Console.TreatControlCAsInput;
                    Console.TreatControlCAsInput = true;
                }
                catch {}
            }

            bool firstTime = true;
            while (true)
            {
                try
                {
                    if (firstTime)
                    {
                        firstTime = false;
                        _singleton.Initialize(cancellationToken);

                        // Render prediction, if there is any, before user input.
                        if (_singleton._queuedKeys.Count is 0)
                        {
                            // Pass 'cancelWhenEmpty: true' to cancel the rendering if there's nothing to render.
                            // It's safe to cancel when empty in this case because we know for sure there is no
                            // existing rendering yet.
                            _singleton.ForceRender(cancelWhenEmpty: true);
                        }
                    }

                    return _singleton.InputLoop();
                }
                catch (OperationCanceledException)
                {
                    // Console is either exiting or the cancellation of ReadLine has been requested
                    // by a custom PSHost implementation.
                    return "";
                }
                catch (ExitException)
                {
                    return "exit";
                }
                catch (CustomHandlerException e)
                {
                    var oldColor = console.ForegroundColor;
                    console.ForegroundColor = ConsoleColor.Red;
                    console.WriteLine(
                        string.Format(CultureInfo.CurrentUICulture, PSReadLineResources.OopsCustomHandlerException, e.InnerException.Message));
                    console.ForegroundColor = oldColor;

                    var lineBeforeCrash = _singleton._buffer.ToString();
                    _singleton.Initialize(cancellationToken);
                    InvokePrompt();
                    Insert(lineBeforeCrash);
                }
                catch (Exception e)
                {
                    while (e.InnerException != null)
                    {
                        e = e.InnerException;
                    }
                    var oldColor = console.ForegroundColor;
                    console.ForegroundColor = ConsoleColor.Red;
                    console.WriteLine(PSReadLineResources.OopsAnErrorMessage1);
                    console.ForegroundColor = oldColor;
                    var sb = new StringBuilder();
                    for (int i = 0; i < _lastNKeys.Count; i++)
                    {
                        sb.Append(' ');
                        sb.Append(_lastNKeys[i].KeyStr);

                        if (_singleton._dispatchTable.TryGetValue(_lastNKeys[i], out var handler) &&
                            "AcceptLine".Equals(handler.BriefDescription, StringComparison.OrdinalIgnoreCase))
                        {
                            // Make it a little easier to see the keys
                            sb.Append('\n');
                        }
                    }

                    var ourVersion = typeof(PSConsoleReadLine).Assembly.GetCustomAttributes<AssemblyInformationalVersionAttribute>().First().InformationalVersion;
                    var osInfo = RuntimeInformation.OSDescription;
                    var bufferWidth = console.BufferWidth;
                    var bufferHeight = console.BufferHeight;

                    console.WriteLine(string.Format(CultureInfo.CurrentUICulture, PSReadLineResources.OopsAnErrorMessage2,
                        ourVersion, osInfo, bufferWidth, bufferHeight,
                        _lastNKeys.Count, sb, e));
                    var lineBeforeCrash = _singleton._buffer.ToString();
                    _singleton.Initialize(cancellationToken);
                    InvokePrompt();
                    Insert(lineBeforeCrash);
                }
                finally
                {
                    try
                    {
                        // If we are closing, restoring the old console settings isn't needed,
                        // and some operating systems, it can cause a hang.
                        if (!_singleton._closingWaitHandle.WaitOne(0))
                        {
                            console.OutputEncoding = _singleton._initialOutputEncoding;

                            bool IsValid(ConsoleColor color)
                            {
                                return color >= ConsoleColor.Black && color <= ConsoleColor.White;
                            }

                            if (IsValid(_singleton._initialForeground)) {
                                console.ForegroundColor = _singleton._initialForeground;
                            }
                            if (IsValid(_singleton._initialBackground)) {
                                console.BackgroundColor = _singleton._initialBackground;
                            }
                            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                            {
                                Console.TreatControlCAsInput = oldControlCAsInput;
                            }
                        }
                    }
                    catch { }

                    if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    {
                        PlatformWindows.Complete();
                    }
                }
            }
        }

        private string InputLoop()
        {
            while (true)
            {
                var killCommandCount = _killCommandCount;
                var yankCommandCount = _yankCommandCount;
                var tabCommandCount = _tabCommandCount;
                var searchHistoryCommandCount = _searchHistoryCommandCount;
                var recallHistoryCommandCount = _recallHistoryCommandCount;
                var anyHistoryCommandCount = _anyHistoryCommandCount;
                var visualSelectionCommandCount = _visualSelectionCommandCount;
                var moveToLineCommandCount = _moveToLineCommandCount;
                var moveToEndOfLineCommandCount = _moveToEndOfLineCommandCount;

                // We attempt to handle window resizing only once per a keybinding processing, because we assume the
                // window resizing cannot and shouldn't happen within the processing of a given keybinding.
                _handlePotentialResizing = true;

                var key = ReadKey();
                ProcessOneKey(key, _dispatchTable, ignoreIfNoAction: false, arg: null);
                if (_inputAccepted)
                {
                    _acceptedCommandLine = _buffer.ToString();
                    MaybeAddToHistory(_acceptedCommandLine, _edits, _undoEditIndex);

                    return _acceptedCommandLine;
                }

                if (killCommandCount == _killCommandCount)
                {
                    // Reset kill command count if it didn't change
                    _killCommandCount = 0;
                }
                if (yankCommandCount == _yankCommandCount)
                {
                    // Reset yank command count if it didn't change
                    _yankCommandCount = 0;
                }
                if (tabCommandCount == _tabCommandCount)
                {
                    // Reset tab command count if it didn't change
                    _tabCommandCount = 0;
                    _tabCompletions = null;
                }
                if (searchHistoryCommandCount == _searchHistoryCommandCount)
                {
                    if (_searchHistoryCommandCount > 0)
                    {
                        _emphasisStart = -1;
                        _emphasisLength = 0;
                        RenderWithPredictionQueryPaused();
                    }
                    _searchHistoryCommandCount = 0;
                    _searchHistoryPrefix = null;
                }
                if (recallHistoryCommandCount == _recallHistoryCommandCount)
                {
                    _recallHistoryCommandCount = 0;
                }
                if (anyHistoryCommandCount == _anyHistoryCommandCount)
                {
                    if (_anyHistoryCommandCount > 0)
                    {
                        ClearSavedCurrentLine();
                        _hashedHistory = null;
                        _currentHistoryIndex = _history.Count;
                    }
                    _anyHistoryCommandCount = 0;
                }
                if (visualSelectionCommandCount == _visualSelectionCommandCount && _visualSelectionCommandCount > 0)
                {
                    _visualSelectionCommandCount = 0;
                    RenderWithPredictionQueryPaused();  // Clears the visual selection
                }
                if (moveToLineCommandCount == _moveToLineCommandCount)
                {
                    _moveToLineCommandCount = 0;

                    if (InViCommandMode() && moveToEndOfLineCommandCount == _moveToEndOfLineCommandCount)
                    {
                        // the previous command was neither a "move to end of line" command
                        // nor a "move to line" command. In that case, the desired column
                        // number will be computed from the current position on the logical line.

                        _moveToEndOfLineCommandCount = 0;
                        _moveToLineDesiredColumn = -1;
                    }
                }
            }
        }

        T CallPossibleExternalApplication<T>(Func<T> func)
        {
            try
            {
                _console.OutputEncoding = _initialOutputEncoding;
                return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    ? PlatformWindows.CallPossibleExternalApplication(func)
                    : func();
            }
            finally
            {
                if (!_skipOutputEncodingChange)
                {
                    _console.OutputEncoding = Encoding.UTF8;
                }
            }
        }

        void ProcessOneKey(PSKeyInfo key, Dictionary<PSKeyInfo, KeyHandler> dispatchTable, bool ignoreIfNoAction, object arg)
        {
            var consoleKey = key.AsConsoleKeyInfo();

            // Our dispatch tables are built as much as possible in a portable way, so for example,
            // we avoid depending on scan codes like ConsoleKey.Oem6 and instead look at the
            // PSKeyInfo.Key. We also want to ignore the shift state as that may differ on
            // different keyboard layouts.
            //
            // That said, we first look up exactly what we get from Console.ReadKey - that will fail
            // most of the time, and when it does, we normalize the key.
            if (!dispatchTable.TryGetValue(key, out var handler))
            {
                // If we see a control character where Ctrl wasn't used but shift was, treat that like
                // shift hadn't be pressed.  This cleanly allows Shift+Backspace without adding a key binding.
                if (key.Shift && !key.Control && !key.Alt)
                {
                    var c = consoleKey.KeyChar;
                    if (c != '\0' && char.IsControl(c))
                    {
                        key = PSKeyInfo.From(consoleKey.Key);
                        dispatchTable.TryGetValue(key, out handler);
                    }
                }
            }

            if (handler != null)
            {
                handler.Action(consoleKey, arg);
            }
            else if (!ignoreIfNoAction)
            {
                SelfInsert(consoleKey, arg);
            }
        }

        static PSConsoleReadLine()
        {
            _singleton = new PSConsoleReadLine();
            _viRegister = new ViRegister(_singleton);
        }

        private PSConsoleReadLine()
        {
            _console = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                ? PlatformWindows.OneTimeInit(this)
                : new VirtualTerminal();
            _charMap = new DotNetCharMap();

            _buffer = new StringBuilder(8 * 1024);
            _statusBuffer = new StringBuilder(256);
            _savedCurrentLine = new HistoryItem();
            _queuedKeys = new Queue<PSKeyInfo>();

            bool usingLegacyConsole = _console is PlatformWindows.LegacyWin32Console;
            _options = new PSConsoleReadLineOptions(DefaultName, usingLegacyConsole);
            _prediction = new Prediction(this);
            SetDefaultBindings(_options.EditMode);
        }

        private void Initialize(CancellationToken cancellationToken)
        {
            if (!_delayedOneTimeInitCompleted)
            {
                DelayedOneTimeInitialize();
                _delayedOneTimeInitCompleted = true;
            }

            _buffer.Clear();
            _edits = new List<EditItem>();
            _undoEditIndex = 0;
            _editGroupStart = -1;
            _current = 0;
            _mark = 0;
            _emphasisStart = -1;
            _emphasisLength = 0;
            _inputAccepted = false;
            _initialX = _console.CursorLeft;
            _initialY = _console.CursorTop;
            _initialForeground = _console.ForegroundColor;
            _initialBackground = _console.BackgroundColor;
            _previousRender = _initialPrevRender;
            _previousRender.UpdateConsoleInfo(_console);
            _previousRender.initialY = _initialY;
            _statusIsErrorMessage = false;
            _cancelReadCancellationToken = cancellationToken;

            _initialOutputEncoding = _console.OutputEncoding;
            _prediction.Reset();

            // Don't change the OutputEncoding if already UTF8, no console, or using raster font on Windows
            _skipOutputEncodingChange = _initialOutputEncoding == Encoding.UTF8
                || (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
                    && PlatformWindows.IsConsoleInput()
                    && PlatformWindows.IsUsingRasterFont());

            if (!_skipOutputEncodingChange) {
                _console.OutputEncoding = Encoding.UTF8;
            }

            _lastRenderTime = Stopwatch.StartNew();

            _killCommandCount = 0;
            _yankCommandCount = 0;
            _tabCommandCount = 0;
            _recallHistoryCommandCount = 0;
            _anyHistoryCommandCount = 0;
            _visualSelectionCommandCount = 0;
            _hashedHistory = null;

            if (_getNextHistoryIndex > 0)
            {
                _currentHistoryIndex = _getNextHistoryIndex;
                UpdateFromHistory(HistoryMoveCursor.ToEnd);
                _getNextHistoryIndex = 0;
                if (_searchHistoryCommandCount > 0)
                {
                    _searchHistoryPrefix = "";
                    if (Options.HistoryNoDuplicates)
                    {
                        _hashedHistory = new Dictionary<string, int>();
                    }
                }
            }
            else
            {
                _currentHistoryIndex = _history.Count;
                _searchHistoryCommandCount = 0;
            }
            if (_previousHistoryItem != null)
            {
                _previousHistoryItem.ApproximateElapsedTime = DateTime.UtcNow - _previousHistoryItem.StartTime;
            }
        }

        private void DelayedOneTimeInitialize()
        {
            // Delayed initialization is needed so that options can be set
            // after the constructor but have an affect before the user starts
            // editing their first command line.  For example, if the user
            // specifies a custom history save file, we don't want to try reading
            // from the default one.

            if (_options.MaximumHistoryCount == 0)
            {
                // Initialize 'MaximumHistoryCount' if it's not defined in user's profile.
                _options.MaximumHistoryCount = PSConsoleReadLineOptions.DefaultMaximumHistoryCount;
            }

            _historyFileMutex = new Mutex(false, GetHistorySaveFileMutexName());

            _history = new HistoryQueue<HistoryItem>(Options.MaximumHistoryCount);
            _recentHistory = new HistoryQueue<string>(capacity: 5);
            _currentHistoryIndex = 0;

            ReadHistoryFile();

            _killIndex = -1; // So first add indexes 0.
            _killRing = new List<string>(Options.MaximumKillRingCount);

            _readKeyWaitHandle = new AutoResetEvent(false);
            _keyReadWaitHandle = new AutoResetEvent(false);
            _closingWaitHandle = new ManualResetEvent(false);
            _requestKeyWaitHandles = new WaitHandle[] {_keyReadWaitHandle, _closingWaitHandle};
            _threadProcWaitHandles = new WaitHandle[] {_readKeyWaitHandle, _closingWaitHandle};

            // This is for a "being hosted in an alternate appdomain scenario" (the
            // DomainUnload event is not raised for the default appdomain). It allows us
            // to exit cleanly when the appdomain is unloaded but the process is not going
            // away.
            if (!AppDomain.CurrentDomain.IsDefaultAppDomain())
            {
                AppDomain.CurrentDomain.DomainUnload += (x, y) =>
                {
                    _closingWaitHandle.Set();
                    _readKeyThread.Join(); // may need to wait for history to be written
                };
            }

            _readKeyThread = new Thread(ReadKeyThreadProc) {IsBackground = true, Name = "PSReadLine ReadKey Thread"};
            _readKeyThread.Start();
        }

        private static void Chord(ConsoleKeyInfo? key = null, object arg = null)
        {
            if (!key.HasValue)
            {
                throw new ArgumentNullException(nameof(key));
            }

            if (_singleton._chordDispatchTable.TryGetValue(PSKeyInfo.FromConsoleKeyInfo(key.Value), out var secondKeyDispatchTable))
            {
                var secondKey = ReadKey();
                _singleton.ProcessOneKey(secondKey, secondKeyDispatchTable, ignoreIfNoAction: true, arg: arg);
            }
        }

        /// <summary>
        /// Abort current action, e.g. incremental history search.
        /// </summary>
        public static void Abort(ConsoleKeyInfo? key = null, object arg = null)
        {
        }

        /// <summary>
        /// Start a new digit argument to pass to other functions.
        /// </summary>
        public static void DigitArgument(ConsoleKeyInfo? key = null, object arg = null)
        {
            if (!key.HasValue || char.IsControl(key.Value.KeyChar))
            {
                Ding();
                return;
            }

            if (_singleton._options.EditMode == EditMode.Vi && key.Value.KeyChar == '0')
            {
                BeginningOfLine();
                return;
            }

            bool sawDigit = false;
            _singleton._statusLinePrompt = "digit-argument: ";
            var argBuffer = _singleton._statusBuffer;
            argBuffer.Append(key.Value.KeyChar);
            if (key.Value.KeyChar == '-')
            {
                argBuffer.Append('1');
            }
            else
            {
                sawDigit = true;
            }

            _singleton.RenderWithPredictionQueryPaused(); // Render prompt
            while (true)
            {
                var nextKey = ReadKey();
                if (_singleton._dispatchTable.TryGetValue(nextKey, out var handler))
                {
                    if (handler.Action == DigitArgument)
                    {
                        if (nextKey.KeyChar == '-')
                        {
                            if (argBuffer[0] == '-')
                            {
                                argBuffer.Remove(0, 1);
                            }
                            else
                            {
                                argBuffer.Insert(0, '-');
                            }
                            _singleton.RenderWithPredictionQueryPaused(); // Render prompt
                            continue;
                        }

                        if (nextKey.KeyChar >= '0' && nextKey.KeyChar <= '9')
                        {
                            if (!sawDigit && argBuffer.Length > 0)
                            {
                                // Buffer is either '-1' or '1' from one or more Alt+- keys
                                // but no digits yet.  Remove the '1'.
                                argBuffer.Length -= 1;
                            }
                            sawDigit = true;
                            argBuffer.Append(nextKey.KeyChar);
                            _singleton.RenderWithPredictionQueryPaused(); // Render prompt
                            continue;
                        }
                    }
                    else if (handler.Action == Abort ||
                             handler.Action == CancelLine ||
                             handler.Action == CopyOrCancelLine)
                    {
                        break;
                    }
                }

                if (int.TryParse(argBuffer.ToString(), out var intArg))
                {
                    _singleton.ProcessOneKey(nextKey, _singleton._dispatchTable, ignoreIfNoAction: false, arg: intArg);
                }
                else
                {
                    Ding();
                }
                break;
            }

            // Remove our status line
            argBuffer.Clear();
            _singleton.ClearStatusMessage(render: true);
        }


        /// <summary>
        /// Erases the current prompt and calls the prompt function to redisplay
        /// the prompt.  Useful for custom key handlers that change state, e.g.
        /// change the current directory.
        /// </summary>
        public static void InvokePrompt(ConsoleKeyInfo? key = null, object arg = null)
        {
            var console = _singleton._console;
            console.CursorVisible = false;

            if (arg is int newY)
            {
                console.SetCursorPosition(0, newY);
            }
            else
            {
                newY = _singleton._initialY - _singleton._options.ExtraPromptLineCount;

                console.SetCursorPosition(0, newY);

                // We need to rewrite the prompt, so blank out everything from a previous prompt invocation
                // in case the next one is shorter.
                var spaces = Spaces(console.BufferWidth);
                for (int i = 0; i < _singleton._options.ExtraPromptLineCount + 1; i++)
                {
                    console.Write(spaces);
                }

                console.SetCursorPosition(0, newY);
            }

            string newPrompt = GetPrompt();

            console.Write(newPrompt);
            _singleton._initialX = console.CursorLeft;
            _singleton._initialY = console.CursorTop;
            _singleton._previousRender = _initialPrevRender;
            _singleton._previousRender.UpdateConsoleInfo(console);
            _singleton._previousRender.initialY = _singleton._initialY;

            _singleton.Render();
            console.CursorVisible = true;
        }

        private static string GetPrompt()
        {
            // TODO: more customized prompt, like including the current filesystem location?
            return "input > ";
        }
    }
}
