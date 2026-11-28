---
external help file: PSTextMate.dll-Help.xml
Module Name: TextMate
online version: https://github.com/trackd/TextMate/blob/main/docs/en-us
schema: 2.0.0
---

# Format-PowerShell

## SYNOPSIS

Renders PowerShell code using TextMate grammars and returns a PSTextMate.Core.HighlightedDocument result for display or programmatic use.

## SYNTAX

### (All)

```
Format-PowerShell [-InputObject] <psobject> [-Theme <string>] [-LineNumbers] [<CommonParameters>]
```

## ALIASES

This cmdlet has the following aliases,
  fps

## DESCRIPTION

Format-PowerShell highlights PowerShell source and script files. Input can be provided as pipeline text or via file contents. The resulting `HighlightedDocument` can be used with console renderers or further processed. Use `-Theme` and `-LineNumbers` to adjust output.

## EXAMPLES

### Example 1: Highlight a short PowerShell snippet

```
'Get-Process | Where-Object { $_.CPU -gt 1 }' | Format-PowerShell
```

Highlights the snippet as source text without executing it.

### Example 2: Highlight a script file with line numbers

```
Get-Content .\scripts\deploy.ps1 -Raw | Format-PowerShell -LineNumbers
```

Displays the script with a line-number gutter.

### Example 3: Pipe FileInfo object and render with theme

```
Get-ChildItem .\scripts\*.ps1 | Format-PowerShell -Theme Monokai
```

Reads each matching file and highlights it using the Monokai theme.

## PARAMETERS

### -Page

Opens the built-in TextMate pager instead of returning output. Navigation, search and terminal lifecycle are independent of OutputFormat. Redirected output is written as plain text.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Width

Caps the layout width in terminal columns for every output format. Like a Spectre renderable, Text and native output are laid out when they are displayed, at the width then available (the console window width, or the width of a native container); -Width only narrows that width. When omitted, Text output reflows to the current window, and HTML output keeps each source line unwrapped. With -LineNumbers, wrapped rows keep the source line's number and leave the gutter of continuation rows blank.

```yaml
Type: System.Int32
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -OutputFormat

Selects Text (default), Spectre, Fullscreen, or Html. Html returns an escaped HTML fragment as a string. Text returns a source-preserving HighlightedDocument displayed directly as ANSI or plain text. Spectre and Fullscreen return native framework objects. TextMate ships their lightweight adapters and automatically imports an installed PwshSpectreConsole or fullscreen module when explicitly requested. A build with -Renderer can bundle the optional UI runtime instead. Every format uses the same rich Markdown, styles, links, wrapping and line numbers at the same Width and theme. Alternate selects Markdown source highlighting for every format. The shared image decoder is optional and independent of OutputFormat.

```yaml
Type: PSTextMate.Core.OutputFormat
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -InputObject

Accepts PowerShell source text, a file, or a script block.
Piped strings are source text; a string passed as an argument is a path unless it cannot be one (it spans several lines, or names a drive that does not exist).
Script blocks, piped or passed as an argument, are their source text: `$function:prompt | Format-PowerShell` or `Format-PowerShell { Get-Date }`.
FileInfo objects and objects with a `FullName` property (or a `Path` property on custom objects) are read as files.

```yaml
Type: System.Object
DefaultValue: ''
SupportsWildcards: false
Aliases:
- FullName
- Path
ParameterSets:
- Name: (All)
  Position: 0
  IsRequired: true
  ValueFromPipeline: true
  ValueFromPipelineByPropertyName: true
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -LineNumbers

When present, include line numbers in the rendered output.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Theme

Chooses a named theme (built-in or registered with Add-TextMateTheme) for styling the highlighted output.

```yaml
Type: System.String
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: false
  ValueFromPipeline: false
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

Accepts textual input representing PowerShell source.

## OUTPUTS

### PSTextMate.Core.HighlightedDocument

Returns the highlighted representation of the input source as a `HighlightedDocument` object.

## NOTES

The cmdlet uses the PowerShell grammar shipped with the module. For very large scripts consider chunking input to avoid high memory usage.

## RELATED LINKS

See `Format-CSharp` and `Format-Markdown` for other language renderers.
