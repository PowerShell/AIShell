using System.CommandLine;
using AIShell.Abstraction;

namespace AIShell.Kernel.Commands;

internal sealed class ClearCommand : CommandBase
{
    public ClearCommand()
        : base("clear", "Clear the screen.")
    {
        this.SetHandler(ClearAction);
        this.AddAlias("cls");
    }

    private void ClearAction()
    {
        Console.Clear();
    }
}
