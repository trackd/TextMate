---
external help file: PSTextMate.dll-Help.xml
Module Name: TextMate
online version: https://github.com/trackd/TextMate/blob/main/docs/en-us
schema: 2.0.0
---

# Format-PowerShell

## SYNOPSIS

Renders PowerShell code using TextMate grammars and returns a PSTextMate.Core.HighlightedText result for display or programmatic use.

## SYNTAX

### (All)

```
Format-PowerShell [-InputObject] <psobject> [-Theme <ThemeName>] [-LineNumbers] [<CommonParameters>]
```

## ALIASES

This cmdlet has the following aliases,
  fps

## DESCRIPTION

Format-PowerShell highlights PowerShell source and script files. Input can be provided as pipeline text or via file contents. The resulting `HighlightedText` can be used with console renderers or further processed. Use `-Theme` and `-LineNumbers` to adjust output.

## EXAMPLES

### Example 1: Highlight a short PowerShell snippet

```
'Get-Process | Where-Object { $_.CPU -gt 1 }' | Format-PowerShell
```

### Example 2: Highlight a script file with line numbers

```
Get-Content .\scripts\deploy.ps1 -Raw | Format-PowerShell -LineNumbers
```

### Example 3: Pipe FileInfo object and render with theme

```
Get-ChildItem .\scripts\*.ps1 | Format-PowerShell -Theme Monokai
```

## PARAMETERS

### -InputObject

Accepts a `string` or object containing PowerShell source text.
Typically used with `Get-Content -Raw` or piping literal strings.
Accepts FileInfo objects

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

When present, include line numbers in the rendered output.

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

Chooses a `TextMateSharp.Grammars.ThemeName` for styling the highlighted output.

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

Accepts textual input representing PowerShell source.

## OUTPUTS

### PSTextMate.Core.HighlightedText

Returns the highlighted representation of the input source as a `HighlightedText` object.

## NOTES

The cmdlet uses the PowerShell grammar shipped with the module. For very large scripts consider chunking input to avoid high memory usage.

## RELATED LINKS

See `Format-CSharp` and `Format-Markdown` for other language renderers.
