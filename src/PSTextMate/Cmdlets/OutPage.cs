namespace PSTextMate.Commands;

/// <summary>Pages highlighted documents or streams ordinary PowerShell output into disk-backed history.</summary>
[Cmdlet(VerbsData.Out, "Page")]
[Alias("page")]
[OutputType(typeof(void))]
public sealed class OutPageCmdlet : PSCmdlet, IDisposable {
    private readonly CancellationTokenSource _cancellation = new();
    private SteppablePipeline? _formatter;
    private StreamingPager? _pager;
    private HighlightedDocument? _pendingDocument;
    private TextStyle _style = TextStyle.Plain;
    private bool _redirected;
    private bool _wrote;
    private int _width;
    private int _disposed;

    [Parameter(ValueFromRemainingArguments = true, DontShow = true, Position = 0)]
    public PSObject[]? EaterOfArgs { get; set; }

    [Parameter(Mandatory = true, ValueFromPipeline = true)]
    [AllowNull]
    public PSObject? InputObject { get; set; }

    protected override void BeginProcessing() {
        _redirected = Console.IsInputRedirected || Console.IsOutputRedirected;
        _width = 80;
        try {
            int width = Host.UI.RawUI.WindowSize.Width;
            if (width > 0)
                _width = width;
        }
        catch (Exception error) when (error is System.Management.Automation.Host.HostException or NotImplementedException
            or NotSupportedException or InvalidOperationException or IOException) {

        }
    }

    protected override void ProcessRecord() {
        if (InputObject?.BaseObject is not { } value)
            return;
        try {
            _cancellation.Token.ThrowIfCancellationRequested();
            if (_pendingDocument is { } pending) {
                _pendingDocument = null;
                Emit(pending);
            }
            if (_formatter is not null) {
                WriteFormatted(_formatter.Process(InputObject));
            }
            else if (OutputRenderer.TryGetDocument(value, out HighlightedDocument? document)) {
                if (!_wrote && !_redirected)
                    _pendingDocument = document;
                else
                    Emit(document!);
            }
            else if (value is string text) {
                Emit(text);
            }
            else {
                string command = "Microsoft.PowerShell.Utility\\Out-String -Stream -Width " + _width.ToString(CultureInfo.InvariantCulture);
                _formatter = ScriptBlock.Create(command).GetSteppablePipeline(CommandOrigin.Internal);
                _formatter.Begin(true);
                WriteFormatted(_formatter.Process(InputObject));
            }
        }
        catch (OperationCanceledException) {
            throw new PipelineStoppedException();
        }
        catch (IOException error) {
            ThrowTerminatingError(new ErrorRecord(error, "PagerFailed", ErrorCategory.WriteError, this));
        }
    }

    private void WriteFormatted(Array values) {
        foreach (object? value in values)
            Emit(PSObject.AsPSObject(value).ToString());
    }

    private void Emit(string text) {
        if (!_redirected) {
            (_pager ??= new StreamingPager(_cancellation.Token)).Append(text);
        }
        else {
            if (!TerminalText.HasControls(text) && !text.AsSpan().Contains('\t') && !text.AsSpan().Contains('\r')) {
                Console.Out.Write(text.AsSpan());
                Console.Out.WriteLine();
                Console.Out.Flush();
                _wrote = true;
                return;
            }
            var parser = new VTParser(_style);
            parser.Append(text);
            HighlightedDocument document = parser.Build(text);
            _style = parser.CurrentStyle;
            WriteRedirected(document);
        }
        _wrote = true;
    }

    private void Emit(HighlightedDocument document) {
        int width = _redirected ? _width : Math.Max(1, Console.WindowWidth);
        int height = _redirected ? 0 : Math.Max(1, Console.WindowHeight - 6);
        document = new TextMateView(document).Layout(Math.Max(1, width - 1), height);
        if (_redirected)
            WriteRedirected(document);
        else
            (_pager ??= new StreamingPager(_cancellation.Token)).Append(document);
        _wrote = true;
    }

    private static void WriteRedirected(HighlightedDocument document) {
        document.WriteTo(Console.Out, ansi: false);
        Console.Out.WriteLine();
        Console.Out.Flush();
    }

    protected override void EndProcessing() {
        try {
            if (_pendingDocument is { } document) {
                TextPager.Show(document, cancellationToken: _cancellation.Token);
                _pendingDocument = null;
            }
            if (_formatter is not null)
                WriteFormatted(_formatter.End());
            _pager?.Complete();
        }
        catch (OperationCanceledException) {
            throw new PipelineStoppedException();
        }
        catch (IOException error) {
            ThrowTerminatingError(new ErrorRecord(error, "PagerFailed", ErrorCategory.WriteError, this));
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
        try {
            _formatter?.Dispose();
        }
        finally {
            _formatter = null;
            try {
                _pager?.Dispose();
            }
            finally {
                _pager = null;
                _cancellation.Dispose();
            }
        }
    }
}
