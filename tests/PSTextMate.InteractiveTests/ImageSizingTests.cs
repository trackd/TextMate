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
    [InlineData("ffd8ffe000064a464946ffc2000b080438078001011100ffd9", 1920, 1080)]
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
    public void UnknownTruncatedOrInvalidHeaders_DeclineAndLeaveTheStreamUntouched(string hex) {
        using var stream = new MemoryStream(Convert.FromHexString(hex));
        Assert.False(ImageHeader.TryRead(stream, out _));
        Assert.Equal(0, stream.Position);
    }

    private sealed class CountingStream(byte[] bytes) : MemoryStream(bytes) {
        internal int BytesRead;
        public override int Read(Span<byte> buffer) {
            int read = base.Read(buffer);
            BytesRead += read;
            return read;
        }
    }
}
