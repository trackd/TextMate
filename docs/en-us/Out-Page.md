---
external help file: PSTextMate.dll-Help.xml
Module Name: TextMate
online version: https://github.com/trackd/TextMate/blob/main/docs/en-us
schema: 2.0.0
---

# Out-Page

## SYNOPSIS

Displays pipeline content in the interactive pager.

## SYNTAX

### (All)

```powershell
Out-Page [-InputObject] <psobject> [<CommonParameters>]
```

## ALIASES

This cmdlet has the following aliases,
 None

## DESCRIPTION

Out-Page collects pipeline input and opens an interactive pager view.
Renderable values are shown directly; other values are formatted through `Out-String -Stream`
and displayed line-by-line.

The pager supports keyboard navigation for scrolling and paging through large output.

## EXAMPLES

### Example 1

Example: page output from a TextMate formatter cmdlet

```powershell
Get-Content .\src\PSTextMate\Cmdlets\OutPage.cs -Raw | Format-CSharp | Out-Page
```

### Example 2

Example: capture and pipe a `HighlightedText` object directly

```powershell
$highlighted = Get-Content .\README.md -Raw | Format-Markdown
$highlighted | Out-Page
```

### Example 3

Example: page PwshSpectreConsole renderables

```powershell
Import-Module PwshSpectreConsole
$num = $host.ui.RawUI.WindowSize.Height - 5
1..$num | 
  ForEach-Object { 
    $randomColor = [Spectre.Console.Color].GetProperties().Name | Get-Random
    $value = Get-Random -Minimum 10 -Maximum 100
    New-SpectreChartItem -Label "Item $_" -Value $value -Color $randomColor 
  } |
 Format-SpectreBarChart | 
 Out-Page
```

### Example 4

Example: page regular `Out-String` content

```powershell
Get-ChildItem -Recurse | Out-String -Stream | Out-Page
```

## PARAMETERS

### -InputObject

Input to display in the pager. Accepts renderables, strings, or general objects from the pipeline.

```yaml
Type: PSObject
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
 Position: 0
 IsRequired: true
 ValueFromPipeline: true
 ValueFromPipelineByPropertyName: false
 ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### CommonParameters

This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable,
-InformationAction, -InformationVariable, -OutBuffer, -OutVariable, -PipelineVariable,
-ProgressAction, -Verbose, -WarningAction, and -WarningVariable. For more information, see
[about_CommonParameters](https://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

### PSObject

Accepts any pipeline object. Renderables are used directly; non-renderables are formatted into text lines for paging.

## OUTPUTS

### System.Void

This cmdlet writes to the interactive pager and does not emit pipeline output.

## NOTES

Use `q` or `Esc` to exit the pager. Arrow keys, PageUp/PageDown, Spacebar, Home, and End are supported for navigation.

## RELATED LINKS

See also `Format-TextMate`, `Format-CSharp`, `Format-Markdown`, and `Format-PowerShell`.
