using System.Buffers.Binary;

namespace PSTextMate.Images;

internal static partial class ImageHeader {
    private static bool ReadAdditionalFormat(Stream stream, long start, long limit, ReadOnlySpan<byte> data,
        out ImageDimensions dimensions) {
        dimensions = default;
        if (data.Length >= 14 && data[..4].SequenceEqual("qoif"u8)) {
            return data[12] is 3 or 4 && data[13] <= 1
                && SetDimensions(BinaryPrimitives.ReadUInt32BigEndian(data[4..]),
                    BinaryPrimitives.ReadUInt32BigEndian(data[8..]), out dimensions);
        }
        if (data.Length >= 12 && data[..4].SequenceEqual("RIFF"u8))
            return ReadRiff(stream, start, data, out dimensions);
        if (data.Length >= 8 && (data[..2].SequenceEqual("II"u8) || data[..2].SequenceEqual("MM"u8)))
            return ReadTiff(stream, start, stream.Length, data, out dimensions);
        if (data.Length >= 6 && data[0] == 0 && data[1] == 0 && data[3] == 0 && data[2] is 1 or 2
            && BinaryPrimitives.ReadUInt16LittleEndian(data[4..]) != 0)
            return ReadIcon(stream, start, stream.Length, out dimensions);
        if (data.Length >= 8 && BinaryPrimitives.ReadUInt32LittleEndian(data) == 0x01312f76)
            return ReadExr(stream, start, limit, data, out dimensions);
        if (data.Length >= 3 && data[0] == 'P' && data[1] is >= (byte)'1' and <= (byte)'6' && IsWhitespace(data[2])) {
            stream.Position = start + 2;
            return ReadPnmNumber(stream, limit, out int width) && ReadPnmNumber(stream, limit, out int height)
                && SetDimensions(width, height, out dimensions);
        }
        // TGA has no magic signature. Check its structural discriminator last.
        if (data.Length >= 18 && data[1] <= 1 && data[2] is 1 or 2 or 3 or 9 or 10 or 11
            && data[16] is 8 or 15 or 16 or 24 or 32 && (data[17] & 0xc0) == 0
            && (data[1] == 1 || BinaryPrimitives.ReadUInt32LittleEndian(data[3..]) == 0 && data[7] == 0)
            && (data[2] is 1 or 9 ? data[1] == 1 : data[1] == 0)) {
            return SetDimensions(BinaryPrimitives.ReadUInt16LittleEndian(data[12..]),
                BinaryPrimitives.ReadUInt16LittleEndian(data[14..]), out dimensions);
        }
        return false;
    }

    private static bool SetDimensions(long width, long height, out ImageDimensions dimensions) {
        dimensions = default;
        if (width is <= 0 or > int.MaxValue || height is <= 0 or > int.MaxValue)
            return false;
        dimensions = new((int)width, (int)height);
        return true;
    }

    private static bool ReadAt(Stream stream, long position, long limit, Span<byte> buffer) {
        if (position < 0 || position > limit || buffer.Length > limit - position)
            return false;
        stream.Position = position;
        return stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false) == buffer.Length;
    }

    private static bool IsWhitespace(int value) => value is 9 or 10 or 11 or 12 or 13 or 32;

    private static bool ReadPnmNumber(Stream stream, long limit, out int value) {
        value = 0;
        int current = -1;
        while (stream.Position < limit) {
            current = stream.ReadByte();
            if (current == '#') {
                while (stream.Position < limit && (current = stream.ReadByte()) is not -1 and not 10 and not 13) { }
            }
            if (!IsWhitespace(current))
                break;
        }
        int digits = 0;
        while (current is >= '0' and <= '9') {
            int digit = current - '0';
            if (value > (int.MaxValue - digit) / 10)
                return false;
            value = value * 10 + digit;
            digits++;
            if (stream.Position >= limit)
                return false;
            current = stream.ReadByte();
        }
        // A number must have a delimiter; a truncated last token is not a header.
        if (current == '#')
            stream.Position--;
        return digits > 0 && value > 0 && (IsWhitespace(current) || current == '#');
    }

    private static bool ReadExr(Stream stream, long start, long limit, ReadOnlySpan<byte> header,
        out ImageDimensions dimensions) {
        dimensions = default;
        if (header[4] != 2)
            return false;
        stream.Position = start + 8;
        Span<byte> name = stackalloc byte[256];
        Span<byte> type = stackalloc byte[256];
        Span<byte> value = stackalloc byte[16];
        for (int count = 0; count < MaximumEntries; count++) {
            if (!ReadTerminated(stream, limit, name, out int nameLength) || nameLength == 0
                || !ReadTerminated(stream, limit, type, out int typeLength)
                || !ReadAt(stream, stream.Position, limit, value[..4]))
                return false;
            uint length = BinaryPrimitives.ReadUInt32LittleEndian(value);
            long position = stream.Position;
            if (length > limit - position)
                return false;
            if (name[..nameLength].SequenceEqual("dataWindow"u8)) {
                if (!type[..typeLength].SequenceEqual("box2i"u8) || length != 16
                    || !ReadAt(stream, position, limit, value))
                    return false;
                long width = (long)BinaryPrimitives.ReadInt32LittleEndian(value[8..]) - BinaryPrimitives.ReadInt32LittleEndian(value) + 1;
                long height = (long)BinaryPrimitives.ReadInt32LittleEndian(value[12..]) - BinaryPrimitives.ReadInt32LittleEndian(value[4..]) + 1;
                return SetDimensions(width, height, out dimensions);
            }
            stream.Position = position + length;
        }
        return false;
    }

    private static bool ReadTerminated(Stream stream, long limit, Span<byte> buffer, out int length) {
        length = 0;
        while (length < buffer.Length && stream.Position < limit) {
            int value = stream.ReadByte();
            if (value < 0)
                return false;
            if (value == 0)
                return true;
            buffer[length++] = (byte)value;
        }
        return false;
    }
}
