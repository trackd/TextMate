using System.Runtime.InteropServices;

namespace PSTextMate.Core;

internal sealed partial class PagerSpool {
    private const int ImageEntrySize = 32;
    private FileStream? _images;
    private int _imageCount;
    private int _cachedImageIndex = -1;
    private TerminalImage? _cachedImage;
    public int ImageCount => Volatile.Read(ref _imageCount);
    internal string? ImageHistoryPath => _images?.Name;
    internal long CachedImageBytes => (_cachedImage?.Payload.Length ?? 0) * 2L;

    private void AppendImages(HighlightedDocument document) {
        if (document.Images.IsEmpty)
            return;
        _images ??= CreateFile();
        Span<byte> entry = stackalloc byte[ImageEntrySize];
        for (int index = 0; index < document.Images.Length; index++) {
            _cancellation.ThrowIfCancellationRequested();
            TextBounds bounds = document.GetImageBounds(index);
            TerminalImage image = document.Images.Span[index].Image;
            long position = _data.Length;
            _data.Position = position;
            _data.Write(MemoryMarshal.AsBytes(image.Payload.Span));
            entry.Clear();
            BitConverter.TryWriteBytes(entry, checked(_offset + document.Lines.Span[bounds.Row].Start));
            BitConverter.TryWriteBytes(entry[4..], bounds.Column);
            BitConverter.TryWriteBytes(entry[8..], bounds.Width);
            BitConverter.TryWriteBytes(entry[12..], bounds.Height);
            BitConverter.TryWriteBytes(entry[16..], position);
            BitConverter.TryWriteBytes(entry[24..], image.Payload.Length);
            BitConverter.TryWriteBytes(entry[28..], (int)image.Protocol);
            _images.Position = (long)_imageCount * ImageEntrySize;
            _images.Write(entry);
            _imageCount++;
        }
    }

    private void ReadImageEntry(int index, Span<byte> entry) {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentOutOfRangeException.ThrowIfNegative(index);
        ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(index, _imageCount);
        _cancellation.ThrowIfCancellationRequested();
        _images!.Position = (long)index * ImageEntrySize;
        _images.ReadExactly(entry);
    }

    public TextBounds GetImageBounds(int index) {
        lock (_gate) {
            Span<byte> entry = stackalloc byte[ImageEntrySize];
            ReadImageEntry(index, entry);
            return new(BitConverter.ToInt32(entry[4..]), LineAt(BitConverter.ToInt32(entry)),
                BitConverter.ToInt32(entry[8..]), BitConverter.ToInt32(entry[12..]));
        }
    }

    public TerminalImage ReadImage(int index) {
        lock (_gate) {
            Span<byte> entry = stackalloc byte[ImageEntrySize];
            ReadImageEntry(index, entry);
            if (_cachedImageIndex == index && _cachedImage is not null)
                return _cachedImage;
            int length = BitConverter.ToInt32(entry[24..]);
            char[] payload = new char[length];
            _data.Position = BitConverter.ToInt64(entry[16..]);
            _data.ReadExactly(MemoryMarshal.AsBytes(payload.AsSpan()));
            var image = new TerminalImage(BitConverter.ToInt32(entry[8..]), BitConverter.ToInt32(entry[12..]), payload,
                (TerminalImageProtocol)BitConverter.ToInt32(entry[28..]));
            _cachedImage = length <= CacheBudget / sizeof(char) ? image : null;
            _cachedImageIndex = _cachedImage is null ? -1 : index;
            return image;
        }
    }
}
