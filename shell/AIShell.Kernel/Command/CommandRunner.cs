using System.CommandLine;
using System.CommandLine.Parsing;
using AIShell.Abstraction;

namespace AIShell.Kernel.Commands;

internal class CommandRunner
{
    internal const string Core = "Core";

    private readonly Shell _shell;
    private readonly Dictionary<string, CommandBase> _commands;

    /// <summary>
    /// Available commands.
    /// </summary>
    internal Dictionary<string, CommandBase> Commands => _commands;

    /// <summary>
    /// Creates an instance of <see cref="CommandRunner"/>.
    /// </summary>
    internal CommandRunner(Shell shell)
    {
        _shell = shell;
        _commands = new(StringComparer.OrdinalIgnoreCase);

        var buildin = new CommandBase[]
        {
            new AgentCommand(),
            new ClearCommand(),
            new CodeCommand(),
            new DislikeCommand(),
            new ExitCommand(),
            new LikeCommand(),
            new RefreshCommand(),
            new RetryCommand(),
            new HelpCommand(),
            //new RenderCommand(),
        };

        LoadCommands(buildin, Core);
    }

    /// <summary>
    /// Load commands into the runner.
    /// </summary>
    /// <param name="commands"></param>
    /// <param name="agentName"></param>
    internal void LoadCommands(IEnumerable<CommandBase> commands, string agentName)
    {
        if (commands is null)
        {
            return;
        }

        foreach (CommandBase command in commands)
        {
            command.Shell = _shell;
            command.Source = agentName;

            // The 'command.Name' is included in the 'command.Aliases' collection.
            // TODO: need to think about how to handle command names/aliases collision.
            // We don't handle collision today -- it will throw when collision happens.
            foreach (string alias in command.Aliases)
            {
                _commands.Add(alias, command);
            }
        }
    }

    /// <summary>
    /// Unload angent commands from the runner.
    /// </summary>
    internal void UnloadAgentCommands()
    {
        var agentCommands = new List<CommandBase>();
        foreach (var command in _commands.Values)
        {
            if (command.Source is Core)
            {
                continue;
            }

            agentCommands.Add(command);
        }

        foreach (var command in agentCommands)
        {
            // The 'command.Name' is included in the 'command.Aliases' collection.
            // TODO: need to update accordingly when we handle command names/aliases collision.
            foreach (string alias in command.Aliases)
            {
                _commands.Remove(alias);
            }

            command.Dispose();
        }
    }

    /// <summary>
    /// Resolve the given command name.
    /// </summary>
    /// <returns>
    /// The corresponding command or null if the name cannot be resolved
    /// </returns>
    internal CommandBase ResolveCommand(string name)
    {
        return _commands.TryGetValue(name, out CommandBase value) ? value : null;
    }

    /// <summary>
    /// Invoke the given command line.
    /// </summary>
    /// <param name="commandLine">The command line to run, which may include flags and arguments.</param>
    /// <exception cref="AIShellException"></exception>
    internal void InvokeCommand(string commandLine)
    {
        int index = commandLine.IndexOf(' ');
        string commandName = index is -1 ? commandLine : commandLine[..index];

        CommandBase command = ResolveCommand(commandName)
            ?? throw new AIShellException($"The term '{commandName}' is not recognized as a name of a command.");

        command.Parser.Invoke(commandLine);
    }
}
