---
title: Install AI Shell
description: Learn how to install AI Shell on your system.
ms.date: 01/12/2026
ms.topic: install-set-up-deploy
ms.collection: ce-skilling-ai-copilot
---
# Install AI Shell

[!INCLUDE[aishell-deprecated](../../includes/aishell-deprecated.md)]

AI Shell is an interactive shell that provides a chat interface for AI language models. For the best
experience, install the following packages:

- The command-line shell (`aish`) interface
- The **AIShell** module for PowerShell

This article explains how to install these packages on your system.

## System requirements

AI Shell is supported on the following platforms:

<!-- markdownlint-disable MD023 MD024 MD051 -->
### [Windows](#tab/windows)

- Windows 10 or higher
- PowerShell 7.4.6 or higher
- Windows Terminal
- PSReadLine v2.4.2-beta2 or higher

### [macOS](#tab/macos)

- macOS v13 Ventura or higher
- PowerShell 7.4.6 or higher
- Default macOS Terminal app for the standalone experience
- iTerm2 required to use the sidecar experience
  - Python 3.11 or higher required to support iTerm2
  - Must enable **Python APIs** in the iTerm2 settings
- PSReadLine v2.4.2-beta2 or higher

### [Linux](#tab/linux)

- Ubuntu 20.04 or higher
- PowerShell 7.4.6 or higher
- PSReadLine v2.4.2-beta2 or higher
- Any terminal emulator supported by the OS (no sidecar experience)

  > [!NOTE]
  > The AI Shell module isn't supported on Linux.

<!-- markdownlint-enable MD023 MD024 MD051 -->

---

## Install AI Shell

For convenience, you can use `installaishell.ps1` script to install AI Shell.

On Windows, this script:

- Installs `aish.exe` to `$env:LOCALAPPDATA\Programs\AIShell` and adds it to your PATH
- Installs the **AIShell** module to your module path location

On macOS, this script:

- Installs the `aish` executable to `/usr/local/AIShell` and creates a symbolic link to
  `/usr/local/bin/aish`.

> [!NOTE]
> This script only works on Windows and Mac systems. Linux users need to follow the manual
> installation instructions.

```powershell
Invoke-Expression "& { $(Invoke-RestMethod 'https://aka.ms/install-aishell.ps1') }"
```

To uninstall AI Shell, invoke the same expression with the `-Uninstall` parameter.

```powershell
Invoke-Expression "& { $(Invoke-RestMethod 'https://aka.ms/install-aishell.ps1') } -Uninstall"
```

To manually install AI Shell, follow the instructions for your platform:

<!-- markdownlint-disable MD023 MD024 MD051 -->
### [Windows](#tab/windows)

1. Download the latest version from the [GitHub releases page][03]. Choose the file that matches
   your system architecture. For example, `AIShell-1.0.0-preview.2-win-x64.zip`.
1. Extract the contents of the ZIP file to a location on your system.
1. Add the extracted folder to your **PATH** environment variable.
1. Install the AI Shell module from the PowerShell Gallery.

   ```powershell
   Install-PSResource -Name AIShell -Preview
   ```

### [macOS](#tab/macos)

1. Download the latest version from the [GitHub releases page][03]. Choose the file that matches
   your system architecture. For example, `AIShell-1.0.0-preview.2-osx-x64.tar.gz`.
1. Extract the contents of the TAR file to a location on your system.
1. Add the extracted folder to your **PATH** environment variable.
1. Install the AI Shell module from the PowerShell Gallery.

   ```powershell
   Install-PSResource -Name AIShell -Preview
   ```

### [Linux](#tab/linux)

1. Download the latest version from the [GitHub releases page][03]. Choose the file that matches
   your system architecture. For example, `AIShell-1.0.0-preview.2-linux-x64.tar.gz`.
1. Extract the contents of the TAR file to a location on your system.
1. Add the extracted folder to your **PATH** environment variable.

<!-- markdownlint-enable MD023 MD024 MD051 -->

---

## Next steps

- [Get started with AI Shell][02]
- [Get started with AI Shell for PowerShell][01]

<!-- link references -->
[01]: get-started/aishell-powershell.md
[02]: get-started/aishell-standalone.md
[03]: https://github.com/PowerShell/AIShell/releases/latest

