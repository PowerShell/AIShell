---
external help file: AIShell.Integration.dll-Help.xml
Module Name: AIShell
online version: https://learn.microsoft.com/en-us/powershell/module/aishell/invoke-aishell?view=ps-modules&wt.mc_id=ps-gethelp
ms.date: 01/12/2026
ms.custom: 1.0.0-preview.6
schema: 2.0.0
ms.collection: ce-skilling-ai-copilot
---
# Invoke-AIShell

## SYNOPSIS
Sends a query to the connected AIShell window. Results are shown in the AIShell window.

## SYNTAX

### Default (Default)

```
Invoke-AIShell -Query <String[]> [-Agent <String>] [-Context <PSObject>] [<CommonParameters>]
```

### Clipboard

```
Invoke-AIShell -Query <String[]> [-Agent <String>] [-ContextFromClipboard] [<CommonParameters>]
```

### PostCode

```
Invoke-AIShell [-PostCode] [<CommonParameters>]
```

### CopyCode

```
Invoke-AIShell [-CopyCode] [<CommonParameters>]
```

### Exit

```
Invoke-AIShell [-Exit] [<CommonParameters>]
```

## DESCRIPTION

[!INCLUDE[aishell-deprecated](../../includes/aishell-deprecated.md)]

This cmdlet sends a query to the open AIShell agent and results are shown in the AIShell window.

## EXAMPLES

### Example 1 - Send a query to the AIShell agent

```powershell
Start-AIShell
Invoke-AIShell -Query "How do I list out the 5 most CPU intensive processes?"
```

This example sends a query, "How do I list out the 5 most CPU intensive processes?" to the AIShell
agent. Responses are given in the AIShell window.

## PARAMETERS

### -Agent

Specifies the agent to use in the current AIShell session. If not specified, AIShell uses the
currently selected agent.

```yaml
Type: System.String
Parameter Sets: Default, Clipboard
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Context

Additional context information you want to send to the AIShell agent.

```yaml
Type: System.Management.Automation.PSObject
Parameter Sets: Default
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: True (ByValue)
Accept wildcard characters: False
```

### -ContextFromClipboard

Use the content in your clipboard as context information for the AIShell agent.

```yaml
Type: System.Management.Automation.SwitchParameter
Parameter Sets: Clipboard
Aliases:

Required: True
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -CopyCode

Invokes `/code copy` command in the AIShell sidecar session. This command copies the code in the
AIShell sidecar session to the clipboard.

```yaml
Type: System.Management.Automation.SwitchParameter
Parameter Sets: CopyCode
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Exit

Invokes `/exit` command in the AIShell sidecar session. This command closes the AIShell
sidecar session.

```yaml
Type: System.Management.Automation.SwitchParameter
Parameter Sets: Exit
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -PostCode

Invokes `/code post` command in the AIShell sidecar session. This command posts the code in the
AIShell sidecar session to your PowerShell session.

```yaml
Type: System.Management.Automation.SwitchParameter
Parameter Sets: PostCode
Aliases:

Required: False
Position: Named
Default value: None
Accept pipeline input: False
Accept wildcard characters: False
```

### -Query

The user input to send to the AIShell agent.

```yaml
Type: System.String[]
Parameter Sets: Default, Clipboard
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

### System.Management.Automation.PSObject

## OUTPUTS

### System.Object

## NOTES

PowerShell includes the following alias for this cmdlet:

- All platforms:
  - `askai`

## RELATED LINKS

[Start-AIShell](Start-AIShell.md)

[Resolve-Error](Resolve-Error.md)

