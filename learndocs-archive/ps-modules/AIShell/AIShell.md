---
Download Help Link: https://aka.ms/aishell-help
Help Version: 1.0.0
Locale: en-US
Module Guid: ecb8bee0-59b9-4dae-9d7b-a990b480279a
Module Name: AIShell
ms.date: 01/12/2026
ms.custom: 1.0.0-preview.6
title: AIShell Module
ms.collection: ce-skilling-ai-copilot
---
# AIShell Module

## Description

[!INCLUDE[aishell-deprecated](../../includes/aishell-deprecated.md)]

The AIShell module integrates PowerShell with AIShell. This integration enables the sharing queries,
errors, and results between PowerShell and AIShell.

## AIShell Cmdlets

### [Invoke-AICommand](Invoke-AICommand.md)

Used by an MCP in AI Shell to invoke a command in the connected PowerShell session.

### [Invoke-AIShell](Invoke-AIShell.md)

Sends a query to the connected AIShell window. Results are shown in the AIShell window.

### [Resolve-Error](Resolve-Error.md)

Sends the last error in the current session  for resolution in the connected AIShell window.

### [Start-AIShell](Start-AIShell.md)

Starts an AIShell session in a split pane window of Windows Terminal and iTerm2 and connects a
communication channel to the PowerShell session that started it.

