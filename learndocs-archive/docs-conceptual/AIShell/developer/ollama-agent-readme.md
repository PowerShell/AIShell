---
title: Ollama agent README
description: Learn how to use the Ollama agent in AI Shell.
ms.date: 01/12/2026
ms.collection: ce-skilling-ai-copilot
---
# Ollama Plugin

[!INCLUDE[aishell-deprecated](../../../includes/aishell-deprecated.md)]

This agent is used to interact with a language model running locally by utilizing the Ollama API.
Before using this agent you need to have Ollama installed and running. To create an agent, you need
to implement the `IAgent` interface.

You can also use this example code as a template to create your own agent.

## Pre-requisites to using the agent

- Install [Ollama][01]
- Install a [Ollama model][02], we
  suggest using the `phi3` model as it's set as the default model in the code
- [Start the Ollama API server][03]

## Configuration

Currently to change the model you will need to modify the query in the code in the
`OllamaChatService` class. The default model is `phi3`.

The default endpoint is `http://localhost:11434/api/generate` with `11434` being the default port.
This can be changed in the code and eventually will be added to a configuration file.

There is an updated version of the Ollama agent available in the AI Shell repository. See the
[README][04] file for the Ollama plugin.

## Known Limitations

- There is no history shared across queries so the model will not be able to remember previous
  queries
- Streaming is currently not supported if you change the stream value to `true` in the data to send
  to the API it will not work

<!-- link references -->
[01]: https://github.com/ollama/ollama
[02]: https://github.com/ollama/ollama?tab=readme-ov-file#model-library
[03]: https://github.com/ollama/ollama?tab=readme-ov-file#start-ollama
[04]: https://github.com/PowerShell/AIShell/blob/v1.0.0-preview.2/shell/agents/AIShell.Ollama.Agent/README.md
