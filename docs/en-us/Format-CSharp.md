---
external help file: PSTextMate.dll-Help.xml
Module Name: TextMate
online version: https://github.com/trackd/TextMate/blob/main/docs/en-us
schema: 2.0.0
---

# Format-CSharp

## SYNOPSIS

Renders C# source code using TextMate grammars and returns a PSTextMate.Core.HighlightedDocument object.
Use for previewing or formatting C# snippets and files.

## SYNTAX

### (All)

```
Format-CSharp [-InputObject] <psobject> [-Theme <string>] [-LineNumbers] [<CommonParameters>]
```

## ALIASES

This cmdlet has the following aliases,
  fcs

## DESCRIPTION

Format-CSharp renders C# input using the TextMate grammar for C#. Input can be provided as objects (strings) via the pipeline or by passing file contents.
The cmdlet produces a `HighlightedDocument` object suitable for rendering to console.
Use `-Theme` to select a visual theme and `-LineNumbers` to include line numbers in the output.

## EXAMPLES

### Example 1

Example: highlight a C# snippet from the pipeline

```
"public class C { void M() {} }" | Format-CSharp
```

### Example 2

Example: format a file and include line numbers

```
Get-Content .\src\Program.cs -Raw | Format-CSharp -LineNumbers
```

### Example 3

Example: Pipe FileInfo objects

```
Get-ChildItem *.cs | Format-CSharp -Theme SolarizedDark
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

Accepts source text, a file, or a script block.
Piped strings are source text; a string passed as an argument is a path unless it cannot be one (it spans several lines, or names a drive that does not exist).
Script blocks are their source text.
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

When specified, include line numbers in the rendered output to aid reference and diffs.

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

Selects a named theme (built-in or registered with Add-TextMateTheme) to use when rendering. If omitted, the module default theme is used.

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

Accepts any object that can be converted to or contains a code string; commonly a `string` produced by `Get-Content -Raw` or piped literal text.

## OUTPUTS

### PSTextMate.Core.HighlightedDocument

Returns a `HighlightedDocument` object which contains the tokenized and styled representation of the input. This object is intended for rendering to consoles or for downstream processing.

## NOTES

This cmdlet uses TextMate grammars packaged with the module. For large files consider streaming the contents or increasing process memory limits.

## RELATED LINKS

See also `Format-PowerShell` and `Format-Markdown` for other language renderers.
