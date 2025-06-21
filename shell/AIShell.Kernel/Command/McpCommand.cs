using System.CommandLine;
using AIShell.Abstraction;

namespace AIShell.Kernel.Commands;

internal sealed class McpCommand : CommandBase
{
    public McpCommand()
        : base("mcp", "Command for managing MCP servers and tools.")
    {
        this.SetHandler(ShowMCPData);

        //var start = new Command("start", "Start an MCP server.");
        //var stop = new Command("stop", "Stop an MCP server.");
        //var server = new Argument<string>(
        //    name: "server",
        //    getDefaultValue: () => null,
        //    description: "Name of an MCP server.").AddCompletions(AgentCompleter);

        //start.AddArgument(server);
        //start.SetHandler(StartMcpServer, server);

        //stop.AddArgument(server);
        //stop.SetHandler(StopMcpServer, server);
    }

    private void ShowMCPData()
    {
        var shell = (Shell)Shell;
        var host = shell.Host;

        if (shell.McpManager.McpServers.Count is 0)
        {
            host.WriteErrorLine("No MCP server is available.");
            return;
        }

        host.RenderMcpServersAndTools(shell.McpManager);
    }
}
