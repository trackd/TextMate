using System.Buffers.Binary;

namespace PSTextMate.Images;

internal static partial class ImageHeader {
    private static bool ReadTiff(Stream stream, long start, long limit, ReadOnlySpan<byte> header,
        out ImageDimensions dimensions) {
        dimensions = default;
        bool little = header[0] == 'I';
        ushort version = ReadUInt16(header[2..], little);
        bool big = version == 43;
        if (version != 42 && !big || big && (header.Length < 16 || ReadUInt16(header[4..], little) != 8
            || ReadUInt16(header[6..], little) != 0))
            return false;
        ulong offset = big ? ReadUInt64(header[8..], little) : ReadUInt32(header[4..], little);
        int headerSize = big ? 16 : 8;
        if (offset < (ulong)headerSize || offset > (ulong)(limit - start))
            return false;
        Span<byte> entry = stackalloc byte[20];
        int countSize = big ? 8 : 2;
        if (!ReadAt(stream, start + (long)offset, limit, entry[..countSize]))
            return false;
        ulong count = big ? ReadUInt64(entry, little) : ReadUInt16(entry, little);
        int entrySize = big ? 20 : 12;
        if (count > MaximumEntries || count > (ulong)((limit - stream.Position) / entrySize))
            return false;
        long width = 0;
        long height = 0;
        for (ulong index = 0; index < count; index++) {
            if (!ReadAt(stream, stream.Position, limit, entry[..entrySize]))
                return false;
            ushort tag = ReadUInt16(entry, little);
            if (tag is not 256 and not 257)
                continue;
            ushort type = ReadUInt16(entry[2..], little);
            ulong values = big ? ReadUInt64(entry[4..], little) : ReadUInt32(entry[4..], little);
            if (values != 1)
                return false;
            ReadOnlySpan<byte> value = entry[(big ? 12 : 8)..];
            ulong size;
            if (type == 3)
                size = ReadUInt16(value, little);
            else if (type == 4)
                size = ReadUInt32(value, little);
            else if (type == 16 && big)
                size = ReadUInt64(value, little);
            else
                return false;
            if (size is 0 or > int.MaxValue)
                return false;
            if (tag == 256)
                width = (long)size;
            else
                height = (long)size;
            if (width > 0 && height > 0)
                return SetDimensions(width, height, out dimensions);
        }
        return false;
    }

    private static ushort ReadUInt16(ReadOnlySpan<byte> bytes, bool little) => little
        ? BinaryPrimitives.ReadUInt16LittleEndian(bytes) : BinaryPrimitives.ReadUInt16BigEndian(bytes);

    private static uint ReadUInt32(ReadOnlySpan<byte> bytes, bool little) => little
        ? BinaryPrimitives.ReadUInt32LittleEndian(bytes) : BinaryPrimitives.ReadUInt32BigEndian(bytes);

    private static ulong ReadUInt64(ReadOnlySpan<byte> bytes, bool little) => little
        ? BinaryPrimitives.ReadUInt64LittleEndian(bytes) : BinaryPrimitives.ReadUInt64BigEndian(bytes);
}
