using System.Buffers.Binary;
using System.Runtime.CompilerServices;
using System.Text;
using PSTextMate.Core;
using PSTextMate.Images;
using Xunit;

namespace PSTextMate.InteractiveTests;

public sealed class ImageSizingTests {
    [Fact]
    public void HeaderProbe_ReadsOnlyThePrefixAndAllocatesNothingAfterWarmup() {
        byte[] bytes = new byte[1_048_576];
        Convert.FromHexString("89504e470d0a1a0a0000000d494844520000078000000438080600000000000000").CopyTo(bytes, 0);
        using var stream = new CountingStream(bytes);
        for (int i = 0; i < 100; i++)
            Assert.True(ImageHeader.TryRead(stream, out _));
        stream.BytesRead = 0;
        long before = GC.GetAllocatedBytesForCurrentThread();
        bool valid = true;
        for (int i = 0; i < 1000; i++)
            valid &= ImageHeader.TryRead(stream, out _);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.True(valid);
        Assert.Equal(0, allocated);
        Assert.Equal(32_000, stream.BytesRead);
        Assert.Equal(0, stream.Position);
    }

    [Theory]
    [InlineData("89504e470d0a1a0a0000000d494844520000078000000438080600000000000000", 1920, 1080)]
    [InlineData("4749463839618002e001000000", 640, 480)]
    [InlineData("47494638376101000200000000", 1, 2)]
    [InlineData("424d000000000000000000000000280000008002000020feffff000000000000", 640, 480)]
    [InlineData("424d0000000000000000000000000c0000008002e00100000000000000000000", 640, 480)]
    [InlineData("424d0000000000000000000000001000000080020000e001000001001800", 640, 480)]
    [InlineData("ffd8ffe000064a464946ffc2000b080438078001011100ffd9", 1920, 1080)]
    [InlineData("716f696600000280000001e00400", 640, 480)]
    [InlineData("0000020000000000000000008002e0011820", 640, 480)]
    [InlineData("00000a0000000000000000008002e0012028", 640, 480)]
    [InlineData("0000030000000000000000008002e0010800", 640, 480)]
    [InlineData("0001010000000100180000008002e0010800", 640, 480)]
    [InlineData("762f3101020000006461746157696e646f7700626f7832690010000000f6ffffffecffffff75020000cb010000", 640, 480)]
    public void ImageHeaders_ReadDimensionsWithoutPixelsAndRestoreStreamPosition(string hex, int width, int height) {
        byte[] header = Convert.FromHexString(hex);
        using var stream = new MemoryStream();
        stream.Write("prefix"u8);
        stream.Write(header);
        stream.Position = 6;
        Assert.True(ImageHeader.TryRead(stream, out ImageDimensions actual));
        Assert.Equal(new(width, height), actual);
        Assert.Equal(6, stream.Position);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    public void NetpbmHeaders_ReadEveryAsciiAndBinaryVariantWithComments(int variant) {
        byte[] header = Encoding.ASCII.GetBytes($"P{variant}\n# a comment\r\n640 # width\n480\n255\n");
        AssertHeader(header, new(640, 480));
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, false, true)]
    [InlineData(true, true, false)]
    [InlineData(false, true, false)]
    [InlineData(true, true, true)]
    [InlineData(false, true, true)]
    public void TiffHeaders_ReadBothByteOrdersClassicAndBigTiff(bool little, bool big, bool shortValues) {
        AssertHeader(Tiff(little, big, shortValues), new(640, 480));
    }

    [Theory]
    [InlineData("VP8 ", "0000009d012a8002e001")]
    [InlineData("VP8L", "2f7fc27700")]
    [InlineData("VP8X", "020000007f0200df0100")]
    public void WebpHeaders_ReadLossyLosslessAndAnimatedCanvasWithPaddedMetadata(string chunk, string payload) {
        byte[] bytes = Riff("WEBP", Chunk("JUNK", [42]), Chunk(chunk, Convert.FromHexString(payload)));
        AssertHeader(bytes, new(640, 480));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IconHeaders_UseLargestEmbeddedFrameIncludingPngLargerThanDirectoryBytes(bool cursor) {
        byte[] icon = Icon(cursor);
        AssertHeader(icon, new(640, 480));
    }

    [Fact]
    public void AniHeaders_ReadFrameResourcesWhenAnimationHeaderDimensionsAreZero() {
        byte[] ani = Riff("ACON", Chunk("anih", new byte[36]),
            Chunk("LIST", [.. "fram"u8, .. Chunk("icon", Icon(true))]));
        AssertHeader(ani, new(640, 480));
    }

    [Fact]
    public void NewHeaderPaths_AllocateNothingAfterWarmupAndDoNotReadPixelPayloads() {
        byte[][] headers = [
            Convert.FromHexString("716f696600000280000001e00400"),
            Convert.FromHexString("0000020000000000000000008002e0011820"),
            Convert.FromHexString("762f3101020000006461746157696e646f7700626f7832690010000000f6ffffffecffffff75020000cb010000"),
            "P6\n640 480\n255\n"u8.ToArray(),
            Tiff(true, false, false), Tiff(false, true, false),
            Riff("WEBP", Chunk("VP8X", Convert.FromHexString("000000007f0200df0100"))),
            Icon(false), Riff("ACON", Chunk("LIST", [.. "fram"u8, .. Chunk("icon", Icon(true))]))
        ];
        foreach (byte[] header in headers) {
            byte[] bytes = new byte[1_048_576];
            header.CopyTo(bytes, 0);
            using var stream = new CountingStream(bytes);
            for (int i = 0; i < 32; i++)
                Assert.True(ProbeBatch(stream));
            stream.BytesRead = 0;
            long before = GC.GetAllocatedBytesForCurrentThread();
            bool valid = ProbeBatch(stream);
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.True(valid);
            Assert.True(allocated == 0, $"Header {Convert.ToHexString(header)} allocated {allocated} bytes.");
            Assert.InRange(stream.BytesRead, 1, 256_000);
            Assert.Equal(0, stream.Position);
        }
    }

    [Fact]
    public void ContainerHeaders_DeclineTruncationAtEveryByteAndRestorePosition() {
        byte[][] headers = [Tiff(true, false, false), Tiff(false, true, false),
            Riff("WEBP", Chunk("VP8X", Convert.FromHexString("000000007f0200df0100"))), Icon(true),
            Riff("ACON", Chunk("LIST", [.. "fram"u8, .. Chunk("icon", Icon(true))]))];
        foreach (byte[] header in headers) {
            for (int size = 0; size < header.Length; size++) {
                using var stream = new MemoryStream(header, 0, size);
                Assert.False(ImageHeader.TryRead(stream, out _));
                Assert.Equal(0, stream.Position);
            }
        }
    }

    [Fact]
    public void TiffHeaders_SeekToLateDirectoryWithoutReadingInterveningPixels() {
        byte[] bytes = new byte[2_097_152];
        byte[] header = Tiff(true, false, false);
        header.AsSpan(0, 8).CopyTo(bytes);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(4), 1_048_576);
        header.AsSpan(8).CopyTo(bytes.AsSpan(1_048_576));
        using var stream = new CountingStream(bytes);
        Assert.True(ImageHeader.TryRead(stream, out ImageDimensions dimensions));
        Assert.Equal(new(640, 480), dimensions);
        Assert.InRange(stream.BytesRead, 1, 100);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void IconHeaders_SeekToLateEmbeddedFramesWithoutReadingInterveningPixels() {
        byte[] bytes = new byte[2_097_152];
        byte[] icon = Icon(false);
        icon.CopyTo(bytes, 0);
        icon.AsSpan(78, 24).CopyTo(bytes.AsSpan(1_048_576));
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(34), 1_048_576);
        using var stream = new CountingStream(bytes);
        Assert.True(ImageHeader.TryRead(stream, out ImageDimensions dimensions));
        Assert.Equal(new(640, 480), dimensions);
        Assert.InRange(stream.BytesRead, 1, 160);
        Assert.Equal(0, stream.Position);
    }

    [Fact]
    public void ContainerHeaders_RejectExcessiveMetadataWork() {
        byte[] tiff = Tiff(true, false, false);
        BinaryPrimitives.WriteUInt16LittleEndian(tiff.AsSpan(8), 4097);
        using var directory = new CountingStream(tiff);
        Assert.False(ImageHeader.TryRead(directory, out _));
        Assert.Equal(0, directory.Position);
        byte[][] chunks = Enumerable.Repeat(Chunk("JUNK", []), 4097)
            .Append(Chunk("VP8X", Convert.FromHexString("000000007f0200df0100"))).ToArray();
        using var riff = new CountingStream(Riff("WEBP", chunks));
        Assert.False(ImageHeader.TryRead(riff, out _));
        Assert.InRange(riff.BytesRead, 1, 32 + 4096 * 8);
        Assert.Equal(0, riff.Position);
    }

    [Theory]
    [InlineData(1920, 1080, 10, 20, 100, 20, 72, 20)]
    [InlineData(1920, 1080, 10, 20, 100, 10, 36, 10)]
    [InlineData(400, 100, 10, 20, 100, 30, 40, 5)]
    [InlineData(100, 4000, 10, 20, 100, 10, 1, 10)]
    [InlineData(1920, 1080, 11, 23, 129, 10, 34, 9)]
    public void ImageRequests_FitPixelAspectRatioInsideAvailableCellsWithoutUpscaling(int width, int height,
        int cellWidth, int cellHeight, int columns, int rows, int expectedColumns, int expectedRows) {
        var cells = new TerminalGraphics(cellWidth == 11, cellWidth, cellHeight);
        Assert.Equal((expectedColumns, expectedRows), ImageSizing.Fit(new(width, height), cells, columns, rows));
    }

    [Theory]
    [InlineData("")]
    [InlineData("89504e470d0a1a0a0000000d49484452")]
    [InlineData("89504e470d0a1a0a0000000d494844520000000000000001")]
    [InlineData("4749463839610000e001")]
    [InlineData("ffd8ffe000010000")]
    [InlineData("ffd8ffda0008000000000000")]
    [InlineData("424d000000000000000000000000280000008002000000000080000000000000")]
    [InlineData("716f696680000000000001e00400")]
    [InlineData("716f696600000280000001e00200")]
    [InlineData("0000020000000000000000000000e0011820")]
    [InlineData("50360a3231343734383336343820310a")]
    [InlineData("50360a36343020343830")]
    [InlineData("762f3101020000006461746157696e646f7700626f78326900100000000000000000000000ffffffff01000000")]
    public void UnknownTruncatedOrInvalidHeaders_DeclineAndLeaveTheStreamUntouched(string hex) {
        using var stream = new MemoryStream(Convert.FromHexString(hex));
        Assert.False(ImageHeader.TryRead(stream, out _));
        Assert.Equal(0, stream.Position);
    }

    private static void AssertHeader(byte[] header, ImageDimensions expected) {
        using var stream = new MemoryStream();
        stream.Write("prefix"u8);
        stream.Write(header);
        stream.Position = 6;
        Assert.True(ImageHeader.TryRead(stream, out ImageDimensions actual));
        Assert.Equal(expected, actual);
        Assert.Equal(6, stream.Position);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static bool ProbeBatch(Stream stream) {
        bool valid = true;
        for (int index = 0; index < 1000; index++)
            valid &= ImageHeader.TryRead(stream, out _);
        return valid;
    }

    private static byte[] Tiff(bool little, bool big, bool shortValues) {
        int headerSize = big ? 16 : 8;
        int countSize = big ? 8 : 2;
        int entrySize = big ? 20 : 12;
        byte[] bytes = new byte[headerSize + countSize + 2 * entrySize];
        (little ? "II"u8 : "MM"u8).CopyTo(bytes);
        Put(bytes.AsSpan(2), big ? 43UL : 42UL, 2, little);
        if (big) {
            Put(bytes.AsSpan(4), 8, 2, little);
            Put(bytes.AsSpan(8), (ulong)headerSize, 8, little);
        }
        else
            Put(bytes.AsSpan(4), (ulong)headerSize, 4, little);
        Put(bytes.AsSpan(headerSize), 2, countSize, little);
        for (int index = 0; index < 2; index++) {
            Span<byte> entry = bytes.AsSpan(headerSize + countSize + index * entrySize);
            Put(entry, (ulong)(256 + index), 2, little);
            Put(entry[2..], shortValues ? 3UL : big ? 16UL : 4UL, 2, little);
            Put(entry[4..], 1, big ? 8 : 4, little);
            Put(entry[(big ? 12 : 8)..], index == 0 ? 640UL : 480UL, shortValues ? 2 : big ? 8 : 4, little);
        }
        return bytes;
    }

    private static void Put(Span<byte> bytes, ulong value, int size, bool little) {
        for (int index = 0; index < size; index++)
            bytes[little ? index : size - 1 - index] = (byte)(value >> (index * 8));
    }

    private static byte[] Chunk(string tag, byte[] content) {
        byte[] bytes = new byte[8 + content.Length + (content.Length & 1)];
        Encoding.ASCII.GetBytes(tag, bytes);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), content.Length);
        content.CopyTo(bytes, 8);
        return bytes;
    }

    private static byte[] Riff(string form, params byte[][] chunks) {
        using var stream = new MemoryStream();
        stream.Write("RIFF\0\0\0\0"u8);
        stream.Write(Encoding.ASCII.GetBytes(form));
        foreach (byte[] chunk in chunks)
            stream.Write(chunk);
        byte[] bytes = stream.ToArray();
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4), bytes.Length - 8);
        return bytes;
    }

    private static byte[] Icon(bool cursor) {
        byte[] bytes = new byte[6 + 32 + 40 + 24];
        bytes[2] = cursor ? (byte)2 : (byte)1;
        bytes[4] = 2;
        bytes[6] = 16;
        bytes[7] = 16;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(14), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(18), 38);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(30), 24);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(34), 78);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(38), 40);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(42), 16);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(46), 32);
        Convert.FromHexString("89504e470d0a1a0a0000000d4948445200000280000001e0").CopyTo(bytes, 78);
        return bytes;
    }

    private sealed class CountingStream(byte[] bytes) : MemoryStream(bytes) {
        internal int BytesRead;
        public override int ReadByte() {
            int value = base.ReadByte();
            if (value >= 0)
                BytesRead++;
            return value;
        }

        public override int Read(Span<byte> buffer) {
            int read = base.Read(buffer);
            BytesRead += read;
            return read;
        }
    }
}
