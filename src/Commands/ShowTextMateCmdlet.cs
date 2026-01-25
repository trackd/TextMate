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
    /// Enables streaming mode for large files, processing in batches.
    /// </summary>
    [Parameter]
    public SwitchParameter Stream { get; set; }

    /// <summary>
    /// When present, force use of the standard renderer even for Markdown grammars.
    /// This can be used to preview alternate rendering behavior.
    /// </summary>
    [Parameter]
    public SwitchParameter Alternate { get; set; }

    /// <summary>
    /// Number of lines to process per batch when streaming (default: 1000).
    /// </summary>
    [Parameter]
    public int BatchSize { get; set; } = 1000;

    /// <summary>
    /// Processes each input record from the pipeline.
    /// </summary>
    protected override void ProcessRecord() {
        if (MyInvocation.ExpectingInput) {
            if (InputObject?.BaseObject is FileInfo file) {
                try {
                    foreach (HighlightedText result in ProcessPathInput(file)) {
                        WriteObject(result.Renderables, enumerateCollection: true);
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
                    WriteObject(result.Renderables, enumerateCollection: true);
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
                // Output each renderable directly so pwshspectreconsole can format them
                WriteObject(result.Renderables, enumerateCollection: true);
            }
        }
        catch (Exception ex) {
            WriteError(new ErrorRecord(ex, "ShowTextMateCmdlet", ErrorCategory.NotSpecified, MyInvocation.BoundParameters));
        }
    }

    private HighlightedText? ProcessStringInput() {
        // Normalize buffered strings into lines
        string[] lines = NormalizeToLines(_inputObjectBuffer);

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

            if (Stream.IsPresent) {
            // Streaming mode - yield HighlightedText objects directly from processor
            WriteVerbose($"Streaming file: {filePath.FullName} with {(asExtension ? "extension" : "language")}: {token}, batch size: {BatchSize}");

            // Direct passthrough - processor returns HighlightedText now
                foreach (HighlightedText result in TextMateProcessor.ProcessFileInBatches(filePath.FullName, BatchSize, Theme, token, asExtension, Alternate.IsPresent)) {
                yield return result;
            }
        }
        else {
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
    }

    private static string[] NormalizeToLines(List<string> buffer) {

        if (buffer.Count == 0) {
            return [];
        }

        // Multiple strings in buffer - treat each as a line
        if (buffer.Count > 1) {
            return [.. buffer];
        }

        // Single string - check if it contains newlines
        string? single = buffer[0];
        if (string.IsNullOrEmpty(single)) {
            return single is not null ? [single] : [];
        }

        // Split on newlines if present
        if (single.Contains('\n') || single.Contains('\r')) {
            return single.Split(["\r\n", "\n", "\r"], StringSplitOptions.None);
        }

        // Single string with no newlines
        return [single];
    }
    private void GetSourceHint() {
        if (InputObject is null) return;

        string? hint = InputObject.Properties["PSPath"]?.Value as string
                        ?? InputObject.Properties["FullName"]?.Value as string;
        if (string.IsNullOrEmpty(hint)) return;

        // remove potential Provider stuff from string.
        hint = GetUnresolvedProviderPathFromPSPath(hint);
        if (_sourceExtensionHint is null) {
            string ext = Path.GetExtension(hint);
            if (!string.IsNullOrWhiteSpace(ext)) {
                _sourceExtensionHint = ext;
                WriteVerbose($"Detected extension hint from PSPath: {ext}");
            }
        }

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
