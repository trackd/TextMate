using System.Management.Automation.Host;

namespace PSTextMate.Commands;

/// <summary>Shared input handling and optional output selection for highlighting cmdlets.</summary>
public abstract class TextMateCmdletBase : PSCmdlet, IDisposable {
    private readonly List<string> _inputBuffer = [];
    private readonly CancellationTokenSource _cancellation = new();

    [Parameter(Mandatory = true, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true, Position = 0)]
    [AllowEmptyString]
    [AllowNull]
    [Alias("FullName", "Path")]
    public PSObject? InputObject { get; set; }

    [Parameter]
    [ArgumentCompleter(typeof(ThemeCompleter))]
    [ValidateNotNullOrEmpty]
    public string Theme { get; set; } = "DarkPlus";

    [Parameter]
    public OutputFormat OutputFormat { get; set; } = OutputFormat.Text;

    [Parameter]
    public SwitchParameter LineNumbers { get; set; }

    [Parameter]
    public SwitchParameter Page { get; set; }

    /// <summary>Common presentation width for every output backend.</summary>
    [Parameter]
    [ValidateRange(8, 32767)]
    public int Width { get; set; }

    protected abstract string FixedToken { get; }
    protected virtual bool FixedTokenIsExtension => false;
    protected virtual bool UsesMarkdownBaseDirectory => false;
    protected virtual bool UsesExtensionHint => false;
    protected virtual bool UseAlternate => false;
    protected virtual string? DefaultLanguage => null;
    protected string? SourceExtensionHint { get; private set; }
    protected string? SourceBaseDirectory { get; private set; }

    protected override void ProcessRecord() {
        if (InputObject?.BaseObject is null)
            return;
        try {
            if (InputObject.BaseObject is FileInfo file) {
                ProcessFile(file);
            }
            else if (MyInvocation.ExpectingInput && InputObject.BaseObject is string text) {
                CaptureSourceHints();
                _inputBuffer.Add(text);
            }
            else {
                ProcessFile(new FileInfo(GetUnresolvedProviderPathFromPSPath(InputObject.ToString())));
            }
        }
        catch (Exception exception) when (exception is not PipelineStoppedException) {
            WriteError(new ErrorRecord(exception, GetType().Name, ErrorCategory.InvalidOperation, InputObject));
        }
    }

    protected override void EndProcessing() {
        try {
            if (_inputBuffer.Count == 0)
                return;
            string source = _inputBuffer.Count == 1 ? _inputBuffer[0] : string.Join('\n', _inputBuffer);
            (string token, bool extension) = ResolveTokenForStringInput();
            Emit(source, token, extension);
        }
        catch (Exception exception) when (exception is not PipelineStoppedException) {
            WriteError(new ErrorRecord(exception, GetType().Name, ErrorCategory.InvalidOperation, InputObject));
        }
        finally {
            Dispose();
        }
    }

    protected override void StopProcessing() => _cancellation.Cancel();

    public void Dispose() {
        _cancellation.Dispose();
        GC.SuppressFinalize(this);
    }

    private void ProcessFile(FileInfo file) {
        if (!file.Exists)
            throw new FileNotFoundException($"File not found: {file.FullName}", file.FullName);
        SourceBaseDirectory = file.DirectoryName;
        SourceExtensionHint = file.Extension;
        (string token, bool extension) = ResolveTokenForPathInput(file);
        Emit(File.ReadAllText(file.FullName), token, extension);
    }

    private void Emit(string source, string token, bool extension) {
        HighlightedDocument document = SyntaxHighlighter.Highlight(source, token, Theme, extension,
            LineNumbers.IsPresent, _cancellation.Token);
        if (document.LimitExceededCount != 0)
            WriteWarning($"Highlighting reached the regex work limit {document.LimitExceededCount} times; some styles may be incomplete.");
        var options = new RendererOptions(UseAlternate, SourceBaseDirectory, ResolveWidth(), ConstrainWidth: Width > 0);
        if (Page.IsPresent) {
            TextPager.Show(document, options, _cancellation.Token);
        }
        else {
            object result = OutputRenderer.Render(document, OutputFormat, options);
            if (OutputFormat is OutputFormat.Text or OutputFormat.Html) {
                WriteObject(result);
            }
            else {
                var native = PSObject.AsPSObject(result);
                native.TypeNames.Insert(0, "PSTextMate.NativeOutput");
                WriteObject(native);
            }
        }
    }

    private int ResolveWidth() {
        if (Width > 0)
            return Width;
        try {
            int width = Host.UI?.RawUI?.WindowSize.Width ?? 0;
            if (width >= 8)
                return width;
        }
        catch (Exception error) when (error is HostException or NotImplementedException or NotSupportedException or InvalidOperationException or IOException) {

        }
        try {
            int width = Host.UI?.RawUI?.BufferSize.Width ?? 0;
            if (width >= 8)
                return width;
        }
        catch (Exception error) when (error is HostException or NotImplementedException or NotSupportedException or InvalidOperationException or IOException) {

        }
        return 80;
    }

    protected virtual (string token, bool asExtension) ResolveTokenForStringInput() => ResolveFixedToken();
    protected virtual (string token, bool asExtension) ResolveTokenForPathInput(FileInfo filePath) => ResolveFixedToken();
    protected (string token, bool asExtension) ResolveFixedToken() => FixedTokenIsExtension
        ? (FixedToken.StartsWith('.') ? FixedToken : "." + FixedToken, true)
        : TextMateResolver.ResolveToken(FixedToken);

    private void CaptureSourceHints() {
        string? hint = InputObject?.Properties["PSPath"]?.Value as string
            ?? InputObject?.Properties["FullName"]?.Value as string
            ?? InputObject?.Properties["PSChildName"]?.Value as string;
        if (string.IsNullOrEmpty(hint))
            return;
        string resolved = GetUnresolvedProviderPathFromPSPath(hint);
        SourceExtensionHint = Path.GetExtension(resolved);
        SourceBaseDirectory = Path.GetDirectoryName(resolved);
    }
}
