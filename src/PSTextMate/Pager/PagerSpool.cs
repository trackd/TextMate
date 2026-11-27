using System.Runtime.InteropServices;

namespace PSTextMate.Core;

/// <summary>Disk-backed history and width index. Only a bounded set of decoded lines remains in memory.</summary>
internal sealed partial class PagerSpool : IPagerSource, IDisposable {
    private const int EntrySize = 20;
    private const int CacheBudget = 4 * 1024 * 1024;
    private long _cachedBytes;
    internal long CachedBytes {
        get {
            lock (_gate)
                return _cachedBytes;
        }
    }
    private readonly Lock _gate = new();
    private readonly FileStream _data;
    private readonly FileStream _logical;
    private readonly FileStream _index;
    internal (string Data, string Logical, string Index) HistoryPaths => (_data.Name, _logical.Name, _index.Name);
    private readonly Dictionary<long, HighlightedDocument> _cache = new(64);
    private readonly Queue<long> _order = new(64);
    private readonly CancellationToken _cancellation;
    private readonly BinaryWriter _writer;
    internal PagerSpool(CancellationToken cancellationToken = default) {
        _cancellation = cancellationToken;
        _data = CreateFile();
        try {
            _logical = CreateFile();
            try {
                _index = CreateFile();
                try {
                    _writer = new BinaryWriter(_data, Encoding.Unicode, leaveOpen: true);
                }
                catch {
                    _index.Dispose();
                    throw;
                }
            }
            catch {
                _logical.Dispose();
                throw;
            }
        }
        catch {
            _data.Dispose();
            throw;
        }
    }
    private int _width = 80;
    private int _logicalCount;
    private int _offset;
    private int _count;
    private int _version;
    private bool _complete;
    public bool IsComplete => Volatile.Read(ref _complete);
    internal void Complete() {
        Volatile.Write(ref _complete, true);
        Interlocked.Increment(ref _version);
    }
    private bool _disposed;
    internal int CachedLines {
        get {
            lock (_gate)
                return _cache.Count;
        }
    }
    public int LineCount => Volatile.Read(ref _count);
    public int Version => Volatile.Read(ref _version);

    private static FileStream CreateFile() => new(Path.Combine(Path.GetTempPath(), "textmate-" + Guid.NewGuid().ToString("N")),
        FileMode.CreateNew, FileAccess.ReadWrite, FileShare.Read, 4096, FileOptions.DeleteOnClose | FileOptions.RandomAccess);

    internal void Append(HighlightedDocument document) {
        lock (_gate) {
            ObjectDisposedException.ThrowIf(_disposed, this);
            AppendImages(document);
            int row = 0;
            foreach (HighlightedLine line in document.Lines.Span) {
                bool imageRow = false;
                for (int image = 0; image < document.Images.Length; image++) {
                    TextBounds bounds = document.GetImageBounds(image);
                    if (row >= bounds.Row && row < bounds.Row + bounds.Height) {
                        imageRow = true;
                        break;
                    }
                }
                AppendLine(document.Text.AsSpan(line.Start, line.Length),
                    document.Ranges.Span.Slice(line.FirstRange, line.RangeCount), line.Start, imageRow);
                row++;
            }
            Publish();
        }
    }

    internal void Append(string text, TextStyle style) {
        lock (_gate) {
            ObjectDisposedException.ThrowIf(_disposed, this);
            int start = 0;
            while (start <= text.Length) {
                int length = text.AsSpan(start).IndexOfAny('\r', '\n');
                if (length < 0)
                    length = text.Length - start;
                ReadOnlySpan<StyledRange> ranges = length == 0 ? [] : [new(0, length, style)];
                AppendLine(text.AsSpan(start, length), ranges, 0);
                int end = start + length;
                if (end == text.Length)
                    break;
                start = end + (text[end] == '\r' && end + 1 < text.Length && text[end + 1] == '\n' ? 2 : 1);
            }
            Publish();
        }
    }

    private void AppendLine(ReadOnlySpan<char> text, ReadOnlySpan<StyledRange> ranges, int sourceStart, bool imageRow = false) {
        _cancellation.ThrowIfCancellationRequested();
        long position = _data.Length;
        _data.Position = position;
        _writer.Write(text.Length);
        _writer.Write(ranges.Length);
        _writer.Write(MemoryMarshal.AsBytes(text));
        foreach (StyledRange range in ranges) {
            _writer.Write(range.Start - sourceStart);
            _writer.Write(range.Length);
            _writer.Write(range.Style.Foreground);
            _writer.Write(range.Style.Background);
            _writer.Write((int)range.Style.Decoration);
            _writer.Write(range.Style.Hyperlink ?? string.Empty);
        }
        Span<byte> logical = stackalloc byte[16];
        BitConverter.TryWriteBytes(logical, position);
        BitConverter.TryWriteBytes(logical[8..], _offset);
        BitConverter.TryWriteBytes(logical[12..], imageRow ? ~text.Length : text.Length);
        _logical.Position = _logical.Length;
        _logical.Write(logical);
        _logicalCount++;
        IndexLine(position, _offset, text, imageRow);
        _offset = checked(_offset + text.Length + 1);
    }

    private void Publish() {
        _index.Flush();
        _writer.Flush();
        _logical.Flush();
        Interlocked.Increment(ref _version);
    }
    private void IndexLine(long position, int offset, ReadOnlySpan<char> text, bool imageRow = false) {
        int start = 0;
        Span<byte> entry = stackalloc byte[EntrySize];
        do {
            int length = imageRow ? text.Length : TerminalCells.Fit(text[start..], _width);
            if (length == 0 && start < text.Length)
                length = TerminalCells.NextClusterLength(text[start..]);
            BitConverter.TryWriteBytes(entry, position);
            BitConverter.TryWriteBytes(entry[8..], offset);
            BitConverter.TryWriteBytes(entry[12..], start);
            BitConverter.TryWriteBytes(entry[16..], length);
            _index.Position = (long)_count * EntrySize;
            _index.Write(entry);
            _count++;
            start += length;
        } while (start < text.Length);
    }

    public void SetWidth(int width) {
        lock (_gate) {
            ObjectDisposedException.ThrowIf(_disposed, this);
            width = Math.Max(1, width);
            if (_width == width)
                return;
            _width = width;
            _count = 0;
            _index.SetLength(0);
            Span<byte> logical = stackalloc byte[16];
            for (int index = 0; index < _logicalCount; index++) {
                _cancellation.ThrowIfCancellationRequested();
                _logical.Position = (long)index * 16;
                _logical.ReadExactly(logical);
                long position = BitConverter.ToInt64(logical);
                int offset = BitConverter.ToInt32(logical[8..]);
                HighlightedDocument line = ReadDocument(position);
                IndexLine(position, offset, line.Text, BitConverter.ToInt32(logical[12..]) < 0);
            }
            _index.Flush();
            Interlocked.Increment(ref _version);
        }
    }

    private void ReadEntry(int index, Span<byte> entry) {
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _count);
        _index.Position = (long)index * EntrySize;
        _index.ReadExactly(entry);
    }

    public PagerLine ReadLine(int index) {
        lock (_gate) {
            ObjectDisposedException.ThrowIf(_disposed, this);
            _cancellation.ThrowIfCancellationRequested();
            Span<byte> entry = stackalloc byte[EntrySize];
            ReadEntry(index, entry);
            HighlightedDocument document = ReadDocument(BitConverter.ToInt64(entry));
            int start = BitConverter.ToInt32(entry[12..]);
            int length = BitConverter.ToInt32(entry[16..]);
            return new(document, new(start, length, 0, document.Ranges.Length), BitConverter.ToInt32(entry[8..]));
        }
    }

    private HighlightedDocument ReadDocument(long position) {
        if (_cache.TryGetValue(position, out HighlightedDocument? document))
            return document;
        _data.Position = position;
        using var reader = new BinaryReader(_data, Encoding.Unicode, leaveOpen: true);
        int length = reader.ReadInt32();
        int rangeCount = reader.ReadInt32();
        char[] textBuffer = ArrayPool<char>.Shared.Rent(Math.Max(1, length));
        string text;
        try {
            _data.ReadExactly(MemoryMarshal.AsBytes(textBuffer.AsSpan(0, length)));
            text = new string(textBuffer, 0, length);
        }
        finally {
            ArrayPool<char>.Shared.Return(textBuffer, clearArray: true);
        }
        var ranges = new StyledRange[rangeCount];
        for (int index = 0; index < ranges.Length; index++) {
            int start = reader.ReadInt32();
            int count = reader.ReadInt32();
            int foreground = reader.ReadInt32();
            int background = reader.ReadInt32();
            var decoration = (TextDecoration)reader.ReadInt32();
            string link = reader.ReadString();
            ranges[index] = new(start, count, new(foreground, background, decoration, link.Length == 0 ? null : link));
        }
        document = new(text, "plaintext", string.Empty, false, ranges, [new(0, length, 0, ranges.Length)], 0, text);
        long bytes = CacheSize(document);
        if (bytes > CacheBudget)
            return document;
        while (_cache.Count > 0 && (_cache.Count >= 64 || _cachedBytes + bytes > CacheBudget)) {
            long key = _order.Dequeue();
            _cachedBytes -= CacheSize(_cache[key]);
            _cache.Remove(key);
        }
        if (bytes <= CacheBudget) {
            _cache.Add(position, document);
            _order.Enqueue(position);
            _cachedBytes += bytes;
        }
        return document;
    }

    private static long CacheSize(HighlightedDocument document) {
        long bytes = 512 + ((long)document.Text.Length * sizeof(char)) + ((long)document.Ranges.Length * 64);
        foreach (StyledRange range in document.Ranges.Span)
            bytes += (range.Style.Hyperlink?.Length ?? 0) * 2L;
        return bytes;
    }

    public int LineAt(int offset) {
        lock (_gate) {
            int low = 0;
            int high = _count;
            Span<byte> entry = stackalloc byte[EntrySize];
            while (low + 1 < high) {
                int middle = low + ((high - low) / 2);
                ReadEntry(middle, entry);
                if (BitConverter.ToInt32(entry[8..]) + BitConverter.ToInt32(entry[12..]) <= offset)
                    low = middle;
                else
                    high = middle;
            }
            return low;
        }
    }

    public void Dispose() {
        lock (_gate) {
            if (_disposed)
                return;
            _disposed = true;
            try {
                _writer.Dispose();
            }
            finally {
                try {
                    _data.Dispose();
                }
                finally {
                    try {
                        _logical.Dispose();
                    }
                    finally {
                        try {
                            _index.Dispose();
                        }
                        finally {
                            _images?.Dispose();
                            _cachedImage = null;
                            _cache.Clear();
                            _order.Clear();
                            _cachedBytes = 0;
                        }
                    }
                }
            }
        }
    }
}
