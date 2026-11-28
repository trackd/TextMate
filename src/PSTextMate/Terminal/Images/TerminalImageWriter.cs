namespace PSTextMate.Core;

/// <summary>
/// Frames an encoded image file for terminals that decode it themselves. No pixels are decoded or resampled:
/// the terminal scales the image into the requested cell rectangle.
/// </summary>
internal static class TerminalImageWriter {
    private const int KittyChunk = 4096;

    internal enum Format { Unknown, Png, Jpeg, Gif }

    internal static Format Detect(ReadOnlySpan<byte> header) =>
        header.StartsWith((ReadOnlySpan<byte>)[0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a]) ? Format.Png
        : header.StartsWith((ReadOnlySpan<byte>)[0xff, 0xd8, 0xff]) ? Format.Jpeg
        : header.StartsWith("GIF87a"u8) || header.StartsWith("GIF89a"u8) ? Format.Gif
        : Format.Unknown;

    /// <summary>
    /// Kitty graphics: a direct PNG transfer displayed in exactly <paramref name="columns"/> x <paramref name="rows"/>
    /// cells, in 4096-character chunks, quiet, without moving the cursor. Kitty decodes PNG only.
    /// </summary>
    internal static string Kitty(ReadOnlySpan<byte> png, int columns, int rows) {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        string data = Convert.ToBase64String(png);
        var output = new StringBuilder(data.Length + (data.Length / KittyChunk * 20) + 64);
        int position = 0;
        do {
            int count = Math.Min(KittyChunk, data.Length - position);
            bool more = position + count < data.Length;
            output.Append("\u001b_G");
            if (position == 0) {
                output.Append("a=T,f=100,t=d,q=2,C=1,c=").Append(columns.ToString(CultureInfo.InvariantCulture))
                    .Append(",r=").Append(rows.ToString(CultureInfo.InvariantCulture)).Append(',');
            }
            else {
                output.Append("q=2,");
            }
            output.Append(more ? "m=1;" : "m=0;").Append(data, position, count).Append("\u001b\\");
            position += count;
        } while (position < data.Length);
        return output.ToString();
    }

    /// <summary>iTerm2 inline image (also WezTerm, VS Code and mintty) in a cell rectangle, keeping its aspect ratio.</summary>
    internal static string InlineImage(ReadOnlySpan<byte> image, int columns, int rows) {
        ArgumentOutOfRangeException.ThrowIfLessThan(columns, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(rows, 1);
        return string.Concat("\u001b]1337;File=inline=1;size=", image.Length.ToString(CultureInfo.InvariantCulture),
            ";width=", columns.ToString(CultureInfo.InvariantCulture), ";height=", rows.ToString(CultureInfo.InvariantCulture),
            ";preserveAspectRatio=1:", Convert.ToBase64String(image), "\a");
    }
}
