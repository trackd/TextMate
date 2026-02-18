---
external help file: PSTextMate.dll-Help.xml
Module Name: TextMate
online version: https://github.com/trackd/TextMate/blob/main/docs/en-us
schema: 2.0.0
---

# Format-CSharp

## SYNOPSIS

Renders C# source code using TextMate grammars and returns a PSTextMate.Core.HighlightedText object.
Use for previewing or formatting C# snippets and files.

## SYNTAX

### (All)

```
Format-CSharp [-InputObject] <psobject> [-Theme <ThemeName>] [-LineNumbers] [<CommonParameters>]
```

## ALIASES

This cmdlet has the following aliases,
  fcs

## DESCRIPTION

Format-CSharp renders C# input using the TextMate grammar for C#. Input can be provided as objects (strings) via the pipeline or by passing file contents.
The cmdlet produces a `HighlightedText` object suitable for rendering to console.
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

### -InputObject

Accepts a string or object containing source code.
When receiving pipeline input, the cmdlet treats the value as source text.
For file processing, pass the file contents (for example with `Get-Content -Raw`).
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

When specified, include line numbers in the rendered output to aid reference and diffs.

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

Selects a `TextMateSharp.Grammars.ThemeName` to use when rendering. If omitted, the module default theme is used.

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

Accepts any object that can be converted to or contains a code string; commonly a `string` produced by `Get-Content -Raw` or piped literal text.

## OUTPUTS

### PSTextMate.Core.HighlightedText

Returns a `HighlightedText` object which contains the tokenized and styled representation of the input. This object is intended for rendering to consoles or for downstream processing.

## NOTES

This cmdlet uses TextMate grammars packaged with the module. For large files consider streaming the contents or increasing process memory limits.

## RELATED LINKS

See also `Format-PowerShell` and `Format-Markdown` for other language renderers.
