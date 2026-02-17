---
document type: cmdlet
external help file: PSTextMate.dll-Help.xml
HelpUri: ''
Locale: en-US
Module Name: PSTextMate
ms.date: 02-17-2026
PlatyPS schema version: 2024-05-01
title: Format-Markdown
---

# Format-Markdown

## SYNOPSIS

Renders Markdown input using TextMate grammars or the module's alternate renderer and returns a PSTextMate.Core.HighlightedText object.

## SYNTAX

### (All)

```
Format-Markdown [-InputObject] <PSObject> [-Alternate] [-Theme <ThemeName>] [-LineNumbers]
 [<CommonParameters>]
```

## ALIASES

This cmdlet has the following aliases,
  fmd

## DESCRIPTION

Format-Markdown highlights Markdown content using the Markdown grammar where appropriate.
Use the `-Alternate` switch to force TextMate renderer as opposed to custom Markdig rendering.
Input may be piped in as text or read from files.
The cmdlet returns a `HighlightedText` object for rendering.

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

### -InputObject

Accepts a string or object containing Markdown text. Common usage is `Get-Content -Raw` piped into the cmdlet.
FileInfo objects are also accepted

```yaml
Type: PSObject
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
Type: SwitchParameter
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

Selects a `TextMateSharp.Grammars.ThemeName` to style the output. If omitted, the module default is used.

```yaml
Type: TextMateSharp.Grammars.ThemeName
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
Type: SwitchParameter
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

### PSTextMate.Core.HighlightedText

Returns a `HighlightedText` object representing the highlighted Markdown content.

## NOTES

The Markdown renderer may apply special handling for fenced code blocks and front-matter when `UsesMarkdownBaseDirectory` is enabled. Use `-Alternate` to bypass markdown-specific rendering.

## RELATED LINKS

See `Format-CSharp` and `Format-PowerShell` for language-specific renderers.
