---
title: What is a command shell?
ms.date: 01/12/2026
description: Learn what a command shell is and how it works.
ms.collection: ce-skilling-ai-copilot
---
# What is a command shell?

[!INCLUDE[aishell-deprecated](../../../includes/aishell-deprecated.md)]

A command shell is a text-based interface for interacting with a computer, also known as a
Read-Eval-Print Loop ([REPL][15]).

A shell takes input from the keyboard, evaluates that input, and executes the input as a shell
command or gives the input to the operating system to be executed. Most shells can also read
commands from a script file, and may include programming features like variables, flow control, and
functions.

## Terminals

A terminal is an application that provides a text-based interface for hosting command shells. Some
terminals are designed to work with a specific shell, while others can host multiple shells. They
may also include advanced features such as:

- Ability to create multiple panes within a single window
- Ability to create multiple tabs to host multiple shells
- Ability to change color schemes and fonts
- Support for copy and paste operations

The following list contains some examples of terminal applications:

- [Windows Terminal][11] - a modern terminal application for Windows that can host multiple shells.
- [Windows Console Host][10] - the default host application on Windows for text-based applications.
  It can also host the Windows Command Shell or PowerShell.
- [Terminal for macOS][14] - the default terminal application on macOS that can host the bash or zsh
  shell.
- [iTerm2 for macOS][12] - a popular 3rd-party terminal application for macOS.
- [Azure Cloud Shell][02] - a browser-based terminal application that's hosted in Microsoft Azure.
  Azure Cloud shell gives you the choice of using bash or PowerShell and come preconfigured with
  many command-line tools to manage Azure resources.

## General purpose command shells

General purpose command shells are designed to work with the operating system. These shell allow you
to run any command that the operating system supports. They also include shell-specific commands and
programming features. The following list contains some examples of general purpose command shells:

- [PowerShell][05]
- [Windows Command Shell][07]
- [bash][16] - popular on Linux
- [zsh][13] - popular on macOS

## Utility command shells

Utility command shells are designed to work with specific applications or services. These shells can
only run commands that are specific to the application or service. Some utility shells support
running commands from a script file, but they don't include programming features. Usually, these
shells can only be used interactively.

- [AI Shell][01] - An interactive-only shell used to communicate with AI services such as Azure
  OpenAI.
- [netsh][08] - Network shell (netsh) is a command-line utility that allows you to configure and
  display the status of various network components on Windows. It is both a command-line tool and a
  command shell. It also supports running commands from a script file.

## Command-line tools

A command-line tool is a standalone program that's run from a command shell. Command-line tools are
typically designed to perform a specific task, such as managing files, configuring settings, or
querying for information. Command-line tools can be used in any shell that supports running external
programs.

- [Azure CLI][03] - a collection of command-line tools for managing Azure resources that can be run
  in any supported shell.
- [Azure PowerShell][04] - a collection of PowerShell modules for managing Azure resources that can
  be run in any supported version of PowerShell.
- [OpenSSH for Windows][06] - a command-line client, as well as a server, for secure communication
  over a network.
- [Windows Commands][09] - a collection of command-line tools that are built into Windows.

In general, command-line tools don't provide a command shell (REPL) interface. The `netsh` command
in Windows is an exception, as it's both a command-line tool and an interactive command shell.

<!-- link references -->
[01]: ../overview.md
[02]: /azure/cloud-shell/overview
[03]: /cli/azure
[04]: /powershell/azure
[05]: /powershell/scripting/overview
[06]: /windows-server/administration/openssh/openssh-overview
[07]: /windows-server/administration/windows-commands/cmd
[08]: /windows-server/administration/windows-commands/netsh
[09]: /windows-server/administration/windows-commands/windows-commands
[10]: /windows/console/consoles
[11]: /windows/terminal
[12]: https://iterm2.com/
[13]: https://support.apple.com/102360
[14]: https://support.apple.com/guide/terminal/welcome/mac
[15]: https://wikipedia.org/wiki/Read%E2%80%93eval%E2%80%93print_loop
[16]: https://www.gnu.org/software/bash/
