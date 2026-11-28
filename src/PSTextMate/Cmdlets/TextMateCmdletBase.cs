using System.Management.Automation.Host;

namespace PSTextMate.Commands;

/// <summary>Shared input handling and optional output selection for highlighting cmdlets.</summary>
public abstract class TextMateCmdletBase : PSCmdlet, IDisposable {
    private readonly List<string> _inputBuffer = [];
    private readonly CancellationTokenSource _cancellation = new();
    private bool _pageWarningWritten;
    private int _disposed;

    [Parameter(Mandatory = true, ValueFromPipeline = true, ValueFromPipelineByPropertyName = true, Position = 0)]
    [AllowEmptyString]
    [AllowNull]
    [Alias("FullName", "Path")]
    // [object] rather than [PSObject]: PowerShell treats a script block argument to any other pipeline-by-property
    // parameter type as a delay-bind script block, which fails with ScriptBlockArgumentNoInput without input.
    // Pipeline values still arrive wrapped, keeping Get-Content's PSPath hints.
    public object? InputObject { get; set; }

    private PSObject? Input => InputObject is null ? null : PSObject.AsPSObject(InputObject);

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

    // The first eligible command probes the terminal once; rendering only reads the cached capabilities.
    protected override void BeginProcessing() => TerminalCapabilities.Prepare(this);

    protected override void ProcessRecord() {
        if (Input?.BaseObject is not { } value)
            return;
        try {
            if (value is FileInfo file) {
                ProcessFile(file);
            }
            else if (value is ScriptBlock script) {
                // A script block is source text, piped or passed to -InputObject: $function:prompt | Format-PowerShell.
                _inputBuffer.Add(script.ToString());
            }
            else if (MyInvocation.ExpectingInput && value is string text) {
                CaptureSourceHints();
                _inputBuffer.Add(text);
            }
            else if (value is string argument && IsSourceText(argument)) {
                _inputBuffer.Add(argument);
            }
            else if (PathProperty(Input) is { } path) {
                // The FullName and Path aliases: objects that name a file, such as Select-Object FullName output.
                ProcessFile(new FileInfo(GetUnresolvedProviderPathFromPSPath(path)));
            }
            else if (MyInvocation.ExpectingInput && value is not string) {
                throw new ArgumentException($"Cannot highlight a {value.GetType().Name}. Pipe source strings, files, script blocks or objects with a FullName or Path property; Out-Page pages other objects.");
            }
            else {
                ProcessFile(new FileInfo(GetUnresolvedProviderPathFromPSPath(Input.ToString())));
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

    protected override void StopProcessing() {
        try {
            _cancellation.Cancel();
        }
        catch (ObjectDisposedException) {
            // StopProcessing can race with EndProcessing's final cleanup.
        }
    }

    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _cancellation.Dispose();
        GC.SuppressFinalize(this);
    }

    private void ProcessFile(FileInfo file) {
        if (!file.Exists) {
            if (Directory.Exists(file.FullName))
                throw new ArgumentException($"'{file.FullName}' is a directory. Pass a file, or pipe Get-ChildItem -File output.");
            string hint = MyInvocation.ExpectingInput ? string.Empty
                : $" To highlight text rather than a file, pipe it: '...' | {MyInvocation.MyCommand.Name}";
            throw new FileNotFoundException($"File not found: {file.FullName}.{hint}", file.FullName);
        }
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
        // Text and native outputs are laid out again at the width available when they are displayed.
        var options = new RendererOptions(UseAlternate, SourceBaseDirectory, ResolveWidth(), ConstrainWidth: Width > 0);
        // HTML is not a terminal: without -Width, code keeps its source lines; Markdown still needs a layout width.
        if (OutputFormat == OutputFormat.Html && Width <= 0 && document.Language != "markdown")
            options = options with { Width = int.MaxValue / 2 };
        if (Page.IsPresent) {
            // Text and native formats display the same in the pager; HTML is the one format -Page cannot honor.
            if (OutputFormat == OutputFormat.Html && !_pageWarningWritten) {
                _pageWarningWritten = true;
                WriteWarning($"-Page shows the document in the built-in pager; -OutputFormat {OutputFormat} is ignored.");
            }
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
        string? hint = Input?.Properties["PSPath"]?.Value as string
            ?? Input?.Properties["FullName"]?.Value as string
            ?? Input?.Properties["PSChildName"]?.Value as string;
        if (string.IsNullOrEmpty(hint))
            return;
        string resolved = GetUnresolvedProviderPathFromPSPath(hint);
        SourceExtensionHint = Path.GetExtension(resolved);
        SourceBaseDirectory = Path.GetDirectoryName(resolved);
    }

    /// <summary>
    /// An argument string is a path, as it always was, unless it cannot be one: it spans several lines, or the
    /// provider cannot resolve it at all (for example "if ($global:x) { }" names a drive that does not exist).
    /// Then it is source text. An existing file always wins, and a well-formed missing path still reports an error.
    /// </summary>
    /// <summary>
    /// FullName names a file on any object. Path only counts on custom objects (Select-Object output, literals):
    /// on a Process or a service it names an executable, which must not be read as source.
    /// </summary>
    private static string? PathProperty(PSObject input) {
        ReadOnlySpan<string> names = input.BaseObject is PSCustomObject ? ["FullName", "Path"] : ["FullName"];
        foreach (string name in names) {
            // Cmdlet output stored in a property (FullName = (Join-Path ...)) stays wrapped in a PSObject.
            object? value = input.Properties[name]?.Value;
            if ((value is PSObject wrapper ? wrapper.BaseObject : value) is string { Length: > 0 } path)
                return path;
        }
        return null;
    }

    private bool IsSourceText(string argument) {
        if (argument.Length == 0)
            return false;
        string path;
        try {
            path = GetUnresolvedProviderPathFromPSPath(argument);
        }
        catch (Exception error) when (error is System.Management.Automation.DriveNotFoundException or ProviderNotFoundException or ProviderInvocationException
            or NotSupportedException or ArgumentException or PSArgumentException or ItemNotFoundException) {
            return true;
        }
        return !File.Exists(path) && argument.AsSpan().IndexOfAny('\r', '\n') >= 0;
    }
}
