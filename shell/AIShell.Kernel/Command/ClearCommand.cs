using System.CommandLine;
using AIShell.Abstraction;

namespace AIShell.Kernel.Commands;

internal sealed class ClearCommand : CommandBase
{
    public ClearCommand()
        : base("cls", "Clear the screen.")
    {
        this.SetHandler(ClearAction);
        this.AddAlias("clear");
    }

    private void ClearAction()
    {
        Console.Clear();
    }
}
