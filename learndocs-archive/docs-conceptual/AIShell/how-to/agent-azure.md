---
title: Azure Copilot Agent
description: Learn how to use the Azure Copilot agent in AI Shell.
ms.date: 01/12/2026
ms.collection: ce-skilling-ai-copilot
---
# Azure Copilot Agent

[!INCLUDE[aishell-deprecated](../../../includes/aishell-deprecated.md)]

This agent is designed to connect you to the [**Azure Copilot**][02] experience directly from your
command line. It provides assistance for Azure CLI commands, Azure PowerShell commands, and general
Azure knowledge. To use this agent, you need to sign in to Azure using the `az login` command from
Azure CLI.

## Prerequisites

- Windows 11 21H2 or higher
- Windows Terminal v1.19 or higher
- [Azure CLI][01] version 2.30.0 or higher installed
- [Azure PowerShell][01] version 14.0.0 or higher installed
- A valid access token for your Azure tenant created by `az login` or `Connect-AzAccount`

## Sample Questions

- "How do I create a new resource group with Azure CLI?"
- "How can I list out the storage accounts I have in Azure PowerShell?"
- "What is Application Insights?"
- "How to create a web app with Azure CLI?"

## Agent features

This agent includes the following features:

- Supports authentication using either the `Connect-AzAccount` command from Azure PowerShell or the
  `az login` command from Azure CLI. Run these commands before starting AI Shell to ensure the token
  cache has a valid access token.
- The `/replace` command supports Azure PowerShell. The agent walks you through replacement of
  parameter values in generated Azure PowerShell responses.

## Telemetry and configuration

The **Azure Copilot** agent captures a small set of telemetry data. The telemetry data captured
contains no personal identifiable information. The agent only captures product usage data to help us
improve the product experience.

When you allow telemetry, the agent only collects the following information:

- The `/` command that you used
- The number of questions asked
- The type of operating system used
- Any exceptions encountered during usage
- The details provided using the `/like` or `/dislike` commands

You can disable this telemetry by modifying the **telemetry** property in the configuration file.
The Azure agent configuration is stored in a JSON file. You can view and edit the JSON config file
by using the `/agent config` command in AI Shell. The available settings are:

```json
{
  "logging": true,
  "telemetry": true
}
```

When logging is enabled, the agent writes logs in the `~/.aish/agent-config/azure` directory. To
disable logging, set the **logging** property to `false`.

To disable telemetry, set the **telemetry** property to `false`.

<!-- link references -->
[01]: /cli/azure/install-azure-cli
[02]: https://azure.microsoft.com/products/copilot

