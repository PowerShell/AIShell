---
description: This article explains how to install and configure AI Shell, and get started chatting with an AI assistant.
ms.date: 01/12/2026
title: Get started with AI Shell
ms.topic: get-started
ms.collection: ce-skilling-ai-copilot
---
# Get started with AI Shell

[!INCLUDE[aishell-deprecated](../../../includes/aishell-deprecated.md)]

AI Shell was created to help command line users find the right commands to use, recover from errors,
and better understand the commands and the output they produce. Follow along and walk through some
examples to get started with AI Shell.

## Starting AI Shell

Run the `aish` command in the shell of your choice. AI Shell starts in a new terminal window and
prompts you to choose an agent.

## Using AI Shell

If you plan to use the Azure OpenAI agent, you will have to configure it with your endpoint, API
keys, and system prompt before using it. You can do so by selecting the agent and then running
`/agent config`. Within the JSON config file that is opened you will have to provide your endpoint,
deployment name, model version and API key. You can configure the system prompt property to better
ground the model to your specific use cases, the default included is for a PowerShell expert.
Additionally if you wish you use OpenAI you can configure the agent with just your API key from
OpenAI in the commented out example in the JSON file.

![An animation showing Getting Started with AI Shell.][02]

The Azure agent is designed to bring the Azure Copilot experience directly to your command line. It
provides assistance for Azure CLI and Azure PowerShell commands. To use this agent, you need to sign
into Azure using the `az login` command from Azure CLI.

## Use AI Shell to interact with the agents

Use these sample queries with each agent.

Azure OpenAI Agent

- "How do I create a text file named helloworld in PowerShell?"
- "What is the difference between a switch and a parameter in PowerShell?"
- How do I get the top 10 most CPU intensive processes on my computer?

Azure Agent

- "How do I create a new resource group with Azure CLI?"
- "How can I list out the storage accounts I have in Azure PowerShell?"
- "What is Application Insights?"
- "How to create a web app with Azure CLI?"

### Switching Agents

You can switch between agents using the `@<agentName>` syntax in your chat messages. For example,

![An animation showing switching between two agents with the @ sign][02]

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

Since you are using it as a standalone executable, the `/code post` command will not work. It is
designed for the sidecar experience with PowerShell 7. See
[Get started with AI Shell in PowerShell][01] for more information.

### Key bindings for commands

AI Shell has key bindings for the `/code` command. They key bindings are currently hard-coded, but
custom key bindings will be supported in a future release.

|                  Key binding                  |     Command      |                     Description                     |
| --------------------------------------------- | ---------------- | --------------------------------------------------- |
| <kbd>Ctrl+d</kbd><kbd>Ctrl</kbd>+<kbd>c</kbd> | `/code copy`     | Copy _all_ the generated code snippets to clipboard |
| <kbd>Ctrl</kbd>+<kbd>\<n\></kbd>              | `/code copy <n>` | Copy the _n-th_ generated code snippet to clipboard |

<!-- link references -->
[01]: aishell-powershell.md
[02]: media/aishell-standalone/standalone-startup.gif

