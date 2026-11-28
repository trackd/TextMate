using System.IO.Compression;

namespace PSTextMate.Core;

/// <summary>
/// Recognizes complete inline image sequences in captured terminal output and measures their cell footprint,
/// without decoding pixels. Only sequences that draw inline data at the cursor are accepted. Sequences that read
/// files, query the terminal (its reply would arrive as keyboard input), download files or change other terminal
/// state are rejected, and the caller drops them as it drops any other control sequence.
/// </summary>
internal static class TerminalImageParser {
    /// <summary>Upper bound for one image sequence, in UTF-16 code units.</summary>
    internal const int MaximumLength = 64 * 1024 * 1024;
    private const int MaximumCells = 1000;
    private const string StringTerminator = "\u001b\\";
    private static readonly SearchValues<char> Base64 = SearchValues.Create("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/=");
    private static readonly SearchValues<char> Parameters = SearchValues.Create("0123456789;");
    private static readonly SearchValues<char> ControlValue = SearchValues.Create("0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ-");

    /// <summary>
    /// Cell metrics used to turn pixel sizes into cells: the image module's, else the size the operating system
    /// reports, else 10x20 like the Sixel module.
    /// </summary>
    internal readonly record struct CellMetrics(int CellWidth, int CellHeight, int Columns = 0, int Rows = 0) {
        internal static CellMetrics From(TerminalGraphics graphics, int columns = 0, int rows = 0) {
            if (graphics.HasCellSize)
                return new(graphics.CellWidth, graphics.CellHeight, columns, rows);
            return TerminalCellSize.TryQuery(out int width, out int height) ? new(width, height, columns, rows) : new(10, 20, columns, rows);
        }

        /// <summary>The probed cell size, else the operating system's, else 10x20. Never queries the terminal.</summary>
        internal static CellMetrics Current(int columns = 0, int rows = 0) {
            TerminalCapabilities capabilities = TerminalCapabilities.Current;
            return capabilities.CellSizeMeasured ? new(capabilities.CellWidth, capabilities.CellHeight, columns, rows) : From(default, columns, rows);
        }
        internal int Width => CellWidth > 0 ? CellWidth : 10;
        internal int Height => CellHeight > 0 ? CellHeight : 20;
    }

    /// <summary>True when <paramref name="text"/> starts like an image sequence; other OSC, DCS and APC commands are not.</summary>
    internal static bool MayStartImage(ReadOnlySpan<char> text) => text.Length >= 3 && text[0] == '\u001b' && text[1] switch {
        'P' => true,
        '_' => text[2] == 'G',
        ']' => text[2..].StartsWith("1337;File=", StringComparison.Ordinal),
        _ => false
    };

    /// <summary>Reads one image sequence starting at <paramref name="text"/>[0].</summary>
    internal static bool TryRead(ReadOnlySpan<char> text, CellMetrics cells, out int consumed, out TerminalImage? image) {
        consumed = 0;
        image = null;
        if (!MayStartImage(text))
            return false;
        try {
            return text[1] switch {
                'P' when text.Length > 3 && text[2] == '!' && text[3] == 'g' => TryReadGoodImage(text, cells, out consumed, out image),
                'P' => TryReadSixel(text, cells, out consumed, out image),
                '_' => TryReadKitty(text, cells, out consumed, out image),
                _ => TryReadInlineImage(text, cells, out consumed, out image)
            };
        }
        catch (Exception error) when (error is FormatException or OverflowException or InvalidDataException or ArgumentException) {
            consumed = 0;
            image = null;
            return false;
        }
    }

    /// <summary>Finds the first image sequence anywhere in module output, ignoring cursor commands around it.</summary>
    internal static bool TryFind(string text, CellMetrics cells, out TerminalImage? image) {
        for (int index = text.IndexOf('\u001b'); index >= 0; index = text.IndexOf('\u001b', index + 1)) {
            if (TryRead(text.AsSpan(index), cells, out _, out image))
                return true;
        }
        image = null;
        return false;
    }

    // DCS P1;P2;P3 q "Pan;Pad;Ph;Pv data ST
    private static bool TryReadSixel(ReadOnlySpan<char> text, CellMetrics cells, out int consumed, out TerminalImage? image) {
        consumed = 0;
        image = null;
        int end = FindTerminator(text, 2);
        if (end < 0)
            return false;
        ReadOnlySpan<char> body = text[2..end];
        int command = body.IndexOf('q');
        if (command is < 0 or > 32 || body[..command].ContainsAnyExcept(Parameters))
            return false;
        ReadOnlySpan<char> data = body[(command + 1)..];
        foreach (char value in data) {
            if (value is < ' ' or > '~')
                return false;
        }
        (int width, int height) = SixelPixels(data);
        if (width <= 0 || height <= 0)
            return false;
        // Terminals draw whole six-pixel bands, so the last band can reach into one more row.
        height = (int)Math.Min(int.MaxValue, ((long)height + 5) / 6 * 6);
        consumed = end + StringTerminator.Length;
        image = Create(PixelsToCells(width, cells.Width), PixelsToCells(height, cells.Height),
            text[..consumed].ToString(), TerminalImageProtocol.Sixel);
        return true;
    }

    private static (int Width, int Height) SixelPixels(ReadOnlySpan<char> data) {
        if (data.Length > 0 && data[0] == '"') {
            // Raster attributes: aspect numerator;denominator;width;height in pixels.
            int stop = data[1..].IndexOfAnyExcept(Parameters) + 1;
            ReadOnlySpan<char> raster = stop > 0 ? data[1..stop] : data[1..];
            Span<Range> parts = stackalloc Range[5];
            if (raster.Split(parts, ';') >= 4
                && int.TryParse(raster[parts[2]], NumberStyles.None, CultureInfo.InvariantCulture, out int rasterWidth)
                && int.TryParse(raster[parts[3]], NumberStyles.None, CultureInfo.InvariantCulture, out int rasterHeight)
                && rasterWidth > 0 && rasterHeight > 0) {
                return (rasterWidth, rasterHeight);
            }
        }
        // No raster attributes: measure the widest band and count the bands.
        int width = 0, column = 0, bands = 1, repeat = 0;
        bool drawn = false;
        for (int index = 0; index < data.Length; index++) {
            char value = data[index];
            if (value == '#') {
                while (index + 1 < data.Length && (char.IsAsciiDigit(data[index + 1]) || data[index + 1] == ';'))
                    index++;
            }
            else if (value == '!') {
                repeat = 0;
                while (index + 1 < data.Length && char.IsAsciiDigit(data[index + 1]))
                    repeat = Math.Min(65535, (repeat * 10) + data[++index] - '0');
                continue;
            }
            else if (value == '$') {
                column = 0;
            }
            else if (value == '-') {
                column = 0;
                bands++;
            }
            else if (value is >= '?' and <= '~') {
                column += Math.Max(1, repeat);
                width = Math.Max(width, column);
                drawn = true;
            }
            repeat = 0;
        }
        return drawn ? (width, bands * 6) : (0, 0);
    }

    // APC G key=value,...;base64 ST, repeated while m=1.
    private static bool TryReadKitty(ReadOnlySpan<char> text, CellMetrics cells, out int consumed, out TerminalImage? image) {
        consumed = 0;
        image = null;
        var payload = new StringBuilder();
        var data = new StringBuilder();
        Dictionary<char, string>? first = null;
        int position = 0;
        while (true) {
            ReadOnlySpan<char> rest = text[position..];
            if (rest.Length < 3 || rest[0] != '\u001b' || rest[1] != '_' || rest[2] != 'G')
                return false;
            int end = FindTerminator(rest, 3);
            if (end < 0)
                return false;
            ReadOnlySpan<char> body = rest[3..end];
            int separator = body.IndexOf(';');
            ReadOnlySpan<char> control = separator < 0 ? body : body[..separator];
            ReadOnlySpan<char> chunk = separator < 0 ? [] : body[(separator + 1)..];
            if (!TryParseControls(control, ',', out Dictionary<char, string> values))
                return false;
            if (first is null) {
                // Display inline data at the cursor only: no file, temporary file or shared-memory media,
                // no queries, deletions, animation or placements of images transmitted elsewhere.
                if (!values.TryGetValue('a', out string? action) || action != "T"
                    || (values.TryGetValue('t', out string? medium) && medium != "d")
                    || values.ContainsKey('U') || values.ContainsKey('P') || values.ContainsKey('Q')) {
                    return false;
                }
                first = values;
            }
            else if (values.Keys.Any(static key => key is not ('m' or 'q'))) {
                return false;
            }
            if (chunk.ContainsAnyExcept(Base64))
                return false;
            // Never ask the terminal for a reply: it would arrive on standard input as key presses.
            values['q'] = "2";
            payload.Append("\u001b_G").AppendJoin(',', values.Select(static pair => pair.Key + "=" + pair.Value));
            if (!chunk.IsEmpty)
                payload.Append(';').Append(chunk);
            payload.Append(StringTerminator);
            if (data.Length < 64 * 1024)
                data.Append(chunk);
            position += end + StringTerminator.Length;
            if (position > MaximumLength)
                return false;
            if (!values.TryGetValue('m', out string? more) || more != "1")
                break;
        }
        (int columns, int rows) = KittyCells(first, data.ToString(), cells);
        if (columns <= 0 || rows <= 0)
            return false;
        consumed = position;
        image = Create(columns, rows, payload.ToString(), TerminalImageProtocol.Kitty);
        return true;
    }

    private static (int Columns, int Rows) KittyCells(Dictionary<char, string> values, string data, CellMetrics cells) {
        int columns = Number(values, 'c');
        int rows = Number(values, 'r');
        if (columns > 0 && rows > 0)
            return (columns, rows);
        // Source size: the crop rectangle, the raw pixel size, or the PNG header of the payload.
        (int width, int height) = (Number(values, 'w'), Number(values, 'h'));
        if (width <= 0 || height <= 0) {
            (width, height) = (Number(values, 's'), Number(values, 'v'));
            if ((width <= 0 || height <= 0) && (!values.TryGetValue('f', out string? format) || format == "100")) {
                ImageDimensions dimensions = default;
                bool compressed = values.TryGetValue('o', out string? compression) && compression == "z";
                if (!TryReadHeader(data, compressed, out dimensions))
                    return (0, 0);
                (width, height) = (dimensions.Width, dimensions.Height);
            }
        }
        return FitCells(columns, rows, width, height, cells, aspect: true);
    }

    // OSC 1337 ; File = key=value;... : base64 (BEL | ST)
    private static bool TryReadInlineImage(ReadOnlySpan<char> text, CellMetrics cells, out int consumed, out TerminalImage? image) {
        consumed = 0;
        image = null;
        const string prefix = "\u001b]1337;File=";
        if (!text.StartsWith(prefix, StringComparison.Ordinal))
            return false;
        int bell = text.IndexOf('\a');
        int st = text.IndexOf(StringTerminator, StringComparison.Ordinal);
        int end = bell < 0 ? st : st < 0 ? bell : Math.Min(bell, st);
        if (end < 0 || end > MaximumLength)
            return false;
        ReadOnlySpan<char> body = text[prefix.Length..end];
        int colon = body.IndexOf(':');
        if (colon < 0)
            return false;
        var arguments = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (Range part in body[..colon].Split(';')) {
            ReadOnlySpan<char> entry = body[..colon][part];
            int equals = entry.IndexOf('=');
            if (equals <= 0)
                return false;
            arguments[entry[..equals].ToString()] = entry[(equals + 1)..].ToString();
        }
        // inline=0 (the default) asks the terminal to download the file: never pass that through.
        if (!arguments.TryGetValue("inline", out string? inline) || inline != "1")
            return false;
        ReadOnlySpan<char> data = body[(colon + 1)..];
        if (data.IsEmpty || data.ContainsAnyExcept(Base64))
            return false;
        int columns = InlineSize(arguments.GetValueOrDefault("width"), cells.Width, cells.Columns);
        int rows = InlineSize(arguments.GetValueOrDefault("height"), cells.Height, cells.Rows);
        if (columns < 0 || rows < 0)
            return false;
        int width = 0, height = 0;
        if ((columns == 0 || rows == 0 || arguments.GetValueOrDefault("preserveAspectRatio") != "0")
            && TryReadHeader(data.ToString(), compressed: false, out ImageDimensions dimensions)) {
            (width, height) = (dimensions.Width, dimensions.Height);
        }
        (columns, rows) = columns > 0 && rows > 0 ? (columns, rows) : FitCells(columns, rows, width, height, cells, aspect: true);
        if (columns <= 0 || rows <= 0)
            return false;
        consumed = end + (text[end] == '\a' ? 1 : StringTerminator.Length);
        image = Create(columns, rows, text[..consumed].ToString(), TerminalImageProtocol.InlineImage);
        return true;
    }

    /// <summary>N cells, Npx pixels, N% of the terminal, or auto (0). -1 for an invalid size.</summary>
    private static int InlineSize(string? value, int cellPixels, int terminalCells) {
        if (string.IsNullOrEmpty(value) || value == "auto")
            return 0;
        ReadOnlySpan<char> span = value;
        if (span.EndsWith("px", StringComparison.Ordinal))
            return int.TryParse(span[..^2], NumberStyles.None, CultureInfo.InvariantCulture, out int pixels) ? PixelsToCells(pixels, cellPixels) : -1;
        if (span.EndsWith('%')) {
            if (!int.TryParse(span[..^1], NumberStyles.None, CultureInfo.InvariantCulture, out int percent) || percent > 100)
                return -1;
            return terminalCells > 0 ? Math.Max(1, terminalCells * percent / 100) : -1;
        }
        return int.TryParse(span, NumberStyles.None, CultureInfo.InvariantCulture, out int count) ? count : -1;
    }

    // DCS ! g header,header;!base64 ST
    private static bool TryReadGoodImage(ReadOnlySpan<char> text, CellMetrics cells, out int consumed, out TerminalImage? image) {
        consumed = 0;
        image = null;
        int end = FindTerminator(text, 4);
        if (end < 0)
            return false;
        ReadOnlySpan<char> body = text[4..end];
        int bodyStart = body.IndexOf(";!", StringComparison.Ordinal);
        ReadOnlySpan<char> headerText = bodyStart < 0 ? body : body[..bodyStart];
        ReadOnlySpan<char> data = bodyStart < 0 ? [] : body[(bodyStart + 2)..];
        var headers = new List<(string Key, string? Value)>();
        foreach (Range part in headerText.Split(',')) {
            ReadOnlySpan<char> entry = headerText[part];
            if (entry.IsEmpty)
                continue;
            int equals = entry.IndexOf('=');
            headers.Add(equals < 0 ? (entry.ToString(), null) : (entry[..equals].ToString(), entry[(equals + 1)..].ToString()));
        }
        // Only the one-shot render carries its own image; named uploads, renders and queries manage terminal state.
        if (!headers.Any(static header => header is ("o", "s")) || data.IsEmpty
            || data.ContainsAnyExcept(Base64)) {
            return false;
        }
        // s requests a status reply on standard input; l scrolls the page. The placement owns neither.
        headers.RemoveAll(static header => header.Key is "s" or "l");
        string? Value(string key) => headers.LastOrDefault(header => header.Key == key).Value;
        int columns = ParseNumber(Value("c"));
        int rows = ParseNumber(Value("r"));
        if (columns <= 0 || rows <= 0) {
            int width = ParseNumber(Value("w"));
            int height = ParseNumber(Value("h"));
            if ((width <= 0 || height <= 0) && TryReadHeader(data.ToString(), compressed: false, out ImageDimensions dimensions))
                (width, height) = (dimensions.Width, dimensions.Height);
            (columns, rows) = FitCells(columns, rows, width, height, cells, aspect: true);
        }
        if (columns <= 0 || rows <= 0)
            return false;
        consumed = end + StringTerminator.Length;
        string payload = "\u001bP!g" + string.Join(',', headers.Select(static header => header.Value is null ? header.Key : header.Key + "=" + header.Value))
            + ";!" + data.ToString() + StringTerminator;
        image = Create(columns, rows, payload, TerminalImageProtocol.GoodImage);
        return true;
    }

    private static TerminalImage? Create(int columns, int rows, string payload, TerminalImageProtocol protocol) =>
        columns is > 0 and <= MaximumCells && rows is > 0 and <= MaximumCells ? new TerminalImage(columns, rows, payload.AsMemory(), protocol) : null;

    /// <summary>Cells for a pixel size, keeping its aspect ratio when only one cell count is given.</summary>
    private static (int Columns, int Rows) FitCells(int columns, int rows, int width, int height, CellMetrics cells, bool aspect) {
        if (width <= 0 || height <= 0)
            return columns > 0 && rows > 0 ? (columns, rows) : (0, 0);
        if (columns > 0 && rows <= 0 && aspect)
            rows = PixelsToCells((int)Math.Min(int.MaxValue, (long)columns * cells.Width * height / width), cells.Height);
        else if (rows > 0 && columns <= 0 && aspect)
            columns = PixelsToCells((int)Math.Min(int.MaxValue, (long)rows * cells.Height * width / height), cells.Width);
        if (columns <= 0)
            columns = PixelsToCells(width, cells.Width);
        if (rows <= 0)
            rows = PixelsToCells(height, cells.Height);
        // Terminals scale an image that is wider than the screen down to fit.
        if (cells.Columns > 0 && columns > cells.Columns) {
            rows = Math.Max(1, (int)Math.Ceiling((double)rows * cells.Columns / columns));
            columns = cells.Columns;
        }
        return (columns, rows);
    }

    private static int PixelsToCells(int pixels, int cellPixels) => pixels <= 0 ? 0 : (int)Math.Min(int.MaxValue, ((long)pixels + cellPixels - 1) / cellPixels);

    /// <summary>Reads the image header from the start of a base64 payload; never decodes the whole image.</summary>
    private static bool TryReadHeader(string base64, bool compressed, out ImageDimensions dimensions) {
        dimensions = default;
        int length = Math.Min(base64.Length, 4 * 1024 * 1024) / 4 * 4;
        if (length == 0)
            return false;
        byte[] bytes = new byte[length / 4 * 3];
        if (!Convert.TryFromBase64Chars(base64.AsSpan(0, length), bytes, out int written)
            && !Convert.TryFromBase64Chars(base64.AsSpan(0, length).TrimEnd('='), bytes, out written)) {
            return false;
        }
        using var stream = new MemoryStream(bytes, 0, written, writable: false);
        if (!compressed)
            return ImageHeader.TryRead(stream, out dimensions);
        using var inflated = new MemoryStream();
        using (var zlib = new ZLibStream(stream, CompressionMode.Decompress)) {
            Span<byte> buffer = stackalloc byte[4096];
            try {
                int read;
                while (inflated.Length < 1024 * 1024 && (read = zlib.Read(buffer)) > 0)
                    inflated.Write(buffer[..read]);
            }
            catch (InvalidDataException) {
                // A truncated prefix still yields the header bytes inflated so far.
            }
        }
        inflated.Position = 0;
        return ImageHeader.TryRead(inflated, out dimensions);
    }

    private static int FindTerminator(ReadOnlySpan<char> text, int start) {
        int end = text[start..].IndexOf(StringTerminator, StringComparison.Ordinal);
        return end < 0 || start + end > MaximumLength ? -1 : start + end;
    }

    private static bool TryParseControls(ReadOnlySpan<char> control, char separator, out Dictionary<char, string> values) {
        values = [];
        foreach (Range part in control.Split(separator)) {
            ReadOnlySpan<char> entry = control[part];
            if (entry.Length < 3 || entry[1] != '=' || !char.IsAsciiLetter(entry[0]) || values.ContainsKey(entry[0])
                || entry[2..].ContainsAnyExcept(ControlValue)) {
                return false;
            }
            values.Add(entry[0], entry[2..].ToString());
        }
        return true;
    }

    private static int Number(Dictionary<char, string> values, char key) =>
        values.TryGetValue(key, out string? value) ? ParseNumber(value) : 0;

    private static int ParseNumber(string? value) =>
        int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out int number) ? number : 0;
}
