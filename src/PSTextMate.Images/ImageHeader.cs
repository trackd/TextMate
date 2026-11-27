using System.Buffers.Binary;

namespace PSTextMate.Images;

internal readonly record struct ImageDimensions(int Width, int Height);

/// <summary>Reads dimensions from container headers without decompressing pixels or loading a codec.</summary>
internal static class ImageHeader {
    internal static bool TryRead(Stream stream, out ImageDimensions dimensions) {
        dimensions = default;
        if (!stream.CanRead || !stream.CanSeek)
            return false;
        long original = stream.Position;
        try {
            Span<byte> header = stackalloc byte[32];
            int length = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
            ReadOnlySpan<byte> data = header[..length];
            int width = 0;
            int height = 0;
            if (length >= 24 && BinaryPrimitives.ReadUInt64BigEndian(data) == 0x89504e470d0a1a0aUL
                && BinaryPrimitives.ReadUInt32BigEndian(data[8..]) == 13 && data.Slice(12, 4).SequenceEqual("IHDR"u8)) {
                width = BinaryPrimitives.ReadInt32BigEndian(data[16..]);
                height = BinaryPrimitives.ReadInt32BigEndian(data[20..]);
            }
            else if (length >= 10 && (data[..6].SequenceEqual("GIF87a"u8) || data[..6].SequenceEqual("GIF89a"u8))) {
                width = BinaryPrimitives.ReadUInt16LittleEndian(data[6..]);
                height = BinaryPrimitives.ReadUInt16LittleEndian(data[8..]);
            }
            else if (length >= 26 && data[..2].SequenceEqual("BM"u8)) {
                uint dib = BinaryPrimitives.ReadUInt32LittleEndian(data[14..]);
                if (dib == 12) {
                    width = BinaryPrimitives.ReadUInt16LittleEndian(data[18..]);
                    height = BinaryPrimitives.ReadUInt16LittleEndian(data[20..]);
                }
                else if (dib >= 40) {
                    width = BinaryPrimitives.ReadInt32LittleEndian(data[18..]);
                    int rawHeight = BinaryPrimitives.ReadInt32LittleEndian(data[22..]);
                    height = rawHeight == int.MinValue ? 0 : Math.Abs(rawHeight);
                }
            }
            else if (length >= 2 && data[0] == 0xff && data[1] == 0xd8) {
                stream.Position = original + 2;
                return ReadJpeg(stream, original, out dimensions);
            }
            if (width <= 0 || height <= 0)
                return false;
            dimensions = new(width, height);
            return true;
        }
        finally {
            stream.Position = original;
        }
    }

    private static bool ReadJpeg(Stream stream, long start, out ImageDimensions dimensions) {
        dimensions = default;
        long limit = Math.Min(stream.Length, start + 1_048_576);
        Span<byte> segment = stackalloc byte[7];
        for (int markerCount = 0; markerCount < 256 && stream.Position < limit; markerCount++) {
            if (stream.ReadByte() != 0xff)
                return false;
            int marker;
            do {
                marker = stream.ReadByte();
            } while (marker == 0xff && stream.Position < limit);
            if (marker is < 0 or 0 or 0xd9 or 0xda)
                return false;
            if (marker is 0x01 or (>= 0xd0 and <= 0xd7))
                continue;
            if (stream.ReadAtLeast(segment[..2], 2, throwOnEndOfStream: false) != 2)
                return false;
            int length = BinaryPrimitives.ReadUInt16BigEndian(segment);
            if (length < 2 || stream.Position + length - 2 > limit)
                return false;
            if (marker is >= 0xc0 and <= 0xcf and not 0xc4 and not 0xc8 and not 0xcc) {
                if (length < 8 || stream.ReadAtLeast(segment[..6], 6, throwOnEndOfStream: false) < 6)
                    return false;
                int height = BinaryPrimitives.ReadUInt16BigEndian(segment[1..]);
                int width = BinaryPrimitives.ReadUInt16BigEndian(segment[3..]);
                if (width <= 0 || height <= 0 || length != 8 + (3 * segment[5]))
                    return false;
                dimensions = new(width, height);
                return true;
            }
            stream.Seek(length - 2, SeekOrigin.Current);
        }
        return false;
    }
}
