using System.Text.RegularExpressions;
using System.CommandLine;
using System.CommandLine.Parsing;
using System.CommandLine.Completions;

using Microsoft.PowerShell;
using AIShell.Abstraction;
using AIShell.Kernel.Commands;

namespace AIShell.Kernel;

internal class ReadLineHelper : IReadLineHelper
{
    // TODO: these colors should be made configurable.
    const string Agent = "\x1b[96m";
    const string Command = "\x1b[92m";
    const string Parameter = "\x1b[90m";
    const string Argument = "\x1b[39;49m";

    private readonly Shell _shell;
    private readonly CommandRunner _cmdRunner;
    private readonly Comparison<CompletionResult> _comparison;
    private readonly HashSet<string> _commonOptions;

    private readonly string _predictorName;
    private readonly Guid _predictorId;
    private readonly EnumerationOptions _enumerationOptions;

    internal ReadLineHelper(Shell shell, CommandRunner commandRunner)
    {
        _shell = shell;
        _cmdRunner = commandRunner;
        _comparison = new(Compare);
        _commonOptions = new(StringComparer.OrdinalIgnoreCase) { "--help", "-h" };

        _predictorName = "completion";
        _predictorId = Guid.NewGuid();
        _enumerationOptions = new()
        {
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            IgnoreInaccessible = true,
            MatchCasing = MatchCasing.CaseInsensitive,
            MatchType = MatchType.Simple,
            ReturnSpecialDirectories = false,
            RecurseSubdirectories = false,
        };
    }

    private int Compare(CompletionResult x, CompletionResult y)
    {
        if (_commonOptions.Contains(x.CompletionText))
        {
            return _commonOptions.Contains(y.CompletionText)
                ? string.Compare(x.CompletionText, y.CompletionText, ignoreCase: true)
                : 1;
        }

        if (_commonOptions.Contains(y.CompletionText))
        {
            return -1;
        }

        return string.Compare(x.CompletionText, y.CompletionText, ignoreCase: true);
    }

    private static string WildcardToRegex(string value)
    {
        return "^" + Regex.Escape(value).Replace("\\?", ".").Replace("\\*", ".*") + "$";
    }

    private List<CompletionResult> CompleteCommandName(string wildcard)
    {
        List<CompletionResult> result = null;
        Regex regex = new(WildcardToRegex(wildcard), RegexOptions.IgnoreCase);
        foreach (var entry in _cmdRunner.Commands)
        {
            string name = entry.Key;
            if (regex.IsMatch(name))
            {
                result ??= new();
                result.Add(new CompletionResult(name, name, CompletionResultType.Command, toolTip: entry.Value.Description));
            }
        }

        result?.Sort(_comparison);
        return result;
    }

    private List<CompletionResult> CompleteFileSystemPath(string wordToComplete)
    {
        bool startsWithTilde = false;
        bool alreadyQuoted = wordToComplete.Contains(' ');
        string homeDirectory = null;
        List<CompletionResult> result = null;

        // Check if the path starts with tilde.
        static bool StartsWithTilde(string path)
        {
            return !string.IsNullOrEmpty(path)
                && path.Length >= 2
                && path[0] is '~'
                && path[1] == Path.DirectorySeparatorChar;
        }

        // Check if the path should be quoted.
        string QuoteIfNeeded(string path)
        {
            // Do not add quoting if the original string is already quoted.
            return !alreadyQuoted && path.Contains(' ') ? $"\"{path}\"" : path;
        }

        // Add one result to the result list.
        void AddOneResult(string path, bool isContainer)
        {
            result ??= [];
            string filePath = startsWithTilde ? path.Replace(homeDirectory, "~") : path;
            string text = QuoteIfNeeded(filePath);

            CompletionResultType resultType = isContainer
                ? CompletionResultType.ProviderContainer
                : CompletionResultType.ProviderItem;
            result.Add(new CompletionResult(text, text, resultType, toolTip: null));
        }

        if (!Path.IsPathFullyQualified(wordToComplete) &&
            (startsWithTilde = StartsWithTilde(wordToComplete)) is false)
        {
            return null;
        }

        string rootPath, fileName;
        if (wordToComplete.EndsWith(Path.DirectorySeparatorChar))
        {
            rootPath = wordToComplete.TrimEnd(Path.DirectorySeparatorChar);
            fileName = "*";
        }
        else
        {
            rootPath = Path.GetDirectoryName(wordToComplete);
            fileName = Path.GetFileName(wordToComplete) + "*";
        }

        if (startsWithTilde)
        {
            rootPath = Utils.ResolveTilde(rootPath);
            homeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (!Directory.Exists(rootPath))
        {
            return null;
        }

        foreach (string dir in Directory.EnumerateDirectories(rootPath, fileName, _enumerationOptions))
        {
            AddOneResult(dir, isContainer: true);
        }

        foreach (string file in Directory.EnumerateFiles(rootPath, fileName, _enumerationOptions))
        {
            AddOneResult(file, isContainer: false);
        }

        return result;
    }

    /// <summary>
    /// Get tab completion results.
    /// </summary>
    /// <param name="input">The input from user.</param>
    /// <param name="cursorIndex">The current cursor index.</param>
    public CommandCompletion CompleteInput(string input, int cursorIndex)
    {
        if (cursorIndex is 0)
        {
            return null;
        }

        if (input.StartsWith('@'))
        {
            return CompleteForAgent(input, cursorIndex);
        }
        else if (input.StartsWith('/'))
        {
            return CompleteForCommand(input, cursorIndex);
        }

        return null;
    }

    private CommandCompletion CompleteForAgent(string input, int cursorIndex)
    {
        int index = input.IndexOf(' ');
        if (index is not -1 && cursorIndex > index)
        {
            return null;
        }

        string targetName = index is -1 ? input[1..] : input[1..index];
        List<CompletionResult> matches = null;

        foreach (var a in _shell.Agents)
        {
            if (ReferenceEquals(a, _shell.ActiveAgent))
            {
                // Skip the current active agent.
                continue;
            }

            string agentName = a.Impl.Name;
            if (agentName.StartsWith(targetName, StringComparison.OrdinalIgnoreCase))
            {
                matches ??= [];
                matches.Add(new CompletionResult(agentName, agentName, CompletionResultType.ParameterValue, a.Impl.Description));
            }
        }

        if (matches is not null)
        {
            return new CommandCompletion(matches, -1, replacementIndex: 1, replacementLength: targetName.Length);
        }

        return null;
    }

    private CommandCompletion CompleteForCommand(string input, int cursorIndex)
    {
        string cmdLine = input[1..].Trim();
        if (cmdLine.Length is 0)
        {
            var matches = CompleteCommandName("*");
            return matches is null ? null : new CommandCompletion(matches, -1, cursorIndex, 0);
        }

        int offset = input.IndexOf(cmdLine);
        if (cursorIndex < offset)
        {
            return null;
        }

        int index = cmdLine.IndexOf(' ');
        string cmdName = index is -1
            ? cmdLine
            : cmdLine[0..index];

        if (cursorIndex <= offset + cmdName.Length)
        {
            var matches = CompleteCommandName($"{cmdName}*");
            return matches is null ? null : new CommandCompletion(matches, -1, offset, cmdName.Length);
        }

        if (_cmdRunner.Commands.TryGetValue(cmdName, out CommandBase command))
        {
            try
            {
                ParseResult parseResult = command.Parser.Parse(input[offset..]);
                IEnumerable<CompletionItem> items = parseResult.GetCompletions(cursorIndex - offset);

                List<CompletionResult> matches = null;
                foreach (CompletionItem item in items)
                {
                    matches ??= new();
                    matches.Add(
                        new CompletionResult(
                            item.InsertText,
                            item.Label,
                            item.InsertText.StartsWith('-')
                                ? CompletionResultType.ParameterName
                                : CompletionResultType.ParameterValue,
                            item.Detail));
                }

                matches?.Sort(_comparison);

                int tokenStartIndex = -1;
                Token tokenAtCursor = null;
                int start = offset + cmdName.Length;

                foreach (Token token in parseResult.Tokens)
                {
                    string value = token.Value;
                    int i = input.IndexOf(value, start);
                    if (cursorIndex >= i && cursorIndex <= i + value.Length)
                    {
                        tokenStartIndex = i;
                        tokenAtCursor = token;
                        break;
                    }
                }

                if (matches is null && tokenAtCursor?.Type is TokenType.Argument)
                {
                    matches = CompleteFileSystemPath(tokenAtCursor.Value);
                }

                if (matches is not null)
                {
                    int replaceIndex = cursorIndex;
                    int replaceLength = 0;

                    if (tokenAtCursor is not null)
                    {
                        replaceIndex = tokenStartIndex;
                        replaceLength = tokenAtCursor.Value.Length;
                    }

                    return new CommandCompletion(matches, -1, replaceIndex, replaceLength);
                }
            }
            catch
            {
                // Ignore all exceptions.
            }
        }

        return null;
    }

    private List<PredictionResult> PredictInput(string input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return null;
        }

        CommandCompletion completion = CompleteInput(input, input.Length);
        if (completion is null || completion.CompletionMatches.Count is 0)
        {
            return null;
        }

        List<PredictiveSuggestion> suggestions = new();
        foreach (CompletionResult result in completion.CompletionMatches)
        {
            var span = input.AsSpan(0, completion.ReplacementIndex);
            suggestions.Add(new PredictiveSuggestion($"{span}{result.CompletionText}", result.ToolTip));
        }

        return new List<PredictionResult>()
        {
            new(_predictorId, _predictorName, session: null, suggestions)
        };
    }

    /// <summary>
    /// Predict based on user's input.
    /// </summary>
    /// <param name="input">The input from user.</param>
    public Task<List<PredictionResult>> PredictInputAsync(string input)
    {
        return Task.Run(() => PredictInput(input));
    }

    /// <summary>
    /// Get the syntax highlighting color for the character at the specified <paramref name="index"/>.
    /// </summary>
    /// <param name="input">The input from user.</param>
    /// <param name="index">The index of the char to be rendered.</param>
    /// <returns>VT sequence of the color.</returns>
    public string GetSyntaxHighlightingColor(string input, int index)
    {
        if (input.StartsWith('@'))
        {
            int spaceIndex = input.IndexOf(' ');
            if (spaceIndex is -1 || index < spaceIndex)
            {
                return Agent;
            }

            return null;
        }
        else if (input.StartsWith('/'))
        {
            // TODO: we should try tokenizing the command to cover single-quoted and double-quoted strings.
            // The tokenization state should be cached so that it can be reused when the input is unchanged.
            if (char.IsWhiteSpace(input[index]))
            {
                return null;
            }

            int spaceIndex = input.IndexOf(' ');
            if (spaceIndex is -1 || index < spaceIndex)
            {
                return Command;
            }

            spaceIndex = input.LastIndexOf(' ', index);
            if (input[spaceIndex + 1] is '-')
            {
                return Parameter;
            }

            return Argument;
        }

        return null;
    }
}

internal class PromptHelper : IReadLineHelper
{
    private const string GUID = "744123ff-aefe-42e9-b38b-6f5416d4c795";

    private readonly string _predictorName;
    private readonly Guid _predictorId;
    private readonly IList<string> _candidates;

    internal PromptHelper(IList<string> candidates)
    {
        _candidates = candidates;
        _predictorName = "suggestion";
        _predictorId = new Guid(GUID);
    }

    public CommandCompletion CompleteInput(string input, int cursorIndex)
    {
        if (_candidates is null || _candidates.Count is 0)
        {
            return null;
        }

        List<CompletionResult> matches = null;
        foreach (string value in _candidates)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (value.StartsWith(input, StringComparison.OrdinalIgnoreCase))
            {
                matches ??= [];
                matches.Add(new CompletionResult(value, value, CompletionResultType.ParameterValue, toolTip: null));
            }
        }

        return matches is null ? null : new CommandCompletion(matches, -1, replacementIndex: 0, replacementLength: input.Length);
    }

    public Task<List<PredictionResult>> PredictInputAsync(string input)
    {
        return Task.Run(() => PredictInput(input));
    }

    private List<PredictionResult> PredictInput(string input)
    {
        if (_candidates is null || _candidates.Count is 0)
        {
            return null;
        }

        int index = 0;
        List<PredictiveSuggestion> suggestions = null;
        foreach (string value in _candidates)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            if (value.StartsWith(input, StringComparison.OrdinalIgnoreCase))
            {
                suggestions ??= [];
                suggestions.Insert(index++, new PredictiveSuggestion(value));
            }
            else if (value.Contains(input, StringComparison.OrdinalIgnoreCase))
            {
                suggestions ??= [];
                suggestions.Add(new PredictiveSuggestion(value));
            }
        }

        return suggestions is null ? null : [new(_predictorId, _predictorName, session: null, suggestions)];
    }

    public string GetSyntaxHighlightingColor(string buffer, int index)
    {
        return null;
    }
}
