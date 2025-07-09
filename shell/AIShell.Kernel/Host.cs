using System.Reflection;
using System.Text;

using AIShell.Abstraction;
using AIShell.Kernel.Mcp;
using Markdig.Helpers;
using Microsoft.PowerShell;
using Spectre.Console;
using Spectre.Console.Json;
using Spectre.Console.Rendering;

namespace AIShell.Kernel;

/// <summary>
/// Host implementation of the AIShell.
/// </summary>
internal sealed class Host : IHost
{
    private readonly bool _inputRedirected;
    private readonly bool _outputRedirected;
    private readonly bool _errorRedirected;
    private readonly IAnsiConsole _stderrConsole;

    internal MarkdownRender MarkdownRender { get; }

    /// <summary>
    /// Creates a new instance of the <see cref="Host"/>.
    /// </summary>
    internal Host()
    {
        _inputRedirected = Console.IsInputRedirected;
        _outputRedirected = Console.IsOutputRedirected;
        _errorRedirected = Console.IsErrorRedirected;
        _stderrConsole = AnsiConsole.Create(
            new AnsiConsoleSettings
            {
                Ansi = AnsiSupport.Detect,
                ColorSystem = ColorSystemSupport.Detect,
                Out = new AnsiConsoleOutput(Console.Error),
            }
        );

        MarkdownRender = new MarkdownRender();   
    }

    /// <inheritdoc/>
    public IHost Write(string value)
    {
        Console.Write(value);
        return this;
    }

    /// <inheritdoc/>
    public IHost WriteLine()
    {
        Console.WriteLine();
        return this;
    }

    /// <inheritdoc/>
    public IHost WriteLine(string value)
    {
        Console.WriteLine(value);
        return this;
    }

    /// <inheritdoc/>
    public IHost WriteErrorLine()
    {
        Console.Error.WriteLine();
        return this;
    }

    /// <inheritdoc/>
    public IHost WriteErrorLine(string value)
    {
        if (Console.IsErrorRedirected || string.IsNullOrEmpty(value))
        {
            Console.Error.WriteLine(value);
        }
        else
        {
            _stderrConsole.MarkupLine(Formatter.Error(value.EscapeMarkup()));
        }

        return this;
    }

    /// <inheritdoc/>
    public IHost Markup(string value)
    {
        if (!string.IsNullOrEmpty(value))
        {
            AnsiConsole.Markup(value);
        }

        return this;
    }

    /// <inheritdoc/>
    public IHost MarkupLine(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            Console.WriteLine();
        }
        else
        {
            AnsiConsole.MarkupLine(value);
        }

        return this;
    }

    /// <inheritdoc/>
    public IHost MarkupNoteLine(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            Console.WriteLine();
        }
        else
        {
            AnsiConsole.MarkupLine($"[orange3]NOTE:[/] {value}");
        }

        return this;
    }

    /// <inheritdoc/>
    public IHost MarkupWarningLine(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            Console.WriteLine();
        }
        else
        {
            AnsiConsole.MarkupLine(Formatter.Warning(value));
        }

        return this;
    }

    /// <inheritdoc/>
    public IStreamRender NewStreamRender(CancellationToken cancellationToken)
    {
        return _outputRedirected
            ? new DummyStreamRender(cancellationToken)
            : new FancyStreamRender(MarkdownRender, cancellationToken);
    }

    /// <inheritdoc/>
    public void RenderFullResponse(string response)
    {
        if (string.IsNullOrWhiteSpace(response))
        {
            WriteLine();
            MarkupNoteLine("Received response is empty or contains whitespace only.");
        }
        else if (_outputRedirected)
        {
            WriteLine(response);
        }
        else
        {
            // Render the markdown only if standard output is not redirected.
            string text = MarkdownRender.RenderText(response);
            if (!LeadingWhiteSpaceHasNewLine(text))
            {
                WriteLine();
            }

            WriteLine(text);
        }
    }

    /// <inheritdoc/>
    public void RenderTable<T>(IList<T> sources)
    {
        RequireStdout(operation: "render table");
        ArgumentNullException.ThrowIfNull(sources);

        if (sources.Count is 0)
        {
            return;
        }

        var elements = new List<IRenderElement<T>>();
        foreach (PropertyInfo property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.CanRead)
            {
                elements.Add(new PropertyElement<T>(property));
            }
        }

        RenderTable(sources, elements);
    }

    /// <inheritdoc/>
    public void RenderTable<T>(IList<T> sources, IList<IRenderElement<T>> elements)
    {
        RequireStdout(operation: "render table");

        ArgumentNullException.ThrowIfNull(sources);
        ArgumentNullException.ThrowIfNull(elements);

        if (sources.Count is 0 || elements.Count is 0)
        {
            return;
        }

        var spectreTable = new Table()
            .LeftAligned()
            .SimpleBorder()
            .BorderColor(Color.Green);

        // Add columns.
        foreach (var element in elements)
        {
            spectreTable.AddColumn($"[green bold]{element.Name}[/]");
        }

        // Add rows.
        int rowIndex = -1;
        foreach (T source in sources)
        {
            spectreTable.AddEmptyRow();
            rowIndex++;

            for (int i = 0; i < elements.Count; i++)
            {
                var element = elements[i];
                string value = element.Value(source).EscapeMarkup() ?? string.Empty;
                spectreTable.Rows.Update(rowIndex, i, new Markup(value));
            }
        }

        AnsiConsole.Write(spectreTable);
    }

    /// <inheritdoc/>
    public void RenderList<T>(T source)
    {
        RequireStdout(operation: "render list");
        ArgumentNullException.ThrowIfNull(source);

        if (source is IDictionary<string, string> dict)
        {
            var elements = new List<IRenderElement<IDictionary<string, string>>>(capacity: dict.Count);
            foreach (string key in dict.Keys)
            {
                elements.Add(new KeyValueElement<IDictionary<string, string>>(key));
            }

            RenderList(dict, elements);
        }
        else
        {
            var elements = new List<IRenderElement<T>>();
            foreach (PropertyInfo property in typeof(T).GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.CanRead)
                {
                    elements.Add(new PropertyElement<T>(property));
                }
            }

            RenderList(source, elements);
        }
    }

    /// <inheritdoc/>
    public void RenderList<T>(T source, IList<IRenderElement<T>> elements)
    {
        RequireStdout(operation: "render list");

        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(elements);

        if (elements.Count is 0)
        {
            return;
        }

        int maxLabelLen = 0;
        foreach (var element in elements)
        {
            int len = element.Name.Length;
            if (len > maxLabelLen)
            {
                maxLabelLen = len;
            }
        }

        var spectreTable = new Table()
            .HideHeaders()
            .NoBorder()
            .AddColumn("Labels", c => c.NoWrap().LeftAligned().Width(maxLabelLen + 4))
            .AddColumn("Values");

        foreach (var element in elements)
        {
            string col1 = element.Name;
            string col2 = element.Value(source).EscapeMarkup() ?? string.Empty;
            spectreTable.AddRow(Spectre.Console.Markup.FromInterpolated($"  [green bold]{col1} :[/]"), new Markup(col2));
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Write(spectreTable);
        AnsiConsole.WriteLine();
    }

    /// <inheritdoc/>
    public void RenderDivider(string text, DividerAlignment alignment)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);
        RequireStdout(operation: "render divider");

        if (!text.Contains("[/]"))
        {
            text = $"[yellow]{text.EscapeMarkup()}[/]";
        }

        Justify justify = alignment is DividerAlignment.Left ? Justify.Left : Justify.Right;
        Rule rule = new Rule(text).RuleStyle("grey").Justify(justify);
        AnsiConsole.Write(rule);
    }

    /// <inheritdoc/>
    public async Task<T> RunWithSpinnerAsync<T>(Func<Task<T>> func, string status = null, SpinnerKind? spinnerKind = null)
    {
        if (_outputRedirected && _errorRedirected)
        {
            // Since both stdout and stderr are redirected, no need to use a spinner for the async call.
            return await func().ConfigureAwait(false);
        }

        IAnsiConsole ansiConsole = _outputRedirected ? _stderrConsole : AnsiConsole.Console;
        Capabilities caps = ansiConsole.Profile.Capabilities;
        bool interactive = caps.Interactive;

        try
        {
            // When standard input is redirected, AnsiConsole's auto detection believes it's non-interactive,
            // and thus doesn't render Status or Progress. However, redirected input should not affect the
            // Status/Progress rendering as long as its output target, stderr or stdout, is not redirected.
            caps.Interactive = true;
            status ??= "Generating...";

            return await ansiConsole
                .Status()
                .AutoRefresh(true)
                .Spinner(GetSpinner(spinnerKind))
                .SpinnerStyle(new Style(Color.Olive))
                .StartAsync(
                    $"[italic slowblink]{status.EscapeMarkup()}[/]",
                    statusContext => func())
                .ConfigureAwait(false);
        }
        finally
        {
            caps.Interactive = interactive;
        }
    }

    /// <inheritdoc/>
    public async Task<T> RunWithSpinnerAsync<T>(Func<IStatusContext, Task<T>> func, string status, SpinnerKind? spinnerKind = null)
    {
        if (_outputRedirected && _errorRedirected)
        {
            // Since both stdout and stderr are redirected, no need to use a spinner for the async call.
            return await func(null).ConfigureAwait(false);
        }

        IAnsiConsole ansiConsole = _outputRedirected ? _stderrConsole : AnsiConsole.Console;
        Capabilities caps = ansiConsole.Profile.Capabilities;
        bool interactive = caps.Interactive;

        try
        {
            // When standard input is redirected, AnsiConsole's auto detection believes it's non-interactive,
            // and thus doesn't render Status or Progress. However, redirected input should not affect the
            // Status/Progress rendering as long as its output target, stderr or stdout, is not redirected.
            caps.Interactive = true;
            status ??= "Generating...";

            return await ansiConsole
                .Status()
                .AutoRefresh(true)
                .Spinner(GetSpinner(spinnerKind))
                .SpinnerStyle(new Style(Color.Olive))
                .StartAsync(
                    $"[italic slowblink]{status.EscapeMarkup()}[/]",
                    ctx => func(new StatusContext(ctx)))
                .ConfigureAwait(false);
        }
        finally
        {
            caps.Interactive = interactive;
        }
    }

    /// <inheritdoc/>
    public async Task<T> PromptForSelectionAsync<T>(string title, IEnumerable<T> choices, Func<T, string> converter = null, CancellationToken cancellationToken = default)
    {
        string operation = "prompt for selection";
        RequireStdin(operation);
        RequireStdoutOrStderr(operation);

        if (choices is null || !choices.Any())
        {
            throw new ArgumentException("No choice was specified.", nameof(choices));
        }

        IAnsiConsole ansiConsole = _outputRedirected ? _stderrConsole : AnsiConsole.Console;
        title ??= "Please select from the below list:";
        converter ??= static t => t is string str ? str : t.ToString();

        var selection = new SelectionPrompt<T>()
            .Title(title)
            .PageSize(10)
            .UseConverter(converter)
            .MoreChoicesText("[#7a7a7a](Move up and down to see more choices)[/]")
            .AddChoices(choices);

        return await selection.ShowAsync(ansiConsole, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<bool> PromptForConfirmationAsync(string prompt, bool defaultValue, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrEmpty(prompt);
        string operation = "prompt for confirmation";

        RequireStdin(operation);
        RequireStdoutOrStderr(operation);

        prompt = $"[orange3 on italic]{(prompt.Contains("[/]") ? prompt : prompt.EscapeMarkup())}[/]";
        IAnsiConsole ansiConsole = _outputRedirected ? _stderrConsole : AnsiConsole.Console;
        var confirmation = new ConfirmationPrompt(prompt) { DefaultValue = defaultValue };

        return await confirmation.ShowAsync(ansiConsole, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<string> PromptForSecretAsync(string prompt, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(prompt);
        string operation = "prompt for secret";

        RequireStdin(operation);
        RequireStdoutOrStderr(operation);

        IAnsiConsole ansiConsole = _outputRedirected ? _stderrConsole : AnsiConsole.Console;
        return await new TextPrompt<string>(prompt)
            .PromptStyle(Color.Red)
            .Secret()
            .ShowAsync(ansiConsole, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public async Task<string> PromptForTextAsync(string prompt, bool optional, IList<string> choices, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrEmpty(prompt);
        string operation = "prompt for text";

        RequireStdin(operation);
        RequireStdoutOrStderr(operation);

        IAnsiConsole ansiConsole = _outputRedirected ? _stderrConsole : AnsiConsole.Console;
        string promptToUse = optional ? $"[#7a7a7a][[Optional]][/] {prompt}" : prompt;
        var textPrompt = new TextPrompt<string>(promptToUse) { AllowEmpty = optional };

        if (choices?.Count > 0)
        {
            textPrompt.AddChoices(choices)
                .InvalidChoiceMessage("[red]Please choose one from the choices![/]");
        }

        return await textPrompt
            .PromptStyle(new Style(Color.Teal))
            .ShowAsync(ansiConsole, cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    public string PromptForArgument(ArgumentInfo argInfo, bool printCaption)
    {
        if (printCaption)
        {
            WriteLine(argInfo.Type is ArgumentInfo.DataType.@string
                ? argInfo.Description
                : $"{argInfo.Description}. Value type: {argInfo.Type}");

            if (!string.IsNullOrEmpty(argInfo.Restriction))
            {
                WriteLine(argInfo.Restriction);
            }
        }

        var suggestions = argInfo.Suggestions;
        if (argInfo.Type is ArgumentInfo.DataType.@bool)
        {
            suggestions ??= ["ture", "flase"];
        }

        var options = PSConsoleReadLine.GetOptions();
        var oldAddToHistoryHandler = options.AddToHistoryHandler;
        var oldReadLineHelper = options.ReadLineHelper;
        var oldPredictionView = options.PredictionViewStyle;
        var oldPredictionSource = options.PredictionSource;

        var newOptions = new SetPSReadLineOption
        {
            AddToHistoryHandler = c => AddToHistoryOption.SkipAdding,
            ReadLineHelper = new PromptHelper(suggestions),
            PredictionSource = PredictionSource.Plugin,
            PredictionViewStyle = PredictionViewStyle.ListView,
        };

        try
        {
            Markup($"[lime]{argInfo.Name}[/]: ");
            PSConsoleReadLine.SetOptions(newOptions);
            string value = PSConsoleReadLine.ReadLine(CancellationToken.None);
            if (Console.CursorLeft is not 0)
            {
                // Ctrl+c was pressed by the user.
                WriteLine();
                throw new OperationCanceledException();
            }

            return value;
        }
        finally
        {
            newOptions.AddToHistoryHandler = oldAddToHistoryHandler;
            newOptions.ReadLineHelper = oldReadLineHelper;
            newOptions.PredictionSource = oldPredictionSource;
            newOptions.PredictionViewStyle = oldPredictionView;
            PSConsoleReadLine.SetOptions(newOptions);
        }
    }

    /// <summary>
    /// Render text content in the "for-reference" style.
    /// </summary>
    /// <param name="header">Title for the content.</param>
    /// <param name="content">Text to be rendered.</param>
    internal void RenderReferenceText(string header, string content)
    {
        RequireStdoutOrStderr(operation: "Render reference");
        IAnsiConsole ansiConsole = _outputRedirected ? _stderrConsole : AnsiConsole.Console;

        var panel = new Panel($"\n[italic]{content.EscapeMarkup()}[/]\n")
            .RoundedBorder()
            .BorderColor(Color.DarkCyan)
            .Header($"[orange3 on italic] {header.Trim()} [/]");

        ansiConsole.WriteLine();
        ansiConsole.Write(panel);
        ansiConsole.WriteLine();
    }

    /// <summary>
    /// Render the MCP tool call request.
    /// </summary>
    /// <param name="tool">The MCP tool.</param>
    /// <param name="jsonArgs">The arguments in JSON form to be sent for the tool call.</param>
    internal void RenderMcpToolCallRequest(McpTool tool, string jsonArgs)
    {
        RequireStdoutOrStderr(operation: "render MCP tool call request");
        IAnsiConsole ansiConsole = _outputRedirected ? _stderrConsole : AnsiConsole.Console;

        bool hasArgs = !string.IsNullOrEmpty(jsonArgs);
        IRenderable content = new Markup($"""

            [bold]Run [olive]{tool.OriginalName}[/] from [olive]{tool.ServerName}[/] (MCP server)[/]

            {tool.Description}

            Input:{(hasArgs ? string.Empty : " <none>")}
            """);

        if (hasArgs)
        {
            var json = new JsonText(jsonArgs)
                .MemberColor(Color.Aqua)
                .ColonColor(Color.White)
                .CommaColor(Color.White)
                .StringStyle(Color.Tan);

            content = new Grid()
                .AddColumn(new GridColumn())
                .AddRow(content)
                .AddRow(json);
        }

        var panel = new Panel(content)
            .Expand()
            .RoundedBorder()
            .Header("[green]  Tool Call Request  [/]")
            .BorderColor(Color.Grey);

        ansiConsole.WriteLine();
        ansiConsole.Write(panel);
        FancyStreamRender.ConsoleUpdated();
    }

    /// <summary>
    /// Render the built-in tool call request.
    /// </summary>
    internal void RenderBuiltInToolCallRequest(string toolName, string description, Tuple<string, string> argument)
    {
        RequireStdoutOrStderr(operation: "render built-in tool call request");
        IAnsiConsole ansiConsole = _outputRedirected ? _stderrConsole : AnsiConsole.Console;

        bool hasArgs = argument is not null;
        string argLine = hasArgs ? $"{argument.Item1}:" : $"Input: <none>";
        IRenderable content = new Markup($"""

            [bold]Run [olive]{toolName}[/] from [olive]{McpManager.BuiltInServerName}[/] (Built-in tool)[/]

            {description}

            {argLine}
            """);

        if (hasArgs)
        {
            content = new Grid()
                .AddColumn(new GridColumn())
                .AddRow(content)
                .AddRow(argument.Item2.EscapeMarkup());
        }

        var panel = new Panel(content)
            .Expand()
            .RoundedBorder()
            .Header("[green]  Tool Call Request  [/]")
            .BorderColor(Color.Grey);

        ansiConsole.WriteLine();
        ansiConsole.Write(panel);
        FancyStreamRender.ConsoleUpdated();
    }

    /// <summary>
    /// Render a table with information about available MCP servers and tools.
    /// </summary>
    /// <param name="mcpManager">The MCP manager instance.</param>
    internal void RenderMcpServersAndTools(McpManager mcpManager)
    {
        RequireStdout(operation: "render MCP servers and tools");

        var toolTable = new Table()
            .LeftAligned()
            .SimpleBorder()
            .BorderColor(Color.Green);

        toolTable.AddColumn("[green bold]Server[/]");
        toolTable.AddColumn("[green bold]Tool[/]");
        toolTable.AddColumn("[green bold]Description[/]");

        List<(string name, string status, string info)> readyServers = null, startingServers = null, failedServers = null;
        foreach (var (name, server) in mcpManager.McpServers)
        {
            (int code, string status, string info) = server.IsInitFinished
                ? server.Error is null
                    ? (1, "[green]\u2713 Ready[/]", string.Empty)
                    : (-1, "[red]\u2717 Failed[/]", $"[red]{server.Error.Message.EscapeMarkup()}[/]")
                : (0, "[yellow]\u25CB Starting[/]", string.Empty);

            var list = code switch
            {
                1 => readyServers ??= [],
                0 => startingServers ??= [],
                _ => failedServers ??= [],
            };

            list.Add((name, status, info));
        }

        if (startingServers is not null)
        {
            foreach (var (name, status, info) in startingServers)
            {
                toolTable.AddRow($"[olive underline]{name}[/]", status, info);
            }
        }

        if (failedServers is not null)
        {
            foreach (var (name, status, info) in failedServers)
            {
                toolTable.AddRow($"[olive underline]{name}[/]", status, info);
            }
        }

        if (mcpManager.BuiltInTools is { Count: > 0 })
        {
            if (toolTable.Rows is { Count: > 0 })
            {
                toolTable.AddEmptyRow();
            }

            toolTable.AddRow($"[olive underline]{McpManager.BuiltInServerName}[/]", "[green]\u2713 Ready[/]", string.Empty);
            foreach (var item in mcpManager.BuiltInTools)
            {
                string description = item.Value.Description;
                int index = description.IndexOf('\n');
                if (index > 0)
                {
                    description = description[..index].Trim();
                }
                toolTable.AddRow(string.Empty, item.Key.EscapeMarkup(), description.EscapeMarkup());
            }
        }

        if (readyServers is not null)
        {
            foreach (var (name, status, info) in readyServers)
            {
                if (toolTable.Rows is { Count: > 0 })
                {
                    toolTable.AddEmptyRow();
                }

                var server = mcpManager.McpServers[name];
                toolTable.AddRow($"[olive underline]{name}[/]", status, info);
                foreach (var item in server.Tools)
                {
                    toolTable.AddRow(string.Empty, item.Key.EscapeMarkup(), item.Value.Description.EscapeMarkup());
                }
            }
        }

        AnsiConsole.Write(toolTable);
    }

    private static Spinner GetSpinner(SpinnerKind? kind)
    {
        return kind switch
        {
            SpinnerKind.Processing => Spinner.Known.Default,
            _ => AsciiLetterSpinner.Default,
        };
    }

    /// <summary>
    /// Throw exception if standard input is redirected.
    /// </summary>
    /// <param name="operation">The intended operation.</param>
    /// <exception cref="InvalidOperationException">Throw the exception if stdin is redirected.</exception>
    private void RequireStdin(string operation)
    {
        if (_inputRedirected)
        {
            throw new InvalidOperationException($"Cannot {operation} when stdin is redirected.");
        }
    }

    /// <summary>
    /// Throw exception if standard output is redirected.
    /// </summary>
    /// <param name="operation">The intended operation.</param>
    /// <exception cref="InvalidOperationException">Throw the exception if stdout is redirected.</exception>
    private void RequireStdout(string operation)
    {
        if (_outputRedirected)
        {
            throw new InvalidOperationException($"Cannot {operation} when the stdout is redirected.");
        }
    }

    /// <summary>
    /// Throw exception if both standard output and error are redirected.
    /// </summary>
    /// <param name="operation">The intended operation.</param>
    /// <exception cref="InvalidOperationException">Throw the exception if stdout and stderr are both redirected.</exception>
    private void RequireStdoutOrStderr(string operation)
    {
        if (_outputRedirected && _errorRedirected)
        {
            throw new InvalidOperationException($"Cannot {operation} when both the stdout and stderr are redirected.");
        }
    }

    /// <summary>
    /// Check if the leading whitespace characters of <paramref name="text"/> contains a newline.
    /// </summary>
    private static bool LeadingWhiteSpaceHasNewLine(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (c == '\n')
            {
                return true;
            }

            if (!c.IsWhitespace())
            {
                break;
            }
        }

        return false;
    }
}

/// <summary>
/// Wrapper of the <see cref="Spectre.Console.StatusContext"/> to not expose 'Spectre.Console' types,
/// so as to avoid requiring all agent implementations to depend on the 'Spectre.Console' package.
/// </summary>
internal sealed class StatusContext : IStatusContext
{
    private readonly Spectre.Console.StatusContext _context;

    internal StatusContext(Spectre.Console.StatusContext context)
    {
        _context = context;
    }

    /// <inheritdoc/>
    public void Status(string status)
    {
        _context.Status($"[italic slowblink]{status.EscapeMarkup()}[/]");
    }
}

internal sealed class AsciiLetterSpinner : Spinner
{
    private const int FrameNumber = 8;
    private readonly List<string> _frames;

    internal static readonly AsciiLetterSpinner Default = new();

    internal AsciiLetterSpinner(int prefixGap = 0, int charLength = 12)
    {
        _frames = new List<string>(capacity: FrameNumber);
        StringBuilder sb = new(capacity: prefixGap + charLength + 2);

        var gap = prefixGap is 0 ? null : new string(' ', prefixGap);
        for (var i = 0; i < FrameNumber; i++)
        {
            sb.Append(gap).Append('/');
            for (var j = 0; j < charLength; j++)
            {
                sb.Append((char)Random.Shared.Next(33, 127));
            }

            _frames.Add(sb.Append('/').ToString());
            sb.Clear();
        }
    }

    public override TimeSpan Interval => TimeSpan.FromMilliseconds(100);
    public override bool IsUnicode => false;
    public override IReadOnlyList<string> Frames => _frames;
}

internal static class Formatter
{
    internal static string Command(string code)
    {
        // Green on grey for command format.
        return $"[rgb(0,195,0) on rgb(48,48,48)] {code} [/]";
    }

    internal static string Error(string message)
    {
        return $"[bold red]ERROR: {message}[/]";
    }

    internal static string Warning(string message)
    {
        return $"[bold yellow]WARNING: {message}[/]";
    }
}
