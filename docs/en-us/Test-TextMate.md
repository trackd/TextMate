---
external help file: PSTextMate.dll-Help.xml
Module Name: TextMate
online version: https://github.com/trackd/TextMate/blob/main/docs/en-us
schema: 2.0.0
---

# Test-TextMate

## SYNOPSIS

Tests whether a language, extension, or file is supported by the module's TextMate grammars. Returns a boolean or diagnostic object indicating support.

## SYNTAX

### FileSet (Default)

```
Test-TextMate -File <string> [<CommonParameters>]
```

### ExtensionSet

```
Test-TextMate -Extension <string> [<CommonParameters>]
```

### LanguageSet

```
Test-TextMate -Language <string> [<CommonParameters>]
```

## ALIASES

This cmdlet has the following aliases,
  None

## DESCRIPTION

Test-TextMate verifies support for TextMate languages and extensions.
Use the `-File` parameter to check a specific file path, `-Extension` to verify a file extension, or `-Language` to test a language identifier.
The cmdlet returns `true` or `false`

## EXAMPLES

### Example 1

Example: test a file path for support

```
Test-TextMate -File .\src\Program.cs
```

### Example 2

Example: test by extension

```
Test-TextMate -Extension .ps1
```

### Example 3

Example: test by language identifier

```
Test-TextMate -Language powershell
```

## PARAMETERS

### -Extension

File extension to test (for example `.ps1`, `.cs`).
When used the cmdlet returns whether the module has a grammar associated with that extension.

```yaml
Type: System.String
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: ExtensionSet
  Position: Named
  IsRequired: true
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -File

Path to a file to test for grammar support.
The path is resolved and existence is validated before checking support.

```yaml
Type: System.String
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: FileSet
  Position: Named
  IsRequired: true
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Language

TextMate language ID to test (for example `powershell`, `csharp`, `markdown`).
Returns whether that language ID is supported.

```yaml
Type: System.String
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: LanguageSet
  Position: Named
  IsRequired: true
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

## OUTPUTS

### bool

Returns `bool` results for support checks. In error cases or file-not-found scenarios the cmdlet may write errors or diagnostic objects to the pipeline.

## NOTES

Use `Get-TextMate` to discover available language IDs and their extensions before calling this cmdlet.

## RELATED LINKS

See `Get-TextMate` and `Format-TextMate` for discovery and rendering workflows.
