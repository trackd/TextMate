namespace PSTextMate.Core;

public enum TextAlignment { Left, Center, Right }
public enum PanelBorder { Rounded, Square, DoubleLine, None }
public readonly record struct TextBounds(int Column, int Row, int Width, int Height);

/// <summary>Shared cell-based geometry used by composition and streaming renderers.</summary>
public static class TextGeometry {
    internal static int DistributeColumns(int width, int gap, ReadOnlySpan<int> requested, Span<int> allocated, out int actualGap) {
        actualGap = requested.Length > 1 ? Math.Min(gap, width / (requested.Length - 1)) : 0;
        int remaining = width - (actualGap * Math.Max(0, requested.Length - 1));
        int flexible = 0;
        allocated.Clear();
        for (int index = 0; index < requested.Length; index++) {
            if (requested[index] == 0) {
                flexible++;
            }
            else {
                allocated[index] = Math.Min(remaining, requested[index]);
                remaining -= allocated[index];
            }
        }
        for (int index = 0; index < requested.Length; index++) {
            if (requested[index] != 0)
                continue;
            allocated[index] = remaining / flexible;
            remaining -= allocated[index];
            flexible--;
        }
        return remaining;
    }

    public static int AlignmentOffset(int available, int occupied, TextAlignment alignment) {
        int space = Math.Max(0, available - occupied);
        return alignment switch {
            TextAlignment.Left => 0,
            TextAlignment.Center => space / 2,
            TextAlignment.Right => space,
            _ => throw new ArgumentOutOfRangeException(nameof(alignment))
        };
    }

    public static void WritePadding(TextWriter writer, int count, char character = ' ') {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        Span<char> buffer = stackalloc char[64];
        buffer.Fill(character);
        while (count > 0) {
            int length = Math.Min(count, buffer.Length);
            writer.Write(buffer[..length]);
            count -= length;
        }
    }
}

/// <summary>Panel border geometry, usable independently of the pager or rendering framework.</summary>
public static class PanelFrame {
    internal static string Characters(PanelBorder border) => border switch {
        PanelBorder.Rounded => "╭╮╰╯─│",
        PanelBorder.Square => "┌┐└┘─│",
        PanelBorder.DoubleLine => "╔╗╚╝═║",
        PanelBorder.None => "      ",
        _ => throw new ArgumentOutOfRangeException(nameof(border))
    };

    public static string Border(int width, string? title = null, bool bottom = false,
        PanelBorder border = PanelBorder.Rounded, TextAlignment alignment = TextAlignment.Left) {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        string characters = Characters(border);
        if (width == 0)
            return string.Empty;
        if (width == 1)
            return characters[bottom ? 2 : 0].ToString();
        string safe = SafeTitle(title ?? string.Empty);
        int inner = width - 2;
        int prefix = safe.Length == 0 || inner == 0 ? 0 : 1;
        int fit = TerminalCells.Fit(safe, Math.Max(0, inner - prefix));
        int cells = TerminalCells.Measure(safe.AsSpan(0, fit));
        int left = TextGeometry.AlignmentOffset(inner, cells + prefix, alignment);
        return string.Create(width + fit - cells, (characters, safe, fit, left, prefix, bottom), static (destination, state) => {
            destination.Fill(state.characters[4]);
            destination[0] = state.characters[state.bottom ? 2 : 0];
            destination[^1] = state.characters[state.bottom ? 3 : 1];
            state.safe.AsSpan(0, state.fit).CopyTo(destination[(1 + state.left + state.prefix)..]);
        });
    }

    public static void WriteBorder(TextWriter writer, int width, ReadOnlySpan<char> title = default, bool bottom = false,
        PanelBorder border = PanelBorder.Rounded, TextAlignment alignment = TextAlignment.Left) {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        string characters = Characters(border);
        if (width == 0)
            return;
        writer.Write(characters[bottom ? 2 : 0]);
        if (width == 1)
            return;
        int inner = width - 2;
        foreach (char value in title) {
            if (char.IsControl(value)) {
                title = SafeTitle(title);
                break;
            }
        }
        int prefix = title.IsEmpty || inner == 0 ? 0 : 1;
        title = title[..TerminalCells.Fit(title, Math.Max(0, inner - prefix))];
        int occupied = TerminalCells.Measure(title) + prefix;
        int left = TextGeometry.AlignmentOffset(inner, occupied, alignment);
        TextGeometry.WritePadding(writer, left + prefix, characters[4]);
        writer.Write(title);
        TextGeometry.WritePadding(writer, inner - left - occupied, characters[4]);
        writer.Write(characters[bottom ? 3 : 1]);
    }

    internal static string SafeTitle(ReadOnlySpan<char> title) {
        foreach (char value in title) {
            if (char.IsControl(value)) {
                return string.Create(title.Length, title.ToString(), static (destination, source) => {
                    for (int index = 0; index < source.Length; index++)
                        destination[index] = char.IsControl(source[index]) ? TerminalText.Visible(source[index]) : source[index];
                });
            }
        }
        return title.ToString();
    }

    internal static string SafeTitle(string title) {
        foreach (char value in title) {
            if (char.IsControl(value))
                return SafeTitle(title.AsSpan());
        }

        return title;
    }

    public static void WriteRowStart(TextWriter writer, int width, int leftPadding = 0, PanelBorder border = PanelBorder.Rounded) {
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(leftPadding);
        if (width == 0)
            return;
        writer.Write(Characters(border)[5]);
        TextGeometry.WritePadding(writer, Math.Min(leftPadding, Math.Max(0, width - 2)));
    }

    /// <summary>Writes the end of a pre-clipped row. Content width is measured in cells, excluding VT escapes.</summary>
    public static void WriteRowEnd(TextWriter writer, int width, int contentColumns, int leftPadding = 0,
        PanelBorder border = PanelBorder.Rounded) {
        ArgumentOutOfRangeException.ThrowIfNegative(width);
        ArgumentOutOfRangeException.ThrowIfNegative(contentColumns);
        ArgumentOutOfRangeException.ThrowIfNegative(leftPadding);
        TextGeometry.WritePadding(writer, Math.Max(0, width - contentColumns - leftPadding - 2));
        if (width > 1)
            writer.Write(Characters(border)[5]);
    }
}

/// <summary>One line-number gutter implementation for native renderers, composed output and the pager.</summary>
/// <remarks>A number of 0 draws the gutter of a wrapped continuation row: separator, no number.</remarks>
public static class LineNumberGutter {
    public static int Digits(int total) {
        ArgumentOutOfRangeException.ThrowIfLessThan(total, 1);
        int digits = 1;
        while (total >= 10) {
            total /= 10;
            digits++;
        }
        return digits;
    }

    public static int Width(int total) => Digits(total) + 3;

    private static int FormatNumber(Span<char> buffer, int number) {
        ArgumentOutOfRangeException.ThrowIfNegative(number);
        if (number == 0)
            return 0;
        number.TryFormat(buffer, out int written, provider: CultureInfo.InvariantCulture);
        return written;
    }

    public static string Format(int number, int total) {
        int digits = Digits(Math.Max(Math.Max(1, number), total));
        Span<char> destination = stackalloc char[digits + 3];
        Format(destination, number, total);
        return destination.ToString();
    }

    public static void Write(TextWriter writer, int number, int total) {
        ArgumentNullException.ThrowIfNull(writer);
        Span<char> buffer = stackalloc char[10];
        int written = FormatNumber(buffer, number);
        TextGeometry.WritePadding(writer, Digits(Math.Max(Math.Max(1, number), total)) - written);
        writer.Write(buffer[..written]);
        writer.Write(" │ ");
    }

    public static int Format(Span<char> destination, int number, int total) {
        int digits = Digits(Math.Max(Math.Max(1, number), total));
        if (destination.Length < digits + 3)
            throw new ArgumentException("The gutter buffer is too small.", nameof(destination));
        Span<char> numberBuffer = stackalloc char[10];
        int written = FormatNumber(numberBuffer, number);
        destination[..(digits + 3)].Fill(' ');
        numberBuffer[..written].CopyTo(destination[(digits - written)..]);
        destination[digits + 1] = '│';
        return digits + 3;
    }

    internal static void Append(StringBuilder builder, int number, int total) {
        Span<char> buffer = stackalloc char[10];
        int written = FormatNumber(buffer, number);
        builder.Append(' ', Digits(Math.Max(Math.Max(1, number), total)) - written).Append(buffer[..written]).Append(" │ ");
    }
}
