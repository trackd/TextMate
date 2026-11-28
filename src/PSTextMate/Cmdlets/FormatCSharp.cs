
namespace PSTextMate.Commands;

/// <summary>
/// Cmdlet for rendering C# input using TextMate syntax highlighting.
/// </summary>
[Cmdlet(VerbsCommon.Format, "CSharp")]
[Alias("fcs")]
[OutputType(typeof(HighlightedDocument), typeof(string))]
public sealed class FormatCSharpCmdlet : TextMateCmdletBase {
    protected override string FixedToken => "csharp";
}
