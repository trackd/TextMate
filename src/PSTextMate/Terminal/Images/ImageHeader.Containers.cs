using System.Buffers.Binary;

namespace PSTextMate.Core;

internal static partial class ImageHeader {
    private static bool ReadRiff(Stream stream, long start, ReadOnlySpan<byte> header,
        out ImageDimensions dimensions) {
        dimensions = default;
        uint size = BinaryPrimitives.ReadUInt32LittleEndian(header[4..]);
        if (size < 4 || size > stream.Length - start - 8)
            return false;
        long end = start + 8 + size;
        // RIFF sizes and directory offsets can span pixel payloads. Bound metadata work,
        // rather than rejecting legitimate headers located late in a large file.
        int budget = MaximumEntries;
        bool webp = header.Slice(8, 4).SequenceEqual("WEBP"u8);
        if (!webp && !header.Slice(8, 4).SequenceEqual("ACON"u8))
            return false;
        Span<byte> chunk = stackalloc byte[10];
        long position = start + 12;
        while (budget-- > 0 && position < end) {
            if (!ReadAt(stream, position, end, chunk[..8]))
                return false;
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
            long content = stream.Position;
            long next = content + length + (length & 1);
            if (next > end)
                return false;
            if (webp) {
                if (chunk[..4].SequenceEqual("VP8X"u8)) {
                    if (length < 10 || !ReadAt(stream, content, end, chunk))
                        return false;
                    int width = 1 + chunk[4] + (chunk[5] << 8) + (chunk[6] << 16);
                    int height = 1 + chunk[7] + (chunk[8] << 8) + (chunk[9] << 16);
                    return (long)width * height <= uint.MaxValue && SetDimensions(width, height, out dimensions);
                }
                if (chunk[..4].SequenceEqual("VP8L"u8)) {
                    if (length < 5 || !ReadAt(stream, content, end, chunk[..5]) || chunk[0] != 0x2f)
                        return false;
                    uint packed = BinaryPrimitives.ReadUInt32LittleEndian(chunk[1..]);
                    return (packed >> 29) == 0 && SetDimensions(1 + (packed & 0x3fff), 1 + ((packed >> 14) & 0x3fff), out dimensions);
                }
                if (chunk[..4].SequenceEqual("VP8 "u8)) {
                    if (length < 10 || !ReadAt(stream, content, end, chunk)
                        || (chunk[0] & 1) != 0 || chunk[3] != 0x9d || chunk[4] != 1 || chunk[5] != 0x2a)
                        return false;
                    return SetDimensions(BinaryPrimitives.ReadUInt16LittleEndian(chunk[6..]) & 0x3fff,
                        BinaryPrimitives.ReadUInt16LittleEndian(chunk[8..]) & 0x3fff, out dimensions);
                }
            }
            else if (chunk[..4].SequenceEqual("LIST"u8)) {
                if (length < 4 || !ReadAt(stream, content, end, chunk[..4]))
                    return false;
                if (chunk[..4].SequenceEqual("fram"u8)) {
                    if (!ReadAniFrames(stream, content + 4, content + length, ref budget, out ImageDimensions frame))
                        return false;
                    dimensions = new(Math.Max(dimensions.Width, frame.Width), Math.Max(dimensions.Height, frame.Height));
                }
            }
            position = next;
        }
        return !webp && position == end && dimensions.Width > 0 && dimensions.Height > 0;
    }

    private static bool ReadAniFrames(Stream stream, long position, long end, ref int budget, out ImageDimensions dimensions) {
        dimensions = default;
        Span<byte> chunk = stackalloc byte[8];
        while (budget-- > 0 && position < end) {
            if (!ReadAt(stream, position, end, chunk))
                return false;
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]);
            long content = stream.Position;
            long next = content + length + (length & 1);
            if (next > end)
                return false;
            if (chunk[..4].SequenceEqual("icon"u8)) {
                if (!ReadAniFrame(stream, content, content + length, ref budget, out ImageDimensions frame))
                    return false;
                dimensions = new(Math.Max(dimensions.Width, frame.Width), Math.Max(dimensions.Height, frame.Height));
            }
            position = next;
        }
        return position == end && dimensions.Width > 0 && dimensions.Height > 0;
    }

    private static bool ReadAniFrame(Stream stream, long start, long end, ref int budget, out ImageDimensions dimensions) {
        dimensions = default;
        Span<byte> header = stackalloc byte[12];
        if (!ReadAt(stream, start, end, header))
            return false;
        uint dib = BinaryPrimitives.ReadUInt32LittleEndian(header);
        if (dib is not 12 and not 16 and not 40 and not 52 and not 56 and not 64 and not 108 and not 124)
            return ReadIcon(stream, start, end, ref budget, out dimensions);
        if (dib > end - start)
            return false;
        // AF_ICON-free ANI frames are raw bitmaps; their height has no icon mask.
        if (dib == 12)
            return SetDimensions(BinaryPrimitives.ReadUInt16LittleEndian(header[4..]),
                BinaryPrimitives.ReadUInt16LittleEndian(header[6..]), out dimensions);
        long height = BinaryPrimitives.ReadInt32LittleEndian(header[8..]);
        return SetDimensions(BinaryPrimitives.ReadInt32LittleEndian(header[4..]), Math.Abs(height), out dimensions);
    }

    private static bool ReadIcon(Stream stream, long start, long limit, out ImageDimensions dimensions) {
        int budget = MaximumEntries;
        return ReadIcon(stream, start, limit, ref budget, out dimensions);
    }

    private static bool ReadIcon(Stream stream, long start, long limit, ref int budget, out ImageDimensions dimensions) {
        dimensions = default;
        Span<byte> entry = stackalloc byte[32];
        if (!ReadAt(stream, start, limit, entry[..6]) || entry[0] != 0 || entry[1] != 0
            || entry[2] is not 1 and not 2 || entry[3] != 0)
            return false;
        int count = BinaryPrimitives.ReadUInt16LittleEndian(entry[4..]);
        if (count == 0 || count > budget || count > (limit - start - 6) / 16)
            return false;
        budget -= count;
        for (int index = 0; index < count; index++) {
            if (!ReadAt(stream, start + 6 + index * 16, limit, entry[..16]))
                return false;
            uint size = BinaryPrimitives.ReadUInt32LittleEndian(entry[8..]);
            uint offset = BinaryPrimitives.ReadUInt32LittleEndian(entry[12..]);
            if (offset < 6 + count * 16 || offset > limit - start || size > limit - start - offset)
                return false;
            long payloadLimit = start + offset + size;
            if (!ReadAt(stream, start + offset, payloadLimit, entry[..12]))
                return false;
            ImageDimensions frame;
            if (BinaryPrimitives.ReadUInt64BigEndian(entry) == 0x89504e470d0a1a0aUL) {
                if (!ReadAt(stream, start + offset, payloadLimit, entry[..24])
                    || BinaryPrimitives.ReadUInt32BigEndian(entry[8..]) != 13 || !entry.Slice(12, 4).SequenceEqual("IHDR"u8)
                    || !SetDimensions(BinaryPrimitives.ReadUInt32BigEndian(entry[16..]), BinaryPrimitives.ReadUInt32BigEndian(entry[20..]), out frame))
                    return false;
            }
            else {
                uint dib = BinaryPrimitives.ReadUInt32LittleEndian(entry);
                if (dib > size)
                    return false;
                int width;
                int height;
                if (dib == 12) {
                    width = BinaryPrimitives.ReadUInt16LittleEndian(entry[4..]);
                    height = BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]);
                }
                else if (dib == 16 || dib >= 40) {
                    width = BinaryPrimitives.ReadInt32LittleEndian(entry[4..]);
                    height = BinaryPrimitives.ReadInt32LittleEndian(entry[8..]);
                }
                else
                    return false;
                // Icon DIBs store the color image followed by its equally tall mask.
                if ((height & 1) != 0 || !SetDimensions(width, height / 2, out frame))
                    return false;
            }
            dimensions = new(Math.Max(dimensions.Width, frame.Width), Math.Max(dimensions.Height, frame.Height));
        }
        return true;
    }
}
