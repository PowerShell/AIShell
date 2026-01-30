---
title: AI Shell release notes
description: >
  Find out what's new in the latest release of AI Shell, an interactive shell that provides a chat interface with language models.
ms.date: 01/12/2026
ms.topic: overview
ms.collection: ce-skilling-ai-copilot
---
# AI Shell Release Notes

[!INCLUDE[aishell-deprecated](../../includes/aishell-deprecated.md)]

This document outlines the changes and improvements made in each release of AI Shell. For a more
complete list of changes, refer to the [Releases page][06] on GitHub.

## 1.0.0-preview.7 - 2025-09-05

This release includes the following changes:

- Move to Azure.Identity v1.14.2 and refactor the telemetry library (#404)
- Use login shell on macOS to start `aish` in sidecar pane to inherit proper PATH (#403)
- Properly escape tool description to avoid malformed Markup object (#408)
- Add gpt-5 models to the supported model list (#409)
- Update the history file name and exclude the environment and managed-identity credentials from
  Azure authentication flow (#412)
- Keep the original casing of model name for custom endpoint to enable Foundry Local (#413)

## [1.0.0-preview.6][02] - 2025-07-24

<a id="1.0.0-preview.6"></a>

This release includes the following changes:

- Update AppInsights connection string to use the new prod environment (#390)
- Make AIShell an MCP client to expose MCP tools to its agents (#392)
- Add built-in tools to AIShell (#394)
- Fix a null-reference exception bug when built-in tools are not available (#396)
- Improve `Resolve-Error` command and allow default system prompt for the `openai-gpt` agent (#397)
- Add the built-in tool `run_command_in_terminal` for AI to execute commands in the connected
  PowerShell session (#398) - This tool is currently only enabled on Windows.

## [1.0.0-preview.5][08] - 2025-06-13

This release is a security patch only, including the following changes:

- Upgrade to .NET SDK 8.0.411 to address the .NET security issue [CVE-2025-30399][362]: .NET Remote
  Code Vulnerability
- `OpenAI` agent: update `DefaultAzureCredential` to allow `InteractiveBrowserCredential`
  (#383)

## [1.0.0-preview.4][04] - 2025-05-15

This release includes the following changes:

- Support posting code from the sidecar AIShell to PowerShell with `Invoke-AIShell -PostCode` (#361)
- Improve the reliability of `Start-AIShell` on macOS (#362)
- Publish NuGet package and PowerShell module in deploy box release (#365)
- Fix code posting on macOS: support posting code by both running `/code post` from the sidecar
  AIShell and running `Invoke-AIShell -PostCode` from PowerShell (#366)
- Update model information to support the new OpenAI models (#368)
- Add `/clear` as an alias to the command `/cls` to clear console in AIShell (#370)
- Ignore the current active agent from the agent completion results for the `@` operator (#372)
- Update installation script to install the AIShell module on macOS too (#374)
- Enhanced model management and system prompt integration in OllamaAgent (#351)
- Adding the Phi Silica agent for "Copilot+PC" devices (#373)
- Use deploy box and `GitHubRelease` task to create the GitHub draft release (#379)
- Make sure the `Runspace` is available when importing the `AIShell` module and throw otherwise
  (#379)

## [1.0.0-preview.3][07] - 2025-03-12

This release includes the following changes:

- Update flight flags and make corresponding changes to the `azure` agent (#349, #355)
- Update the regex for matching single-quote and double-quote strings for `PowerShell` and `Bash`
  syntax (#357)
- Add support for Entra ID authentication when using the `interpreter` or `openai-gpt` agents (#356)
- Update `install-aishell.ps1` to allow a user to specify the version to install (#345)

## [1.0.0-preview.2][01] - 2025-02-26

This release includes the following changes:

- Check and remove execute permission from the config file (#317)
- Use `nano` or `$EDITOR` (if defined) to open the config file on Linux (#318)
- Refactor the `openai-gpt` agent to move to `Azure.AI.OpenAI` v2.1.0 (#328)
- Add support to Azure PowerShell login credential (#329)
- Allow using 3rd party AI services that are compatible with OpenAI API format in the `openai-gpt`
  agent (#331)
- Check for update before return the description for `openai-gpt` agent (#332)
- Use `#7a7a7a` as the grey color in AIShell to meet the contrast requirement (#333)
- Remove the fallback logic for authorization check and stick to the production URL (#334)
- Capture native command output using screen scraping API on Windows (#335)
- Add `shell` as an alias to the `Bash` parser (#336)
- Enable `pluginstore` and update the topic name for CLI handler (#337)
- Log the response text if exception is thrown while processing a user query (#338)
- Enable parameter injection for Azure PowerShell response (#339)
- Fix build to preserve file permission for Linux and macOS packages (#344)
- Fix AzCLI command parsing to handle long/short flags and when no parameter is present (#344)
- Implement conversation context and streaming for the `Ollama` agent with `OllamaSharp` (#310)
- Adding docs and files for deploying an Azure OpenAI instance via Bicep file (#324)
- Modify readme and agent to point to docs (#326)

## [1.0.0-preview.1][03] - 2024-11-15

AI Shell is a new CLI tool that creates an interactive shell to connect you with different
artificial intelligence assistants. We refer to these different AI assistants as AI agents; AI Shell
includes two agents by default:

- Azure Copilot
- Azure OpenAI

### Key features

- Interactive chat to talk to AI agents
- Rendering of markdown responses
- Chat `/` commands to interact with the code responses from the AI agent of choice

<!-- link references -->
[01]: https://devblogs.microsoft.com/powershell/ai-shell-preview-2/
[02]: https://devblogs.microsoft.com/powershell/preview-6-ai-shell/
[03]: https://devblogs.microsoft.com/powershell/announcing-the-public-preview-of-ai-shell/
[04]: https://devblogs.microsoft.com/powershell/preview-4-ai-shell/
[06]: https://github.com/PowerShell/AIShell/releases
[07]: https://github.com/PowerShell/AIShell/releases/tag/v1.0.0-preview.3
[08]: https://github.com/PowerShell/AIShell/releases/tag/v1.0.0-preview.5
[362]: https://github.com/dotnet/announcements/issues/362

