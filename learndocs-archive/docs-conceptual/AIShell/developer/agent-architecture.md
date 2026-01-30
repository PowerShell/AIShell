---
title: Architecture of AI Shell
description: This article explains the architecture of AI Shell and API required to support agents.
ms.date: 01/12/2026
ms.topic: concept-article
ms.collection: ce-skilling-ai-copilot
---
# AI Shell architecture

[!INCLUDE[aishell-deprecated](../../../includes/aishell-deprecated.md)]

![AI Shell architecture diagram.][01]

## AIShell.Abstraction

This project will be released as a NuGet package. It contains all the interfaces for defining an
agent plugin that interacts with AI Shell.

This abstraction layer includes:

- `IShell`: Represents a proxy of the AI shell.
- `IHost`: Represents a proxy of the shell host.
- `IRenderElement`: Represents the `header/value` or `label/value` pairs for rendering objects in
  table or list format.
- `IStreamRender`: Represents a special render for rendering streaming response.
- `ILLMAgent`: Represents an agent plugin.
- `IOrchestrator`: Derives `ILLMAgent`. Represents a special agent that can route a query to the
  most suitable agent.
- `ICodeAnalyzer`: Derives `ILLMAgent`. Represents a special agent that can analyze code for
  security concerns.
- `CommandBase`: represents a command that an agent can register to the shell when being loaded.

The most important interface method in `ILLMAgent` is `Task<bool> Chat(string input, IShell shell)`,
which will be called by the shell when a query comes from the user. It gives extreme flexibility to
the implementation of an agent. An agent can do whatever it wants targeting an arbitrary AI backend
and render the output using the utilities provided by `IShell`.

An agent plugin is responsible for managing its own chat history.

## AIShell.Kernel

This is the implementation of AI Shell. It consists of the following components:

- ReadLine
- Renders (markdown render, stream render, paging render)
- Plugin management
- Host (a range of utility methods for writing output and interactive prompting)
- Command runner and built-in commands
- Utilities (clipboard, tab completion, predictive intellisense)
  - Code execution for `python`, `powershell`, `cmd`, and `bash` (to enable agents to do function
    calling with LLM. **not-yet-started**)
- Shell integration (deep integration with the command-line shell. **not-yet-started**)
- Configuration (colors, key bindings, and etc. **not-yet-started**)

## AIShell.App

This is a thin wrapper over `AIShell.Kernel` to create an executable. The initial idea is to
make `AIShell.Kernel` a library, so it can be used or hosted by other applications. We can
easily merge `AIShell.App` into `AIShell.Kernel` if this idea no longer make sense.

<!-- link references -->
[01]: media/agent-architecture/aishell-agent-architecture.png

