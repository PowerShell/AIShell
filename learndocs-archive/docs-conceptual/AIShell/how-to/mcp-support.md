---
title: MCP support
description: Learn how to enable and use MCPs in AIShell.
ms.date: 01/12/2026
ms.topic: how-to
ms.collection: ce-skilling-ai-copilot
---
# MCP support

[!INCLUDE[aishell-deprecated](../../../includes/aishell-deprecated.md)]

Model Context Protocol (MCP) is an open protocol that standardizes how applications provide context
to large language models (LLMs). Starting in AI Shell 1.0.0-preview.6, AI Shell can act as an MCP
Host and client to MCP servers. The key participants in the MCP architecture are:

- MCP Host - AI Shell coordinates and manages one or multiple MCP clients
- MCP Client - The client component of AI Shell maintains a connection to an MCP server and obtains
  context from an MCP server for the MCP host to use
- MCP Server - A program, running locally or hosted remotely, that provides context to MCP clients

For more information about MCP, see the [Architecture Overview][06] in the official Model Context
Protocol documentation.

MCP tools enable AI agents to access external tools and services to enhance their capabilities and
provide more accurate responses. MCPs can integrate with various APIs, databases, and other
resources, allowing agents to retrieve real-time information and perform complex tasks.

## Built-in tools for AI Shell

AI Shell comes with a set of MCP-like built-in tools that are available to all agents. These
commands are similar to MCP Server tools, but are exclusive to the AI Shell experience. The tools
enhance the AI Shell experience by providing context-aware capabilities and automation features.
You can use them in conjunction with other MCP servers to create a powerful AI-driven shell
environment.

The following list contains the built-in tools and their usage:

- `get_working_directory` - Get the current working directory of the connected PowerShell session,
  including the provider name (e.g., `FileSystem`, `Certificate`) and the path (e.g., `C:\\`,
  `cert:\\`).
- `get_command_history` - Get up to 5 of the most recent commands executed in the connected
  PowerShell session.
- `get_terminal_content` - Get all output currently displayed in the terminal window of the
  connected PowerShell session.
- `get_environment_variables` - Get environment variables and their values from the connected
  PowerShell session. Values of potentially sensitive variables are redacted.
- `copy_text_to_clipboard` - Copy the provided text or code to the system clipboard, making it
  available for pasting elsewhere.
- `post_code_to_terminal` - Insert code into the prompt of the connected PowerShell session without
  executing it. The user can review and choose to run it manually by pressing Enter.
- `run_command_in_terminal` - This tool allows you to execute shell commands in a persistent
  PowerShell session, preserving environment variables, working directory, and other context across
  multiple commands.
- `get_command_output` - Get the output of a command previously started with
  `run_command_in_terminal`.

> [!NOTE]
> The built-in tools rely on the side-car experience with a connected PowerShell session and provide
> enhanced context awareness and automation capabilities.

## Demos

Here is a simple demo showing how you can have AI Shell run commands on your behalf using the
`run_command_in_terminal` tool:

![Have the MCP run a command in terminal for you.][09]

This example shows how additional context is provided to AI Shell to improve results:

![Getting more context with built-in tools.][07]

You can also use the `get_terminal_content` tool to get the content from the connected terminal and
provide it to AI Shell to help it understand what you are trying to do:

![Getting content from the terminal for output created before AI Shell started.][08]

## Find and configure MCP servers

An MCP server is an application that runs locally or is hosted remotely and accessed through an
endpoint URL. Many MCP servers are open source and available for free. Local MCP server apps are
typically built using Node.js and can installed using `npm`. You can find a list of available MCP
servers at [Awesome MCP Servers][03].

Before you can use an MCP server (Local or remote) you must add it to the `mcp.json` configuration
file. In some cases, you might not need to install the local MCP before using it. However, the MCP
server will be installed the first time you start AI Shell. This can take several minutes. For the
best experience, preinstall the MCP server. Consult the documentation for your specific MCP server
for installation instructions. Remote MCP servers don't need to be installed. You just need to add
them to the `mcp.json` file.

### Install a local MCP server

The following example installs the `@modelcontextprotocol/server-everything` and
`@modelcontextprotocol/server-filesystem` MCP servers. These servers are published as node
packages. You must have `Node.js` installed to use these MCP servers.

To install the MCP servers, you can use the following commands:

```powershell
npm install -g @modelcontextprotocol/server-everything
npm install -g @modelcontextprotocol/server-filesystem
```

For more information about their command and capabilities, see the following documentation:

- [Everything MCP Server][04]
- [Filesystem MCP Server][05]

### Add MCP Servers

To add an MCP server, create an `mcp.json` file in `$HOME\.aish\` folder. The following example
shows two local MCP servers, `everything` and `filesystem`, and two remote MCP server. You can add
as many MCP servers as you want.

```json
{
    "servers": {
        "everything": {
            "type": "stdio",
            "command": "npx",
            "args": [
                "-y",
                "@modelcontextprotocol/server-everything"
            ]
        },
        "filesystem": {
            "type": "stdio",
            "command": "npx",
            "args": [
                "-y",
                "@modelcontextprotocol/server-filesystem",
                "C:/Users/username/"
            ]
        },
        "github": {
            "type": "http",
            "url": "https://api.githubcopilot.com/mcp/",
            "headers": {
                "Authorization": "Bearer <YOUR_GITHUB_TOKEN>"
            }
        },
        "microsoft.docs.mcp": {
            "type": "http",
            "url": "https://learn.microsoft.com/api/mcp"
        }
    }
}
```

For more information about the remote MCP server from Microsoft, see:

- [GitHub's official MCP Server][02]
- [Microsoft Learn MCP Server][01]

<!-- link references -->
[01]: /training/support/mcp
[02]: https://github.com/github/github-mcp-server
[03]: https://mcpservers.org/
[04]: https://mcpservers.org/servers/modelcontextprotocol/everything
[05]: https://mcpservers.org/servers/modelcontextprotocol/filesystem
[06]: https://modelcontextprotocol.io/docs/learn/architecture
[07]: media/mcp-support/openai-agent-context.gif
[08]: media/mcp-support/openai-agent-get.gif
[09]: media/mcp-support/openai-agent-run.gif

