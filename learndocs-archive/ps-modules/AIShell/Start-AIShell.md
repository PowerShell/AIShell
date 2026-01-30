---
external help file: AIShell.Integration.dll-Help.xml
Module Name: AIShell
online version: https://learn.microsoft.com/en-us/powershell/module/aishell/start-aishell?view=ps-modules&wt.mc_id=ps-gethelp
ms.date: 01/12/2026
ms.custom: 1.0.0-preview.6
schema: 2.0.0
ms.collection: ce-skilling-ai-copilot
---
# Start-AIShell

## SYNOPSIS
Starts an AIShell session in a split pane window of Windows Terminal and iTerm2 and connects a
communication channel to the PowerShell session that started it.

## SYNTAX

```
Start-AIShell [-Path <String>] [<CommonParameters>]
```

## DESCRIPTION

[!INCLUDE[aishell-deprecated](../../includes/aishell-deprecated.md)]

Starts an AIShell session in a split pane window of Windows Terminal and iTerm2. The AIShell session
is started in the right pane of the terminal window. The left pane is the current shell session. You
must use these windows to interact with the AIShell session.

When you run this cmdlet on macOS, it checks for the presence of iTerm2. If iTerm2 is not installed,
it attempts to install iTerm2 using the pip3 package manager. If iTerm2 is installed, it starts an
iTerm2 session in a split pane window.

## EXAMPLES

### Example 1 - Start an AIShell session

```powershell
Start-AIShell
```

### Example 2 - Start an AIShell session with a specific path

```powershell
Start-AIShell -PATH C:\Users\aish.exe
```

## PARAMETERS

### -Path

By default, the cmdlet looks for the `aish` executable in the locations listed in the `$env:PATH`
environment variable. Use this parameter to specify an alternate location for the `aish`
executable.

```yaml
Type: System.String
Parameter Sets: (All)
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### CommonParameters

This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable,
-InformationAction, -InformationVariable, -OutVariable, -OutBuffer, -PipelineVariable, -Verbose,
-WarningAction, and -WarningVariable. For more information, see
[about_CommonParameters](http://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

### None

## OUTPUTS

### System.Object

## NOTES

PowerShell includes the following alias for this cmdlet:

- All platforms:
  - `aish`

## RELATED LINKS

[Invoke-AIShell](Invoke-AIShell.md)

[Resolve-Error](Resolve-Error.md)

