---
document type: cmdlet
external help file: TextMate.dll-Help.xml
HelpUri: ''
Locale: en-US
Module Name: TextMate
ms.date: 02-17-2026
PlatyPS schema version: 2024-05-01
title: Get-SupportedTextMate
---

# Get-SupportedTextMate

## SYNOPSIS

Retrieves a list of supported TextMate languages and grammar metadata available to the module.

## SYNTAX

### (All)

```
Get-SupportedTextMate [<CommonParameters>]
```

## ALIASES

This cmdlet has the following aliases,
  None

## DESCRIPTION

Get-SupportedTextMate returns detailed `TextMateSharp.Grammars.Language` objects describing available grammars, file extensions, scopes, and other metadata. Useful for tooling that needs to map file types to TextMate language IDs.

## EXAMPLES

### Example 1

Example: list all supported languages

```
Get-SupportedTextMate
```

### Example 2

Example: show language names and extensions

```
Get-SupportedTextMate | Select-Object Name, Extensions
```

### Example 3

Example: find languages supporting .cs files

```
Get-SupportedTextMate | Where-Object { $_.Extensions -contains '.cs' }
```

## PARAMETERS

### CommonParameters

This cmdlet supports the common parameters: -Debug, -ErrorAction, -ErrorVariable,
-InformationAction, -InformationVariable, -OutBuffer, -OutVariable, -PipelineVariable,
-ProgressAction, -Verbose, -WarningAction, and -WarningVariable. For more information, see
[about_CommonParameters](https://go.microsoft.com/fwlink/?LinkID=113216).

## INPUTS

## OUTPUTS

### TextMateSharp.Grammars.Language

Emits `Language` objects from TextMateSharp describing the grammar, scope name, file extensions and any available configuration.

## NOTES

The returned objects can be used by `Format-TextMate` or other consumers to determine which grammar token to apply for a given file.

## RELATED LINKS

See `Format-TextMate` for rendering and `Test-SupportedTextMate` for support checks.
