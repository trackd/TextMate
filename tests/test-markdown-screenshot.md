# TextMate Markdown Demo

| Feature | Example |
|--------:|:--------|
| Highlighting | C# and PowerShell code blocks below |
| Table | This table is a small sample |
| Image | Inline image shown below |

```csharp
// C# code block
public static bool IsSupportedFile(string file) {
    string ext = Path.GetExtension(file);
    return TextMateHelper.Extensions?.Contains(ext) == true;
}
```

- [x] Completed task with checkmark
- [ ] Incomplete task with empty checkbox

```powershell
function Write-Greeting {
  [cmdletbinding()]
  param(
    [string] $Name
  )
  "Hello $Name!"
}
```

*Italic text* and **bold text** and ***bold italic text***.

<img src="../assets/texmatelogo.png" width="20" alt="logo">

*Inline Sixel*
