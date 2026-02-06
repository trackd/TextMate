using System.Management.Automation;
using PSTextMate.Core;

namespace PSTextMate.Commands;

/// <summary>
/// Cmdlet for rendering PowerShell input using TextMate syntax highlighting.
/// </summary>
[Cmdlet(VerbsCommon.Format, "PowerShell")]
[Alias("fps")]
[OutputType(typeof(HighlightedText))]
public sealed class FormatPowerShellCmdlet : TextMateCmdletBase {
    protected override string FixedToken => "powershell";
}
