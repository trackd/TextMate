---
external help file: PSTextMate.dll-Help.xml
Module Name: TextMate
online version: https://github.com/trackd/TextMate/blob/main/docs/en-us/Add-TextMateLanguage.md
schema: 2.0.0
---

# Add-TextMateLanguage

## SYNOPSIS

Registers a local TextMate grammar in memory.

## SYNTAX

```
Add-TextMateLanguage [-Path] <string> -Name <string> [-Extension <string[]>] [-Alias <string[]>]
 [-Force] [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION

Loads and validates a local TextMate grammar supported by Tint, including JSON and plist grammars. Registers its language name, aliases and file extensions for highlighting and discovery. Registration can occur after highlighting has started. Existing documents retain their catalog and styles. Registrations are shared by the loaded module instance, are not written to disk, and last for the PowerShell process. Add commands to your profile to load them in future processes. The module does not download grammar files or dependencies.

## EXAMPLES

### Example 1: Add a language

```powershell
Add-TextMateLanguage -Path ./custom.tmLanguage.json -Name custom -Extension .custom
Format-TextMate ./example.custom
```

Registers a local grammar and highlights a matching file.

### Example 2: Replace a registration and inspect its metadata

```powershell
Add-TextMateLanguage -Path ./custom.tmLanguage.json -Name custom -Extension .custom -Alias custom-code -Force -PassThru
```

Replaces an existing registration and returns its language metadata.

### Example 3: Preview registration

```powershell
Add-TextMateLanguage -Path ./custom.tmLanguage.json -Name custom -WhatIf
```

Validates the file without changing the active catalog.

## PARAMETERS

### -Path

Literal local path to a grammar file. Relative paths resolve in the current PowerShell location.

```yaml
Type: System.String
DefaultValue: ''
SupportsWildcards: false
Aliases:
- FullName
- LiteralPath
ParameterSets:
- Name: (All)
  Position: 0
  IsRequired: true
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: true
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Name

Language identifier used by Format-TextMate -Language. The text and plaintext names are reserved.

```yaml
Type: System.String
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: true
  ValueFromPipeline: false
  ValueFromPipelineByPropertyName: false
  ValueFromRemainingArguments: false
DontShow: false
AcceptedValues: []
HelpMessage: ''
```

### -Extension

File extensions associated with this language. A leading dot is added when omitted.

```yaml
Type: System.String[]
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

### -Alias

Additional language names for discovery and lookup.

```yaml
Type: System.String[]
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

### -Force

Replaces conflicting language, scope or extension registrations.

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

### -PassThru

Returns the registered language metadata. Otherwise the command produces no pipeline output.

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

### -WhatIf

Shows the registration that would occur without changing the catalog.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: ''
SupportsWildcards: false
Aliases:
- wi
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

### -Confirm

Requests confirmation before changing the catalog.

```yaml
Type: System.Management.Automation.SwitchParameter
DefaultValue: ''
SupportsWildcards: false
Aliases:
- cf
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

## INPUTS

System.String

Path binds by property name, including FileInfo.FullName.

## OUTPUTS

PSTextMate.Core.Language

Returned only with PassThru.

## NOTES

Requires PowerShell 7.6 or later. External grammars must supply any referenced grammar dependencies separately.

## RELATED LINKS

[Get-TextMateGrammar](Get-TextMateGrammar.md)
[Add-TextMateTheme](Add-TextMateTheme.md)
