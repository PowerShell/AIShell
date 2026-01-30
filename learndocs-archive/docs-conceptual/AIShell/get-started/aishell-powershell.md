---
description: This article explains how to install and configure AI Shell, and get started chatting with an AI assistant in PowerShell.
ms.date: 01/12/2026
title: Get started with AI Shell in PowerShell
ms.topic: get-started
ms.collection: ce-skilling-ai-copilot
---
# Get started with AI Shell in PowerShell

[!INCLUDE[aishell-deprecated](../../../includes/aishell-deprecated.md)]

AI Shell was created to help command line users find the right commands to use, recover from errors,
and better understand the commands and the output they produce. Follow along and walk through some
examples to get started with AI Shell.

## Starting AI Shell

Use the `Start-AIShell` command in the **AI Shell** module to open the sidecar experience in Windows
Terminal. When AI Shell starts, it prompts you to choose an agent.

![An animation showing Getting Started with AI Shell.][05]

## Using AI Shell

Before you can use the Azure OpenAI agent, you must create a configuration that includes your
endpoint, API keys, and system prompt. Start AI Shell, select the agent, and run `/agent config`.
Within the JSON config file that is opened you will have to provide your endpoint, deployment name,
model version and API key. You can configure the system prompt property to better ground the model
to your specific use cases, the default included is for a PowerShell expert. Additionally if you
wish you use OpenAI you can configure the agent with just your API key from OpenAI in the commented
out example in the JSON file.

The Azure agent is designed to bring the Azure Copilot experience directly to your command line. It
provides assistance for Azure CLI and Azure PowerShell commands. To use this agent, you need to sign
into Azure using the `az login` command from Azure CLI.

## Use AI Shell to interact with the agents

Use these sample queries with each agent.

Azure OpenAI Agent

- "How do I create a text file named helloworld in PowerShell?"
- "What is the difference between a switch and a parameter in PowerShell?"
- How do I get the top 10 most CPU intensive processes on my computer?

Azure Copilot Agent

- "How do I create a new resource group with Azure CLI?"
- "How can I list out the storage accounts I have in Azure PowerShell?"
- "What is Application Insights?"
- "How to create a web app with Azure CLI?"

Here's a quick demo showing the Azure Agent in action:

![An animation showing Azure Agent in action.][01]

### Switching Agents

You can switch between agents using the `@<agentName>` syntax in your chat messages. For example,

![An animation showing switching between two agents with the @ sign][06]

You can also use a chat command to switch agents. For example, to switch to the `openai-gpt` agent,
use `/agent use openai-gpt`.

### Chat commands

By default, `aish` provides a base set of chat commands used to interact with the AI model. To get a
list of commands, use the `/help` command in the chat session.

```
  Name       Description                                      Source
──────────────────────────────────────────────────────────────────────
  /agent     Command for agent management.                    Core
  /cls       Clear the screen.                                Core
  /code      Command to interact with the code generated.     Core
  /dislike   Dislike the last response and send feedback.     Core
  /exit      Exit the interactive session.                    Core
  /help      Show all available commands.                     Core
  /like      Like the last response and send feedback.        Core
  /refresh   Refresh the chat session.                        Core
  /render    Render a markdown file, for diagnosis purpose.   Core
  /retry     Regenerate a new response for the last query.    Core
```

### Inserting code

When chatting with the agent, you can use the `/code post` command to automatically insert
the code from the response into the working shell. This is the simplest way to quickly get the code
you need to run in your shell. You can also use the hot key <kbd>Ctrl</kbd>+<kbd>d</kbd>,
<kbd>Ctrl</kbd>+<kbd>d</kbd> to insert the code into the working shell.

![An animation showing Inserting Code with AI Shell.][02]

### Key bindings for commands

AI Shell has key bindings for the `/code` command. They key bindings are currently hard-coded, but
custom key bindings will be supported in a future release.

|                       Key bindings                       |     Command      |                            Functionality                            |
| -------------------------------------------------------- | ---------------- | ------------------------------------------------------------------- |
| <kbd>Ctrl+d</kbd><kbd>Ctrl</kbd>+<kbd>c</kbd>            | `/code copy`     | Copy _all_ the generated code snippets to clipboard                 |
| <kbd>Ctrl</kbd>+<kbd>\<n\></kbd>                         | `/code copy <n>` | Copy the _n-th_ generated code snippet to clipboard                 |
| <kbd>Ctrl</kbd>+<kbd>d</kbd><kbd>Ctrl</kbd>+<kbd>d</kbd> | `/code post`     | Post _all_ the generated code snippets to the connected application |
| <kbd>Ctrl</kbd>+<kbd>d</kbd><kbd>\<n\></kbd>             | `/code post <n>` | Post the _n-th_ generated code snippet to the connected application |

Additionally, you can switch between the panes easier using the following keyboard shortcuts.

|             Key bindings             |                 Functionality                 |
| ------------------------------------ | --------------------------------------------- |
| <kbd>Alt</kbd>+<kbd>RightArrow</kbd> | Moves your cursor to the right AI Shell pane  |
| <kbd>Alt</kbd>+<kbd>LeftArrow</kbd>  | Moves your cursor to the left PowerShell pane |

### Resolving Errors

If you encounter an error in your working terminal, you can use the `Resolve-Error` cmdlet to send
that error to the open AI Shell window for resolution. This command asks the AI model to help you
resolve the error.

![An animation showing Resolving Errors with AI Shell.][04]

### Invoking AI Shell

You can use the `Invoke-AIShell` cmdlet to send queries to the current agent in the open AI Shell window.
This command allows you to interact with the AI model from your working terminal.

![An animation using Invoke-AIShell.][03]

<!-- link references -->
[01]: media/aishell-powershell/azure-agent.gif
[02]: media/aishell-powershell/insert-code.gif
[03]: media/aishell-powershell/invoke-aishell.gif
[04]: media/aishell-powershell/resolve-error.gif
[05]: media/aishell-powershell/start-aishell.gif
[06]: media/aishell-powershell/switch-agents.gif

