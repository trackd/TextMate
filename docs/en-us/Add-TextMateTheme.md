---
external help file: PSTextMate.dll-Help.xml
Module Name: TextMate
online version: https://github.com/trackd/TextMate/blob/main/docs/en-us/Add-TextMateTheme.md
schema: 2.0.0
---

# Add-TextMateTheme

## SYNOPSIS

Registers a local TextMate theme in memory.

## SYNTAX

```
Add-TextMateTheme [-Path] <string> -Name <string> [-Force] [-PassThru] [-WhatIf] [-Confirm] [<CommonParameters>]
```

## DESCRIPTION

Loads and validates a local theme supported by Tint, including VS Code JSON and TextMate plist themes. The name becomes available through Theme argument completion and works with every output format. Registration can occur after highlighting has started. Existing documents keep their original theme snapshot. Registrations are shared by the loaded module instance, are not written to disk, and last for the PowerShell process. Add registration commands to your profile for future processes. The command does not download themes.

## EXAMPLES

### Example 1: Add and use a theme

```powershell
Add-TextMateTheme -Path ./custom-theme.json -Name CustomDark
Format-PowerShell ./example.ps1 -Theme CustomDark
```

Registers a named theme for future documents.

### Example 2: Replace a registered theme

```powershell
Add-TextMateTheme -Path ./updated-theme.json -Name CustomDark -Force -PassThru
```

Replaces the theme for new documents and returns its name.

### Example 3: Preview an override

```powershell
Add-TextMateTheme -Path ./custom-theme.json -Name DarkPlus -Force -WhatIf
```

Validates a theme without overriding the built-in theme.

## PARAMETERS

### -Path

Literal local path to a theme file. Relative paths resolve in the current PowerShell location.

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

Name used by the Theme parameter. Custom names are not limited to built-in enum values.

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

### -Force

Replaces a theme with the same name, including a built-in theme.

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

Returns the registered name. Otherwise the command produces no pipeline output.

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

System.String

Returned only with PassThru.

## NOTES

Requires PowerShell 7.6 or later. Theme files and local includes must remain available while registration is being loaded; the module ships no custom theme assets.

## RELATED LINKS

[Add-TextMateLanguage](Add-TextMateLanguage.md)
[Format-TextMate](Format-TextMate.md)
