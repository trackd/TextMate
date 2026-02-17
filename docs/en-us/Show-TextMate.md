---
document type: cmdlet
external help file: PSTextMate.dll-Help.xml
HelpUri: ''
Locale: en-US
Module Name: PSTextMate
ms.date: 02-17-2026
PlatyPS schema version: 2024-05-01
title: Show-TextMate
---

# Show-TextMate

## SYNOPSIS

Displays syntax-highlighted text using TextMate grammars. Accepts strings or file input and returns a `HighlightedText` object for rendering.

## SYNTAX

### Default (Default)

```
Show-TextMate [-InputObject] <psobject> [-Language <string>] [-Alternate] [-Theme <ThemeName>]
 [-LineNumbers] [<CommonParameters>]
```

## ALIASES

This cmdlet has the following aliases,
  stm, Show-Code

## DESCRIPTION

Show-TextMate renders textual input using an appropriate TextMate grammar.
When `-Language` is provided it forces that language;
when omitted the cmdlet may infer language from file extension or default to `powershell`.
Use `-Alternate` to force the standard renderer for Markdown files.

## EXAMPLES

### Example 1

Example: highlight a snippet with an explicit language

```
"print('hello')" | Show-TextMate -Language python
```

### Example 2

Example: render a file and let the cmdlet infer language from extension

```
Show-TextMate -InputObject (Get-Content scripts\deploy.ps1 -Raw)
```

### Example 3

Example: preview a Markdown file

```
Get-Content README.md -Raw | Show-TextMate -Theme SolarizedDark
```

## PARAMETERS

### -Alternate

Forces the standard (non-markdown-specialized) renderer. Useful for previewing how code blocks will appear under the generic renderer.

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

### -InputObject

Accepts a `string` or object containing textual content.
Use `Get-Content -Raw` to pass file contents.

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

### -Language

Hint to force a particular TextMate language ID (for example `powershell`, `csharp`, `python`).
When provided it overrides extension-based inference.

```yaml
Type: String
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

### -LineNumbers

Include line numbers in the output when specified.

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

Select a `TextMateSharp.Grammars.ThemeName` used for styling output.

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

### CommonParameters

This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable,
-InformationAction, -InformationVariable, -OutBuffer, -OutVariable, -PipelineVariable,
-ProgressAction, -Verbose, -WarningAction, and -WarningVariable. For more information, see
[about_CommonParameters](https://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

### PSObject

Accepts textual input or objects containing text; commonly used with `Get-Content -Raw` or pipeline strings.

## OUTPUTS

### PSTextMate.Core.HighlightedText

Returns a `HighlightedText` object representing the rendered tokens and styling metadata.

## NOTES

If language cannot be inferred and `-Language` is not provided, the cmdlet defaults to `powershell` for string input. Use `-Language` to override detection.

## RELATED LINKS

See `Get-SupportedTextMate` to discover available language IDs and `Format-*` cmdlets for language-specific formatting.
