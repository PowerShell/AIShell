---
external help file: AIShell.Integration.dll-Help.xml
Module Name: AIShell
online version: https://learn.microsoft.com/en-us/powershell/module/aishell/resolve-error?view=ps-modules&wt.mc_id=ps-gethelp
ms.date: 01/12/2026
ms.custom: 1.0.0-preview.6
schema: 2.0.0
ms.collection: ce-skilling-ai-copilot
---
# Resolve-Error

## SYNOPSIS
Sends the last error in the current session  for resolution in the connected AIShell window.

## SYNTAX

```
Resolve-Error [-Agent <String>] [-IncludeOutputFromClipboard] [<CommonParameters>]
```

## DESCRIPTION

[!INCLUDE[aishell-deprecated](../../includes/aishell-deprecated.md)]

When an error occurs in the current session, this cmdlet sends the error to the AIShell agent for
resolution. The command sends the full error object to the current AIShell agent session, which
attempts to provide a resolution.

Beginning with theAI Shell v1.0.0-preview.2 release, the `Resolve-Error` cmdlet also works with
error messages output from native commands.

## EXAMPLES

### Example 1 - Resolves the last error

```powershell
PS> Start-AIShell
#User receives an error

PS> Resolve-Error
```

This example shows how to ask AIShell to resolve the last error that occurred in the current AIShell
session. AIShell analyzes the error and attempts to provide a solution in the AIShell agent window.

## PARAMETERS

### -Agent

Specifies the agent to use in the current AIShell session. If not specified, AIShell uses the
currently selected agent.

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

### -IncludeOutputFromClipboard

When this parameter is specified, the output copied to the clipboard is included in the error sent
to AIShell.

```yaml
Type: System.Management.Automation.SwitchParameter
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
  - `fixit`

## RELATED LINKS

[Invoke-AIShell](Invoke-AIShell.md)

[Start-AIShell](Start-AIShell.md)

