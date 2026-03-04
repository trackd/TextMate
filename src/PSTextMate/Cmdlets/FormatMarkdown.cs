using System.Management.Automation;
using PSTextMate.Core;

namespace PSTextMate.Commands;

/// <summary>
/// Cmdlet for rendering Markdown input using TextMate syntax highlighting.
/// </summary>
[Cmdlet(VerbsCommon.Format, "Markdown")]
[Alias("fmd")]
[OutputType(typeof(HighlightedText))]
public sealed class FormatMarkdownCmdlet : TextMateCmdletBase {
    /// <summary>
    /// When present, force the standard renderer even for Markdown grammars.
    /// </summary>
    [Parameter]
    public SwitchParameter Alternate { get; set; }

    protected override string FixedToken => "markdown";
    protected override bool UsesMarkdownBaseDirectory => true;
    protected override bool UseAlternate => Alternate.IsPresent;
}
