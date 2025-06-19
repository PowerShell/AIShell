using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using AIShell.Abstraction;
using Spectre.Console;

namespace AIShell.Kernel;

internal sealed class DummyStreamRender : IStreamRender
{
    private readonly StringBuilder _buffer;
    private readonly CancellationToken _cancellationToken;

    internal DummyStreamRender(CancellationToken token)
    {
        _buffer = new StringBuilder();
        _cancellationToken = token;
    }

    public string AccumulatedContent => _buffer.ToString();

    public List<CodeBlock> CodeBlocks => Utils.ExtractCodeBlocks(_buffer.ToString(), out _);

    public void Refresh(string newChunk)
    {
        // Stop rendering up on cancellation.
        _cancellationToken.ThrowIfCancellationRequested();

        Console.Write(newChunk);
        _buffer.Append(newChunk);
    }

    public void Dispose() { }
}

internal sealed partial class FancyStreamRender : IStreamRender
{
    internal const char ESC = '\x1b';
    internal static readonly Regex AnsiRegex = CreateAnsiRegex();

    private static int s_consoleUpdateFlag = 0;

    private readonly int _bufferWidth, _bufferHeight;
    private readonly MarkdownRender _markdownRender;
    private readonly StringBuilder _buffer;
    private readonly CancellationToken _cancellationToken;

    private int _localFlag;
    private Point _initialCursor;
    private string _currentText;
    private string _accumulatedContent;
    private List<string> _previousContents;

    internal FancyStreamRender(MarkdownRender markdownRender, CancellationToken token)
    {
        _bufferWidth = Console.BufferWidth;
        _bufferHeight = Console.BufferHeight;
        _markdownRender = markdownRender;
        _buffer = new StringBuilder();
        _cancellationToken = token;

        _localFlag = s_consoleUpdateFlag;
        _initialCursor = new(Console.CursorLeft, Console.CursorTop);
        _accumulatedContent = _currentText = string.Empty;
        _previousContents = null;

        // Hide the cursor when rendering the streaming response.
        Console.CursorVisible = false;
    }

    public string AccumulatedContent
    {
        get
        {
            if (_previousContents is null)
            {
                return _accumulatedContent;
            }

            _previousContents.Add(_accumulatedContent);
            return string.Concat(_previousContents);
        }
    }

    public List<CodeBlock> CodeBlocks
    {
        get
        {
            // Create a new list to return, so as to prevent agents from changing
            // the list that is used internally by 'CodeBlockVisitor'.
            var blocks = _markdownRender.GetAllCodeBlocks();
            return blocks is null ? null : [.. blocks];
        }
    }

    public void Dispose()
    {
        if (!string.IsNullOrEmpty(_accumulatedContent))
        {
            // Write a new line if we did render something out to the console.
            Console.WriteLine();
        }

        // Show the cursor after the rendering is done.
        Console.CursorVisible = true;
    }

    public void Refresh(string newChunk)
    {
        // Avoid rendering the new chunk up on cancellation.
        _cancellationToken.ThrowIfCancellationRequested();

        // The host wrote out something while this stream render is active.
        // We need to reset the state of this stream render in this case.
        if (_localFlag < s_consoleUpdateFlag)
        {
            _localFlag = s_consoleUpdateFlag;
            _initialCursor = new(Console.CursorLeft, Console.CursorTop);
            Console.CursorVisible = false;

            if (_buffer.Length > 0)
            {
                (_previousContents ??= []).Add(_accumulatedContent);
                _accumulatedContent = _currentText = string.Empty;
                _buffer.Clear();
            }
        }

        _buffer.Append(newChunk);
        _accumulatedContent = _buffer.ToString();
        RefreshImpl(_markdownRender.RenderText(_accumulatedContent));

        // If the rendering is in progress, don't let the cancellation interrupt the rendering.
        // But we throw the exception when the current rendering is done.
        _cancellationToken.ThrowIfCancellationRequested();
    }

    private void RefreshImpl(string newText)
    {
        if (string.Equals(newText, _currentText, StringComparison.Ordinal))
        {
            return;
        }

        int newTextStartIndex = 0;
        var cursorStart = _initialCursor;
        bool redoWholeLine = false;

        if (!string.IsNullOrEmpty(_currentText))
        {
            int index = SameUpTo(newText, out redoWholeLine);
            newTextStartIndex = index + 1;

            // When the new text start exactly with the current text, we just continue to write.
            // No need to move the cursor in that case.
            bool moveCursor = index < _currentText.Length - 1;
            if (moveCursor && index >= 0)
            {
                // When 'index == -1', we just move the cursor to the initial position.
                // Otherwise, calculate the cursor position for the next write.
                string oldPlainText = GetPlainText(_currentText[..newTextStartIndex]);
                cursorStart = ConvertOffsetToPoint(cursorStart, oldPlainText, oldPlainText.Length);

                if (cursorStart.Y < 0)
                {
                    // This can only happen when we are streaming out a large table that spans over a buffer window.
                    // Width of the columns for the table may change when new content arrives, so we sometimes need
                    // to rewrite the whole table. When the beginning of the table already scrolls up off the window,
                    // we will reach here in the code. In this case, we just pretend to write out rows of the new table
                    // until we are at the first line of the terminal, and then we start to really write out the rest
                    // of the new table.
                    //
                    // This is a simple implementation with the assumption that a row of the table always fits in a
                    // single line on the terminal and always ends with a LF. It should be the case for our markdown
                    // table VT render.
                    int y = cursorStart.Y;
                    for (int j = newTextStartIndex; j < newText.Length; j++)
                    {
                        if (newText[j] is '\n')
                        {
                            y += 1;
                            if (y is 0)
                            {
                                newTextStartIndex = j + 1;
                                break;
                            }
                        }
                    }

                    cursorStart = new Point(0, 0);
                }
            }

            if (moveCursor)
            {
                Console.SetCursorPosition(cursorStart.X, cursorStart.Y);
                // erase from that cursor position (inclusive) to the end of the display
                Console.Write("\x1b[0J");
            }
            else
            {
                cursorStart = new(Console.CursorLeft, Console.CursorTop);
            }
        }

        Console.Out.Write(newText.AsSpan(newTextStartIndex));

        // Update the streaming render
        int topMax = _bufferHeight - 1;
        if (Console.CursorTop == topMax)
        {
            // If the current cursor top is less than top-max, then there was no scrolling-up and the
            // initial cursor position was not changed.
            // But if it's equal to top-max, then the terminal buffer may have scrolled, and in that
            // case we need to re-calculate and update the relative position of the initial cursor.
            string newPlainText = GetPlainText(newText[newTextStartIndex..]);
            Point cursorEnd = ConvertOffsetToPoint(cursorStart, newPlainText, newPlainText.Length);

            if (cursorEnd.Y > topMax)
            {
                int offset = cursorEnd.Y - topMax;
                _initialCursor.Y -= offset;
            }
        }

        _currentText = newText;

        // Wait for a short interval before refreshing again for the in-coming payload.
        // We use a smaller interval (20ms) when rendering code blocks, so as to reduce the flashing when
        // rewriting the whole line. Otherwise, we use the 50ms interval.
        Thread.Sleep(redoWholeLine ? 20 : 50);
    }

    /// <summary>
    /// The regular expression for matching ANSI escape sequences, which consists of the followings in the same order:
    ///  - erase from the current cursor position (inclusive) to the end of the line: ESC[K
    ///  - graphics regex: graphics/color mode ESC[1;2;...m
    ///  - csi regex: CSI escape sequences
    ///  - hyperlink regex: hyperlink escape sequences. Note: '.*?' makes '.*' do non-greedy match.
    /// </summary>
    [GeneratedRegex(@"(\x1b\[K)|(\x1b\[\d+(;\d+)*m)|(\x1b\[\?\d+[hl])|(\x1b\]8;;.*?\x1b\\)", RegexOptions.Compiled)]
    private static partial Regex CreateAnsiRegex();

    private static string GetPlainText(string text)
    {
        if (!text.Contains(ESC))
        {
            return text;
        }

        return AnsiRegex.Replace(text, string.Empty);
    }

    /// <summary>
    /// Return the index up to which inclusively we consider the current text and the new text are the same.
    /// Note that, the return value can range from -1 (nothing is the same) to `cur_text.Length - 1` (all is the same).
    /// </summary>
    private int SameUpTo(string newText, out bool redoWholeLine)
    {
        int i = 0;

        // Note that, `newText` is not necessarily longer than `_currentText`. After more chunks coming in, the trailing
        // part of the raw text may now be considered a markdown structure and thus the new text may now be shorter than
        // the current text.
        for (; i < _currentText.Length && i < newText.Length; i++)
        {
            if (_currentText[i] != newText[i])
            {
                break;
            }
        }

        int j = i - 1;
        redoWholeLine = false;

        if (i < _currentText.Length && _currentText.IndexOf("\x1b[0m", i, StringComparison.Ordinal) != -1)
        {
            // When the portion to be re-written contains the 'RESET' sequence, it's safer to re-write the whole
            // logical line because all existing color or font effect was already reset and so those decorations
            // would be lost if we re-write from the middle of the logical line.
            // Well, this assumes decorations always start fresh for a new logical line, which is truely the case
            // for the code block syntax highlighting done by our Markdown VT render.
            redoWholeLine = true;
            for (; j >= 0; j--)
            {
                if (_currentText[j] == '\n')
                {
                    break;
                }
            }
        }

        return j;
    }

    private Point ConvertOffsetToPoint(Point point, string text, int offset)
    {
        int x = point.X;
        int y = point.Y;

        for (int i = 0; i < offset; i++)
        {
            char c = text[i];
            if (c == '\n')
            {
                y += 1;
                x = 0;
            }
            else
            {
                int size = c.GetCellWidth();
                x += size;
                // Wrap?  No prompt when wrapping
                if (x >= _bufferWidth)
                {
                    // If character didn't fit on current line, it will move entirely to the next line.
                    x = (x == _bufferWidth) ? 0 : size;

                    // If cursor is at column 0 and the next character is newline, let the next loop
                    // iteration increment y.
                    if (x != 0 || !(i + 1 < offset && text[i + 1] == '\n'))
                    {
                        y += 1;
                    }
                }
            }
        }

        // If next character actually exists, and isn't newline, check if wider than the space left on the current line.
        if (text.Length > offset && text[offset] != '\n')
        {
            int size = text[offset].GetCellWidth();
            if (x + size > _bufferWidth)
            {
                // Character was wider than remaining space, so character, and cursor, appear on next line.
                x = 0;
                y++;
            }
        }

        return new Point(x, y);
    }

    /// <summary>
    /// Call this method to report writing to console from outside the stream render.
    /// </summary>
    /// <remarks>
    /// With the MCP tool calls, we may need to render the tool call request while a stream render is active.
    /// This method is used to notify an active stream render about updates in console from elsewhere, so the
    /// stream render can reset its state and start freshly.
    /// Note that, a stream render and the host won't really write to console in parallel. But it is possible
    /// that the stream render wrote some output and stoped, and then the host wrote some other output. Since
    /// the stream render depends on the initial cursor position to refresh all content when new chunks coming
    /// in, it needs to reset its state in such a case, so that it can continue to work correctly afterwards.
    /// </remarks>
    internal static void ConsoleUpdated()
    {
        s_consoleUpdateFlag++;
    }
}

internal struct Point
{
    public int X;
    public int Y;

    internal Point(int x, int y)
    {
        X = x;
        Y = y;
    }

    public override readonly string ToString()
    {
        return string.Format(CultureInfo.InvariantCulture, "{0},{1}", X, Y);
    }
}
