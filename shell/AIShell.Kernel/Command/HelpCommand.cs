using System.CommandLine;
using AIShell.Abstraction;

namespace AIShell.Kernel.Commands;

internal sealed class HelpCommand : CommandBase
{
    public HelpCommand()
        : base("help", "Show all available commands.")
    {
        this.SetHandler(HelpAction);
    }

    private void HelpAction()
    {
        var shellImpl = (Shell)Shell;
        var host = shellImpl.Host;

        var commands = shellImpl.CommandRunner.Commands;
        var list = commands.Values.Distinct().Order(new CommandComparer()).ToList();

        host.WriteLine();
        host.MarkupLine("[bold white]-[/] Type then press [bold olive underline]Enter[/] to chat with the chosen AI agent.");
        host.MarkupLine("[bold white]-[/] All available commands are listed in the table below.");

        var elements = new IRenderElement<CommandBase>[]
        {
            new CustomElement<CommandBase>("Name", c => $"/{c.Name}"),
            new PropertyElement<CommandBase>(nameof(Description)),
            new CustomElement<CommandBase>("Source", c => c.Source),
        };

        shellImpl.Host.RenderTable(list, elements);
        host.MarkupLine($"Learn more at [link]https://aka.ms/AIShell[/].\n");
    }

    private class CommandComparer : IComparer<CommandBase>
    {
        public int Compare(CommandBase x, CommandBase y)
        {
            if (x == y) return 0;
            if (x is null) return -1;
            if (y is null) return 1;

            if (x.Source == y.Source) return string.Compare(x.Name, y.Name);
            if (x.Source != CommandRunner.Core) return -1;
            if (y.Source != CommandRunner.Core) return 1;

            // Should be unreachable here.
            throw new NotImplementedException();
        }
    }
}
