---
external help file: PSTextMate.dll-Help.xml
Module Name: TextMate
online version: https://github.com/trackd/TextMate/blob/main/docs/en-us
schema: 2.0.0
---

# Out-Page

## SYNOPSIS

Displays pipeline content in the interactive pager.

## SYNTAX

### (All)

```powershell
Out-Page [-InputObject] <psobject> [<CommonParameters>]
```

## ALIASES

This cmdlet has the following aliases,
 page

## DESCRIPTION

Out-Page displays pipeline input as it arrives, while the upstream command continues running.
TextMate documents and their native output wrappers are displayed through the shared presentation model. Other objects use PowerShell formatting before paging. The pager is built into TextMate and does not load Spectre or Fullscreen.

Interactive streams use temporary files for history and a decoded cache limited to 64 source lines and an approximately 4 MiB budget. Temporary history is removed when the session closes. A single TextMate document retains the normal document pager, including its retained graphics. Redirected strings are written immediately as plain text. PowerShell's object formatter can initially buffer objects while choosing table columns.

Captured strings and PowerShell-formatted output retain ANSI colors, text decorations and OSC 8 hyperlinks through the shared document model. Cursor movement, terminal queries and other terminal commands are discarded. This is a styled output viewer, not a terminal emulator. Source files passed to TextMate formatters continue to display control characters literally.

Each changed pager frame is enclosed in synchronized-output mode 2026 boundaries. Terminals that support this mode present the frame together; other terminals ignore the boundaries. The pager does not read capability replies. Windows input/output modes are restored when the session exits, including cancellation and errors.

When the optional Sixel module supplies image pixels, the pager retains full images rather than clipping them or substituting block characters during scrolling. Navigation crosses an image as one unit. Images that cannot fit in the current viewport reserve blank space until a full placement is possible.

The pager supports keyboard navigation for scrolling and paging through large output.

use `?` for interactive help.

Navigation:

- Up/Down or j/k: move one displayed line.
- PageUp/PageDown, Space/l or b/h: move one page.
- Ctrl+U/Ctrl+D: move half a page.
- Home/End or g/G: jump to the first or last page.
- F: follow arriving output; ordinary navigation leaves follow mode.
- `/` or Ctrl+F: edit a literal, case-insensitive search. Enter applies; Escape cancels editing.
- n/N: next/previous occurrence, wrapping at the document boundaries.
- c: clear search; ?: show help; q/Escape/Ctrl+C: quit.

Search supports cursor movement, Home/End, Delete, grapheme-aware Backspace and Ctrl+U to clear the input. Matches are highlighted. The pager responds to resizing while idle, caches visible styled rows and writes only rows that change. It uses an alternate screen and restores terminal input and cursor state when exiting. Redirected output is written as plain text without opening an interactive session.

Search indexes at most 100,000 occurrences to bound memory; a plus sign marks a truncated result count. Quitting stops the producer at its next pipeline write. A producer blocked between writes may finish its current operation first; Ctrl+C requests normal PowerShell pipeline cancellation.

## EXAMPLES

### Example 1

Example: page output from a TextMate formatter cmdlet

```powershell
Get-Content .\src\PSTextMate\Cmdlets\OutPage.cs -Raw | Format-CSharp | Out-Page
```

### Example 2

Example: capture and pipe a `HighlightedDocument` object directly

```powershell
$highlighted = Get-Content .\README.md -Raw | Format-Markdown
$highlighted | Out-Page
```

### Example 3

Example: page ordinary PowerShell objects

```powershell
Get-Process | Select-Object Name, Id, CPU | Out-Page
```

### Example 4

Example: page regular `Out-String` content

```powershell
Get-ChildItem -Recurse | Out-String -Stream | Out-Page
```

## PARAMETERS

### -InputObject

Input to display in the pager. Accepts TextMate documents, their native wrappers, strings, or general objects from the pipeline. Native renderables are otherwise intended for their framework's layout and event loop.

```yaml
Type: System.Management.Automation.PSObject
DefaultValue: ''
SupportsWildcards: false
Aliases: []
ParameterSets:
- Name: (All)
  Position: Named
  IsRequired: true
  ValueFromPipeline: true
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

Accepts any pipeline object. Renderables are used directly; non-renderables are formatted into text lines for paging.

## OUTPUTS

### System.Void

This cmdlet writes to the interactive pager and does not emit pipeline output.

## NOTES

Use `q` or `Esc` to exit the pager. Arrow keys, PageUp/PageDown, Spacebar, Home, and End are supported for navigation.

## RELATED LINKS

See also `Format-TextMate`, `Format-CSharp`, `Format-Markdown`, and `Format-PowerShell`.
