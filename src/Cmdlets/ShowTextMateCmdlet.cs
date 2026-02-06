using System.Management.Automation;
using PSTextMate.Core;
using PSTextMate.Utilities;
using Spectre.Console.Rendering;
using TextMateSharp.Grammars;

namespace PSTextMate.Commands;

/// <summary>
/// Cmdlet for displaying syntax-highlighted text using TextMate grammars.
/// Supports both string input and file processing with theme customization.
/// </summary>
[Cmdlet(VerbsCommon.Show, "TextMate", DefaultParameterSetName = "Default")]
[Alias("st", "Show-Code")]
[OutputType(typeof(HighlightedText))]
public sealed class ShowTextMateCmdlet : PSCmdlet {
    private readonly List<string> _inputObjectBuffer = [];
    private string? _sourceExtensionHint;
    private string? _sourceBaseDirectory;

    /// <summary>
    /// String content to render with syntax highlighting.
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
    /// TextMate language ID for syntax highlighting (e.g., 'powershell', 'csharp', 'python').
    /// If not specified, detected from file extension (for files) or defaults to 'powershell' (for strings).
    /// </summary>
    [Parameter]
    [ArgumentCompleter(typeof(LanguageCompleter))]
    public string? Language { get; set; }

    /// <summary>
    /// Color theme to use for syntax highlighting.
    /// </summary>
    [Parameter]
    public ThemeName Theme { get; set; } = ThemeName.DarkPlus;

    /// <summary>
    /// When present, force use of the standard renderer even for Markdown grammars.
    /// This can be used to preview alternate rendering behavior.
    /// </summary>
    [Parameter]
    public SwitchParameter Alternate { get; set; }

    /// <summary>
    /// When present, output a single HighlightedText container instead of enumerating renderables.
    /// </summary>
    [Parameter]
    public SwitchParameter Lines { get; set; }

    protected override void ProcessRecord() {
        if (MyInvocation.ExpectingInput) {
            if (InputObject?.BaseObject is FileInfo file) {
                try {
                    foreach (HighlightedText result in ProcessPathInput(file)) {
                        if (Lines.IsPresent) {
                            WriteObject(result.Renderables, enumerateCollection: true);
                        }
                        else {
                            WriteObject(result);
                        }
                    }
                }
                catch (Exception ex) {
                    WriteError(new ErrorRecord(ex, "ShowTextMateCmdlet", ErrorCategory.NotSpecified, file));
                }
            }
            else if (InputObject?.BaseObject is string inputString) {
                // Extract extension hint and base directory from PSPath if available
                if (_sourceExtensionHint is null || _sourceBaseDirectory is null) {
                    GetSourceHint();
                }

                // Buffer the input string for later processing
                _inputObjectBuffer.Add(inputString);
            }
        }
        else if (InputObject is not null) {
            FileInfo file = new(GetUnresolvedProviderPathFromPSPath(InputObject?.ToString()));
            if (!file.Exists) return;
            try {
                foreach (HighlightedText result in ProcessPathInput(file)) {
                    if (Lines.IsPresent) {
                        WriteObject(result.Renderables, enumerateCollection: true);
                    }
                    else {
                        WriteObject(result);
                    }
                }
            }
            catch (Exception ex) {
                WriteError(new ErrorRecord(ex, "ShowTextMateCmdlet", ErrorCategory.NotSpecified, file));
            }
        }
    }

    /// <summary>
    /// Finalizes processing after all pipeline records have been processed.
    /// </summary>
    protected override void EndProcessing() {

        try {
            if (_inputObjectBuffer.Count == 0) {
                // WriteVerbose("No string input provided");
                return;
            }
            if (_sourceExtensionHint is null || _sourceBaseDirectory is null) {
                GetSourceHint();
            }
            HighlightedText? result = ProcessStringInput();
            if (result is not null) {
                if (Lines.IsPresent) {
                    WriteObject(result.Renderables, enumerateCollection: true);
                }
                else {
                    WriteObject(result);
                }
            }
        }
        catch (Exception ex) {
            WriteError(new ErrorRecord(ex, "ShowTextMateCmdlet", ErrorCategory.NotSpecified, MyInvocation.BoundParameters));
        }
    }

    private HighlightedText? ProcessStringInput() {
        // Normalize buffered strings into lines
        string[] lines = TextMateHelper.NormalizeToLines(_inputObjectBuffer);

        if (lines.AllIsNullOrEmpty()) {
            WriteVerbose("All input strings are null or empty");
            return null;
        }

        // Resolve language (explicit parameter, pipeline extension hint, or default)
        string effectiveLanguage = !string.IsNullOrEmpty(Language) ? Language :
            !string.IsNullOrEmpty(_sourceExtensionHint) ? _sourceExtensionHint :
            "powershell";

        WriteVerbose($"effectiveLanguage: {effectiveLanguage}");

        (string? token, bool asExtension) = TextMateResolver.ResolveToken(effectiveLanguage);

        // Process and wrap in HighlightedText
        IRenderable[]? renderables = TextMateProcessor.ProcessLines(lines, Theme, token, isExtension: asExtension, forceAlternate: Alternate.IsPresent);

        return renderables is null
            ? null
            : new HighlightedText {
                Renderables = renderables
            };
    }

    private IEnumerable<HighlightedText> ProcessPathInput(FileInfo filePath) {
        // FileInfo filePath = new(GetUnresolvedProviderPathFromPSPath(fileinfo));

        if (!filePath.Exists) {
            throw new FileNotFoundException($"File not found: {filePath.FullName}", filePath.FullName);
        }

        // Set the base directory for relative image path resolution in markdown
        // Use the full directory path or current directory if not available
        string markdownBaseDir = filePath.DirectoryName ?? Environment.CurrentDirectory;
        Rendering.ImageRenderer.CurrentMarkdownDirectory = markdownBaseDir;
        WriteVerbose($"Set markdown base directory for image resolution: {markdownBaseDir}");

        // Resolve language: explicit parameter > file extension
        (string token, bool asExtension) = !string.IsNullOrWhiteSpace(Language)
            ? TextMateResolver.ResolveToken(Language)
            : (filePath.Extension, true);

        // Single file processing
        WriteVerbose($"Processing file: {filePath.FullName} with {(asExtension ? "extension" : "language")}: {token}");

        string[] lines = File.ReadAllLines(filePath.FullName);
        IRenderable[]? renderables = TextMateProcessor.ProcessLines(lines, Theme, token, isExtension: asExtension, forceAlternate: Alternate.IsPresent);

        if (renderables is not null) {
            yield return new HighlightedText {
                Renderables = renderables
            };
        }
    }

    private void GetSourceHint() {
        if (InputObject is null) return;
        string? hint = InputObject.Properties["PSPath"]?.Value as string
                        ?? InputObject.Properties["FullName"]?.Value as string
                        ?? InputObject.Properties["PSChildName"]?.Value as string;
        if (string.IsNullOrEmpty(hint)) return;

        WriteVerbose($"Language Hint: {hint}");

        if (_sourceExtensionHint is null) {
            string ext = Path.GetExtension(hint);
            if (!string.IsNullOrWhiteSpace(ext)) {
                _sourceExtensionHint = ext;
                WriteVerbose($"Detected extension hint from PSPath: {ext}");
            }
        }
        // remove potential Provider stuff from string.
        hint = GetUnresolvedProviderPathFromPSPath(hint);
        if (_sourceBaseDirectory is null) {
            string? baseDir = Path.GetDirectoryName(hint);
            if (!string.IsNullOrWhiteSpace(baseDir)) {
                _sourceBaseDirectory = baseDir;
                Rendering.ImageRenderer.CurrentMarkdownDirectory = baseDir;
                WriteVerbose($"Set markdown base directory from PSPath: {baseDir}");
            }
        }
    }
}
