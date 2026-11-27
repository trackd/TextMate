namespace PSTextMate.Core;

/// <summary>Owns a streaming spool and the sole terminal reader. Producers never write to the terminal.</summary>
internal sealed class StreamingPager : IDisposable {
    private readonly PagerSpool _spool;
    private readonly CancellationTokenSource _stop;
    private readonly Task _worker;
    private System.Runtime.ExceptionServices.ExceptionDispatchInfo? _failure;
    private TextStyle _style = TextStyle.Plain;
    private int _disposed;

    internal StreamingPager(CancellationToken cancellationToken, IPagerTerminal? terminal = null) {
        _stop = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _spool = new PagerSpool(_stop.Token);
        // Retained payloads already passed the image provider's capability check. Do not query input here.
        terminal ??= new PagerTerminal(captureControlC: false, retainedImages: () => _spool.ImageCount > 0);
        _worker = Task.Factory.StartNew(() => {
            try {
                TerminalPager.Run(new PagerState(_spool), terminal, _stop.Token);
            }
            catch (Exception exception) {
                Volatile.Write(ref _failure, System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception));
                throw;
            }
            finally {
                _stop.Cancel();
            }
        }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);
    }

    internal void Append(string text) {
        try {
            ThrowIfStopped();
            if (!TerminalText.HasControls(text) && !text.AsSpan().Contains('\t')) {
                _spool.Append(text, _style);
                ThrowIfStopped();
                return;
            }
            var parser = new VTParser(_style);
            parser.Append(text);
            HighlightedDocument document = parser.Build(text);
            _style = parser.CurrentStyle;
            _spool.Append(document);
            ThrowIfStopped();
        }
        catch (OperationCanceledException) {
            ThrowIfStopped();
            throw;
        }
    }

    internal void Append(HighlightedDocument document) {
        try {
            ThrowIfStopped();
            _spool.Append(document);
            ThrowIfStopped();
        }
        catch (OperationCanceledException) {
            ThrowIfStopped();
            throw;
        }
    }

    private void ThrowIfStopped() {
        if (_worker.IsCompleted)
            _worker.GetAwaiter().GetResult();
        if (_stop.IsCancellationRequested) {
            Volatile.Read(ref _failure)?.Throw();
            _stop.Token.ThrowIfCancellationRequested();
        }
    }

    internal void Complete() {
        _spool.Complete();
        _worker.GetAwaiter().GetResult();
    }

    public void Dispose() {
        if (Interlocked.Exchange(ref _disposed, 1) != 0)
            return;
        _stop.Cancel();
        try {
            _worker.GetAwaiter().GetResult();
        }
        catch (Exception) when (_worker.IsFaulted || _stop.IsCancellationRequested) {
            // Append/Complete surface worker failures. Cleanup must preserve an upstream pipeline error.
        }
        finally {
            _spool.Dispose();
            _stop.Dispose();
        }
    }
}
