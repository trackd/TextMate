using System.IO;
using System.Management.Automation;
using PSTextMate.Core;
using PSTextMate.Utilities;
using TextMateSharp.Grammars;

namespace PSTextMate.Commands;

/// <summary>
/// Cmdlet for displaying syntax-highlighted text using TextMate grammars.
/// Supports both string input and file processing with theme customization.
/// </summary>
[Cmdlet(VerbsCommon.Format, "TextMate", DefaultParameterSetName = "Default")]
[Alias("Show-TextMate", "ftm")]
[OutputType(typeof(HighlightedText))]
public sealed class FormatTextMateCmdlet : TextMateCmdletBase {
    /// <summary>
    /// TextMate language ID for syntax highlighting (e.g., 'powershell', 'csharp', 'python').
    /// If not specified, detected from file extension (for files) or defaults to 'powershell' (for strings).
    /// </summary>
    [Parameter]
    [ArgumentCompleter(typeof(LanguageCompleter))]
    public string? Language { get; set; }

    /// <summary>
    /// When present, force use of the standard renderer even for Markdown grammars.
    /// This can be used to preview alternate rendering behavior.
    /// </summary>
    [Parameter]
    public SwitchParameter Alternate { get; set; }

    protected override string FixedToken => string.Empty;

    protected override bool UsesMarkdownBaseDirectory => true;

    protected override bool UsesExtensionHint => true;

    protected override bool UseAlternate => Alternate.IsPresent;

    protected override string? DefaultLanguage => "powershell";

    protected override (string token, bool asExtension) ResolveTokenForStringInput() {
        string effectiveLanguage = !string.IsNullOrEmpty(Language)
            ? Language
            : !string.IsNullOrEmpty(SourceExtensionHint)
                ? SourceExtensionHint
                : DefaultLanguage ?? "powershell";

        WriteVerbose($"effectiveLanguage: {effectiveLanguage}");

        return TextMateResolver.ResolveToken(effectiveLanguage);
    }

    protected override (string token, bool asExtension) ResolveTokenForPathInput(FileInfo filePath) {
        return !string.IsNullOrWhiteSpace(Language)
            ? TextMateResolver.ResolveToken(Language)
            : (filePath.Extension, true);
    }
}
