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
        var mcpManager = shell.McpManager;

        if (mcpManager.McpServers.Count is 0 && mcpManager.BuiltInTools is null)
        {
            host.WriteErrorLine("No MCP server is available.");
            return;
        }

        host.RenderMcpServersAndTools(shell.McpManager);
    }
}
