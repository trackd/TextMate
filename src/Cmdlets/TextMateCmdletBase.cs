using System.Management.Automation;
using PSTextMate;
using PSTextMate.Core;
using PSTextMate.Utilities;
using Spectre.Console.Rendering;
using TextMateSharp.Grammars;

namespace PSTextMate.Commands;

/// <summary>
/// Base cmdlet for rendering input using a fixed TextMate language or extension token.
/// </summary>
public abstract class TextMateCmdletBase : PSCmdlet {
    private readonly List<string> _inputObjectBuffer = [];
    private string? _sourceBaseDirectory;

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
    /// Error identifier used for error records.
    /// </summary>
    protected virtual string ErrorId => GetType().Name;

    /// <summary>
    /// Indicates whether alternate rendering should be used.
    /// </summary>
    protected virtual bool UseAlternate => false;

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
                if (UsesMarkdownBaseDirectory) {
                    EnsureBaseDirectoryFromInput();
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

            if (UsesMarkdownBaseDirectory) {
                EnsureBaseDirectoryFromInput();
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

        (string token, bool asExtension) = ResolveFixedToken();
        IRenderable[]? renderables = TextMateProcessor.ProcessLines(lines, Theme, token, isExtension: asExtension, forceAlternate: UseAlternate);

        return renderables is null
            ? null
            : new HighlightedText {
                Renderables = renderables
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

        (string token, bool asExtension) = ResolveFixedToken();
        WriteVerbose($"Processing file: {filePath.FullName} with {(asExtension ? "extension" : "language")}: {token}");

        string[] lines = File.ReadAllLines(filePath.FullName);
        IRenderable[]? renderables = TextMateProcessor.ProcessLines(lines, Theme, token, isExtension: asExtension, forceAlternate: UseAlternate);

        if (renderables is not null) {
            yield return new HighlightedText {
                Renderables = renderables
            };
        }
    }

    private (string token, bool asExtension) ResolveFixedToken() {
        if (!FixedTokenIsExtension) {
            return TextMateResolver.ResolveToken(FixedToken);
        }

        string token = FixedToken.StartsWith('.') ? FixedToken : "." + FixedToken;
        return (token, true);
    }

    private void EnsureBaseDirectoryFromInput() {
        if (_sourceBaseDirectory is not null || InputObject is null) {
            return;
        }

        string? hint = InputObject.Properties["PSPath"]?.Value as string
                        ?? InputObject.Properties["FullName"]?.Value as string
                        ?? InputObject.Properties["PSChildName"]?.Value as string;
        if (string.IsNullOrEmpty(hint)) {
            return;
        }

        string resolvedPath = GetUnresolvedProviderPathFromPSPath(hint);
        string? baseDir = Path.GetDirectoryName(resolvedPath);
        if (!string.IsNullOrWhiteSpace(baseDir)) {
            _sourceBaseDirectory = baseDir;
            Rendering.ImageRenderer.CurrentMarkdownDirectory = baseDir;
            WriteVerbose($"Set markdown base directory from input: {baseDir}");
        }
    }
}
