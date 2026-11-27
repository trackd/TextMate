using System.Buffers;

namespace PSTextMate.Core;

/// <summary>A reusable, single-owner text writer. Borrowed spans are invalidated by writing, clearing or disposal.</summary>
public sealed class TextBufferWriter : TextWriter, IBufferWriter<char> {
    private char[] _buffer;
    private bool _disposed;
    private TextWriter? _sink;
    private int _flushThreshold;
    [ThreadStatic] private static TextBufferWriter? _cached;
    public override Encoding Encoding => Encoding.Unicode;
    public int Capacity => _buffer.Length;
    public ReadOnlySpan<char> WrittenSpan => _buffer.AsSpan(0, Length);
    public int Length { get; private set; }

    public TextBufferWriter(int initialCapacity = 256) : base(CultureInfo.InvariantCulture) {
        ArgumentOutOfRangeException.ThrowIfLessThan(initialCapacity, 1);
        _buffer = ArrayPool<char>.Shared.Rent(initialCapacity);
    }

    public void Clear() {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Length = 0;
    }

    public override void Write(char value) {
        if (_sink is not null && Length >= _flushThreshold)
            FlushBuffer();
        EnsureCapacity(1);
        _buffer[Length++] = value;
    }
    public override void Write(string? value) => Write(value.AsSpan());
    public void WriteStyle(TextStyle style) => VT.WriteStyle(this, style);
    public void MoveTo(int column, int row) => VT.MoveTo(this, column, row);
    public override void Write(ReadOnlySpan<char> buffer) {
        if (_sink is not null) {
            if ((long)Length + buffer.Length > _flushThreshold)
                FlushBuffer();
            if (buffer.Length >= _flushThreshold) {
                _sink.Write(buffer);
                return;
            }
        }
        EnsureCapacity(buffer.Length);
        buffer.CopyTo(_buffer.AsSpan(Length));
        Length += buffer.Length;
    }
    public override void Write(char[] buffer, int index, int count) {
        ArgumentNullException.ThrowIfNull(buffer);
        Write(buffer.AsSpan(index, count));
    }
    public override void Write(int value) {
        Span<char> number = stackalloc char[11];
        value.TryFormat(number, out int written, provider: CultureInfo.InvariantCulture);
        Write(number[..written]);
    }
    public override void Write(long value) {
        Span<char> number = stackalloc char[20];
        value.TryFormat(number, out int written, provider: CultureInfo.InvariantCulture);
        Write(number[..written]);
    }
    public override string ToString() => new(WrittenSpan);

    public Span<char> GetSpan(int sizeHint = 0) {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);
        EnsureCapacity(Math.Max(1, sizeHint));
        return _buffer.AsSpan(Length);
    }
    public Memory<char> GetMemory(int sizeHint = 0) {
        ArgumentOutOfRangeException.ThrowIfNegative(sizeHint);
        EnsureCapacity(Math.Max(1, sizeHint));
        return _buffer.AsMemory(Length);
    }
    public void Advance(int count) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(count, _buffer.Length - Length);
        Length += count;
        if (_sink is not null && Length >= _flushThreshold)
            FlushBuffer();
    }

    internal void Attach(TextWriter sink, int flushThreshold = 16384) {
        _sink = sink;
        _flushThreshold = flushThreshold;
    }
    internal void Detach() => _sink = null;
    private void FlushBuffer() {
        if (Length == 0)
            return;
        _sink!.Write(WrittenSpan);
        Length = 0;
    }
    public override void Flush() {
        if (_sink is null)
            return;
        FlushBuffer();
        _sink.Flush();
    }

    private void EnsureCapacity(int additional) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int required = checked(Length + additional);
        if (required <= _buffer.Length)
            return;
        int capacity = (int)Math.Min(Array.MaxLength, Math.Max(required, (long)_buffer.Length * 2));
        char[] replacement = ArrayPool<char>.Shared.Rent(capacity);
        WrittenSpan.CopyTo(replacement);
        ArrayPool<char>.Shared.Return(_buffer, clearArray: true);
        _buffer = replacement;
    }

    internal static TextBufferWriter Rent() {
        TextBufferWriter? writer = _cached;
        _cached = null;
        return writer ?? new TextBufferWriter();
    }
    internal static void Return(TextBufferWriter writer) {
        writer.Detach();
        writer.Clear();
        if (writer.Capacity <= 65536 && _cached is null)
            _cached = writer;
        else
            writer.Dispose();
    }

    protected override void Dispose(bool disposing) {
        if (!_disposed) {
            _sink = null;
            ArrayPool<char>.Shared.Return(_buffer, clearArray: true);
            _buffer = [];
            Length = 0;
            _disposed = true;
        }
        base.Dispose(disposing);
    }
}
