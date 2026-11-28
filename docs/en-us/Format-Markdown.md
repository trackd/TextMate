---
external help file: PSTextMate.dll-Help.xml
Module Name: TextMate
online version: https://github.com/trackd/TextMate/blob/main/docs/en-us
schema: 2.0.0
---

# Format-Markdown

## SYNOPSIS

Renders Markdown input using TextMate grammars or the module's alternate renderer and returns a PSTextMate.Core.HighlightedDocument object.

## SYNTAX

### (All)

```
Format-Markdown [-InputObject] <PSObject> [-Alternate] [-Theme <string>] [-LineNumbers]
 [<CommonParameters>]
```

## ALIASES

This cmdlet has the following aliases,
  fmd

## DESCRIPTION

Format-Markdown highlights Markdown content using the Markdown grammar where appropriate.
Use the `-Alternate` switch to force TextMate renderer as opposed to custom Markdig rendering.
Input may be piped in as text or read from files.
The cmdlet returns a `HighlightedDocument` object for rendering.

## EXAMPLES

### Example 1

Example: highlight a Markdown string

```
"# Title`n`n- item1`n- item2" | Format-Markdown
```

### Example 2

Example: format a file using the alternate renderer

```
Get-Content README.md -Raw | Format-Markdown -Alternate
```

### Example 3

Example: pipe FileInfo object and use a theme and line numbers

```
Get-ChildItem docs\guide.md | Format-Markdown -Theme SolarizedLight -LineNumbers
```

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

Accepts Markdown text, a file, or a script block. Common usage is `Get-Content -Raw` piped into the cmdlet.
Piped strings are Markdown text; a string passed as an argument is a path unless it cannot be one (it spans several lines, or names a drive that does not exist).
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

Includes line numbers in the rendered output when specified.

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

Selects a named theme (built-in or registered with Add-TextMateTheme) to style the output. If omitted, the module default is used.

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

### -Alternate

When present, forces the module's TextMate rendering instead of the custom Markdig rendering path.
Useful for testing alternate presentation.

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

### CommonParameters

This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable,
-InformationAction, -InformationVariable, -OutBuffer, -OutVariable, -PipelineVariable,
-ProgressAction, -Verbose, -WarningAction, and -WarningVariable. For more information, see
[about_CommonParameters](https://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

### PSObject

Accepts Markdown text as a `string` or an object that can provide textual content.

## OUTPUTS

### PSTextMate.Core.HighlightedDocument

Returns a `HighlightedDocument` object representing the highlighted Markdown content.

## NOTES

The Markdown renderer may apply special handling for fenced code blocks and front-matter when `UsesMarkdownBaseDirectory` is enabled. Use `-Alternate` to bypass markdown-specific rendering.

## RELATED LINKS

See `Format-CSharp` and `Format-PowerShell` for language-specific renderers.
