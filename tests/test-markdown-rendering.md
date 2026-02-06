# Markdown Rendering Test File

### Fenced Code Block with Language

```csharp
public class TestClass
{
    public string Name { get; set; } = "Test";
    
    public void DoSomething()
    {
        Console.WriteLine($"Hello {Name}!");
    }
}
```

### PowerShell Code Block

```powershell
Get-Process | Where-Object { $_.ProcessName -like "pwsh*" } | Select-Object ProcessName, Id
```

### Plain Code Block

```
This is a plain code block
with no language specified
and multiple lines
```

## Task Lists

- [x] Completed task with checkmark
- [ ] Incomplete task with empty checkbox
- [x] Another completed task
- [ ] Another incomplete task

## Paragraphs and Emphasis

This is a **bold** text and this is *italic* text.  
Here's some `inline code` in a paragraph.  

## Tables

| Column 1 | Column 2 | Column 3 |
|----------|----------|----------|
| Row 1 A  | Row 1 B  | Row 1 C  |
| Row 2 A  | Row 2 B  | Row 2 C  |

## Mixed Content

This paragraph contains **bold**, *italic*, and `code` elements all together.  

### Indented Code Block

    This is an indented code block  
    with multiple lines  
    and preserved spacing  

## Special Characters and VT Sequences

Text with potential VT sequences: `\x1b[31mRed Text\x1b[0m`  

## Edge Cases

### Empty Code Block

```

```

### Code Block with Trailing Whitespace

```javascript
function test() {
    console.log("test");    
}    
```

### Nested Lists with Tasks

1. First item
   - [x] Nested completed task
   - [ ] Nested incomplete task
2. Second item
   - [ ] Another nested task
