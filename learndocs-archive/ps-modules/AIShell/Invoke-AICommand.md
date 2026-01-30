---
external help file: AIShell.Integration.dll-Help.xml
Module Name: AIShell
ms.custom: 1.0.0-preview.6
ms.date: 01/12/2026
online version: https://learn.microsoft.com/en-us/powershell/module/aishell/invoke-aicommand?view=ps-modules&wt.mc_id=ps-gethelp
schema: 2.0.0
ms.collection: ce-skilling-ai-copilot
---
# Invoke-AICommand

## SYNOPSIS

Used by an MCP in AI Shell to invoke a command in the connected PowerShell session.

## SYNTAX

```
Invoke-AICommand [-Command] <ScriptBlock> [<CommonParameters>]
```

## DESCRIPTION

[!INCLUDE[aishell-deprecated](../../includes/aishell-deprecated.md)]

MCPs in AI Shell use this cmdlet to invoke a command in the connected PowerShell session. This
command is not intended for users to directly invoke. When an AI model creates a result containing
commands, the MCP can use this cmdlet to execute the commands in the connected PowerShell session.

## EXAMPLES

## PARAMETERS

### -Command

The scriptblock to be executed.

```yaml
Type: System.Management.Automation.ScriptBlock
Parameter Sets: (All)
Aliases:

Required: True
Position: 0
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

## RELATED LINKS

