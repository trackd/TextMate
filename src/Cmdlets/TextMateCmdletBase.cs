using System.Management.Automation;
using PSTextMate;
using PSTextMate.Core;
using PSTextMate.Utilities;
using Spectre.Console.Rendering;
using TextMateSharp.Grammars;

namespace PSTextMate.Commands;

/// <summary>
/// Base cmdlet for rendering input using TextMate language or extension tokens.
/// </summary>
public abstract class TextMateCmdletBase : PSCmdlet {
    private readonly List<string> _inputObjectBuffer = [];
    private string? _sourceBaseDirectory;
    private string? _sourceExtensionHint;

    /// <summary>
    /// String content or file path to render with syntax highlighting.
    /// </summary>
    [Parameter(
        Mandatory = true,
        ValueFromPipeline = true,
        ValueFromPipelineByPropertyName = true,
        Position = 0
    )]
    [AllowEmptyString]
    [AllowNull]
    [Alias("FullName", "Path")]
    public PSObject? InputObject { get; set; }

    /// <summary>
    /// Color theme to use for syntax highlighting.
    /// </summary>
    [Parameter]
    public ThemeName Theme { get; set; } = ThemeName.DarkPlus;

    /// <summary>
    /// When present, render a gutter with line numbers.
    /// </summary>
    [Parameter]
    public SwitchParameter LineNumbers { get; set; }


    /// <summary>
    /// Fixed language or extension token used for rendering.
    /// </summary>
    protected abstract string FixedToken { get; }

    /// <summary>
    /// Indicates whether the fixed token should be treated as a file extension.
    /// </summary>
    protected virtual bool FixedTokenIsExtension => false;

    /// <summary>
    /// Indicates whether a Markdown base directory should be resolved for image paths.
    /// </summary>
    protected virtual bool UsesMarkdownBaseDirectory => false;

    /// <summary>
    /// Indicates whether an extension hint should be inferred from pipeline metadata.
    /// </summary>
    protected virtual bool UsesExtensionHint => false;

    /// <summary>
    /// Error identifier used for error records.
    /// </summary>
    protected virtual string ErrorId => GetType().Name;

    /// <summary>
    /// Indicates whether alternate rendering should be used.
    /// </summary>
    protected virtual bool UseAlternate => false;

    /// <summary>
    /// Default language token used when no explicit input is available.
    /// </summary>
    protected virtual string? DefaultLanguage => null;

    /// <summary>
    /// Resolved extension hint from the pipeline input.
    /// </summary>
    protected string? SourceExtensionHint => _sourceExtensionHint;

    /// <summary>
    /// Resolved base directory for markdown rendering.
    /// </summary>
    protected string? SourceBaseDirectory => _sourceBaseDirectory;

    protected override void ProcessRecord() {
        if (MyInvocation.ExpectingInput) {
            if (InputObject?.BaseObject is FileInfo file) {
                try {
                    foreach (HighlightedText result in ProcessPathInput(file)) {

                            WriteObject(result);
                    }
                }
                catch (Exception ex) {
                    WriteError(new ErrorRecord(ex, ErrorId, ErrorCategory.NotSpecified, file));
                }
            }
            else if (InputObject?.BaseObject is string inputString) {
                if (UsesMarkdownBaseDirectory || UsesExtensionHint) {
                    EnsureSourceHints();
                }

                _inputObjectBuffer.Add(inputString);
            }
        }
        else if (InputObject is not null) {
            FileInfo file = new(GetUnresolvedProviderPathFromPSPath(InputObject?.ToString()));
            if (!file.Exists) {
                return;
            }

            try {
                foreach (HighlightedText result in ProcessPathInput(file)) {

                        WriteObject(result);
                }
            }
            catch (Exception ex) {
                WriteError(new ErrorRecord(ex, ErrorId, ErrorCategory.NotSpecified, file));
            }
        }
    }

    protected override void EndProcessing() {
        try {
            if (_inputObjectBuffer.Count == 0) {
                return;
            }

            if (UsesMarkdownBaseDirectory || UsesExtensionHint) {
                EnsureSourceHints();
            }

            HighlightedText? result = ProcessStringInput();
            if (result is not null) {
                    WriteObject(result);
            }
        }
        catch (Exception ex) {
            WriteError(new ErrorRecord(ex, ErrorId, ErrorCategory.NotSpecified, MyInvocation.BoundParameters));
        }
    }

    private HighlightedText? ProcessStringInput() {
        string[] lines = TextMateHelper.NormalizeToLines(_inputObjectBuffer);

        if (lines.AllIsNullOrEmpty()) {
            WriteVerbose("All input strings are null or empty");
            return null;
        }

        (string token, bool asExtension) = ResolveTokenForStringInput();
        IRenderable[]? renderables = TextMateProcessor.ProcessLines(lines, Theme, token, isExtension: asExtension, forceAlternate: UseAlternate);

        return renderables is null
            ? null
            : new HighlightedText {
                Renderables = renderables,
                ShowLineNumbers = LineNumbers.IsPresent
            };
    }

    private IEnumerable<HighlightedText> ProcessPathInput(FileInfo filePath) {
        if (!filePath.Exists) {
            throw new FileNotFoundException($"File not found: {filePath.FullName}", filePath.FullName);
        }

        if (UsesMarkdownBaseDirectory) {
            string markdownBaseDir = filePath.DirectoryName ?? Environment.CurrentDirectory;
            Rendering.ImageRenderer.CurrentMarkdownDirectory = markdownBaseDir;
            WriteVerbose($"Set markdown base directory for image resolution: {markdownBaseDir}");
        }

        (string token, bool asExtension) = ResolveTokenForPathInput(filePath);
        WriteVerbose($"Processing file: {filePath.FullName} with {(asExtension ? "extension" : "language")}: {token}");

        string[] lines = File.ReadAllLines(filePath.FullName);
        IRenderable[]? renderables = TextMateProcessor.ProcessLines(lines, Theme, token, isExtension: asExtension, forceAlternate: UseAlternate);

        if (renderables is not null) {
            yield return new HighlightedText {
                Renderables = renderables,
                ShowLineNumbers = LineNumbers.IsPresent
            };
        }
    }

    protected virtual (string token, bool asExtension) ResolveTokenForStringInput() {
        return ResolveFixedToken();
    }

    protected virtual (string token, bool asExtension) ResolveTokenForPathInput(FileInfo filePath) {
        return ResolveFixedToken();
    }

    protected (string token, bool asExtension) ResolveFixedToken() {
        if (!FixedTokenIsExtension) {
            return TextMateResolver.ResolveToken(FixedToken);
        }

        string token = FixedToken.StartsWith('.') ? FixedToken : "." + FixedToken;
        return (token, true);
    }

    private void EnsureSourceHints() {
        if (InputObject is null) {
            return;
        }

        if (_sourceBaseDirectory is not null && _sourceExtensionHint is not null) {
            return;
        }

        string? hint = InputObject.Properties["PSPath"]?.Value as string
                        ?? InputObject.Properties["FullName"]?.Value as string
                        ?? InputObject.Properties["PSChildName"]?.Value as string;
        if (string.IsNullOrEmpty(hint)) {
            return;
        }

        if (_sourceExtensionHint is null) {
            string ext = Path.GetExtension(hint);
            if (string.IsNullOrWhiteSpace(ext)) {
                string resolvedHint = GetUnresolvedProviderPathFromPSPath(hint);
                ext = Path.GetExtension(resolvedHint);
            }

            if (!string.IsNullOrWhiteSpace(ext)) {
                _sourceExtensionHint = ext;
                WriteVerbose($"Detected extension hint from input: {ext}");
            }
        }

        if (_sourceBaseDirectory is null) {
            string resolvedPath = GetUnresolvedProviderPathFromPSPath(hint);
            string? baseDir = Path.GetDirectoryName(resolvedPath);
            if (!string.IsNullOrWhiteSpace(baseDir)) {
                _sourceBaseDirectory = baseDir;
                Rendering.ImageRenderer.CurrentMarkdownDirectory = baseDir;
                WriteVerbose($"Set markdown base directory from input: {baseDir}");
            }
        }
    }
}
