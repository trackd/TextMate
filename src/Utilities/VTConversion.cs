using System.Runtime.CompilerServices;
using System.Text;
using Spectre.Console;

namespace PSTextMate.Helpers;

/// <summary>
/// Efficient parser for VT (Virtual Terminal) escape sequences that converts them to Spectre.Console objects.
/// Supports RGB colors, 256-color palette, 3-bit colors, and text decorations.
/// </summary>
public static class VTParser {
    private const char ESC = '\x1B';
    private const char CSI_START = '[';
    private const char OSC_START = ']';
    private const char SGR_END = 'm';

    /// <summary>
    /// Parses a string containing VT escape sequences and returns a Paragraph object.
    /// Optimized single-pass streaming implementation that avoids intermediate collections.
    /// This is more efficient than ToMarkup() as it directly constructs the Paragraph
    /// without intermediate markup string generation, parsing, or segment collection.
    /// </summary>
    /// <param name="input">Input string with VT escape sequences</param>
    /// <returns>Paragraph object with parsed styles applied</returns>
    public static Paragraph ToParagraph(string input) {
        if (string.IsNullOrEmpty(input))
            return new Paragraph();

        var paragraph = new Paragraph();
        ReadOnlySpan<char> span = input.AsSpan();
        var currentStyle = new StyleState();
        int textStart = 0;
        int i = 0;

        while (i < span.Length) {
            if (span[i] == ESC && i + 1 < span.Length) {
                if (span[i + 1] == CSI_START) {
                    // Append text segment before escape sequence
                    if (i > textStart) {
                        string text = input[textStart..i];
                        if (currentStyle.HasAnyStyle) {
                            paragraph.Append(text, currentStyle.ToSpectreStyle());
                        }
                        else {
                            paragraph.Append(text, Style.Plain);
                        }
                    }

                    // Parse CSI escape sequence
                    int escapeEnd = ParseEscapeSequence(span, i, ref currentStyle);
                    if (escapeEnd > i) {
                        i = escapeEnd;
                        textStart = i;
                    }
                    else {
                        i++;
                    }
                }
                else if (span[i + 1] == OSC_START) {
                    // Append text segment before OSC sequence
                    if (i > textStart) {
                        string text = input[textStart..i];
                        if (currentStyle.HasAnyStyle) {
                            paragraph.Append(text, currentStyle.ToSpectreStyle());
                        }
                        else {
                            paragraph.Append(text, Style.Plain);
                        }
                    }

                    // Parse OSC sequence
                    OscResult oscResult = ParseOscSequence(span, i, ref currentStyle);
                    if (oscResult.End > i) {
                        // If we found hyperlink text, add it as a segment
                        if (!string.IsNullOrEmpty(oscResult.LinkText)) {
                            if (currentStyle.HasAnyStyle) {
                                paragraph.Append(oscResult.LinkText, currentStyle.ToSpectreStyle());
                            }
                            else {
                                paragraph.Append(oscResult.LinkText, Style.Plain);
                            }
                        }
                        i = oscResult.End;
                        textStart = i;
                    }
                    else {
                        i++;
                    }
                }
                else {
                    i++;
                }
            }
            else {
                i++;
            }
        }

        // Append remaining text
        if (textStart < span.Length) {
            string text = input[textStart..];
            if (currentStyle.HasAnyStyle) {
                paragraph.Append(text, currentStyle.ToSpectreStyle());
            }
            else {
                paragraph.Append(text, Style.Plain);
            }
        }

        return paragraph;
    }

    /// <summary>
    /// Parses a single VT escape sequence and updates the style state.
    /// Uses stack-allocated parameter array for efficient memory usage.
    /// Returns the index after the escape sequence.
    /// </summary>
    private static int ParseEscapeSequence(ReadOnlySpan<char> span, int start, ref StyleState style) {
        int i = start + 2; // Skip ESC[
        const int MaxEscapeSequenceLength = 1024;

        // Stack-allocate parameter array (SGR sequences typically have < 16 parameters)
        Span<int> parameters = stackalloc int[16];
        int paramCount = 0;
        int currentNumber = 0;
        bool hasNumber = false;
        int escapeLength = 0;

        // Parse parameters (numbers separated by semicolons or colons)
        while (i < span.Length && span[i] != SGR_END && escapeLength < MaxEscapeSequenceLength) {
            if (IsDigit(span[i])) {
                // Overflow-safe parsing per XenoAtom pattern
                int digit = span[i] - '0';
                if (currentNumber > (int.MaxValue - digit) / 10) {
                    currentNumber = int.MaxValue;  // Clamp instead of overflow
                }
                else {
                    currentNumber = (currentNumber * 10) + digit;
                }
                hasNumber = true;
            }
            // Support both ; and : as separators (SGR uses : for hyperlinks)
            else if (span[i] is ';' or ':') {
                if (paramCount < parameters.Length) {
                    parameters[paramCount++] = hasNumber ? currentNumber : 0;
                }
                currentNumber = 0;
                hasNumber = false;
            }
            else {
                // Invalid character, abort parsing
                return start + 1;
            }
            i++;
            escapeLength++;
        }

        if (i >= span.Length || span[i] != SGR_END) {
            // Invalid sequence
            return start + 1;
        }

        // Add the last parameter
        if (paramCount < parameters.Length) {
            parameters[paramCount++] = hasNumber ? currentNumber : 0;
        }

        // Apply SGR parameters to style (using slice of actual parameters)
        ApplySgrParameters(parameters[..paramCount], ref style);

        return i + 1; // Return position after 'm'
    }

    /// <summary>
    /// Result of parsing an OSC sequence.
    /// </summary>
    private readonly struct OscResult(int end, string? linkText = null) {
        public readonly int End = end;
        public readonly string? LinkText = linkText;
    }

    /// <summary>
    /// Parses an OSC (Operating System Command) sequence and updates the style state.
    /// Returns the result containing end position and any link text found.
    /// Safety limits prevent memory exhaustion from malformed sequences.
    /// </summary>
    private static OscResult ParseOscSequence(ReadOnlySpan<char> span, int start, ref StyleState style) {
        int i = start + 2; // Skip ESC]
        const int MaxOscLength = 32768;
        int oscLength = 0;

        // Check if this is OSC 8 (hyperlink)
        if (i < span.Length && span[i] == '8' && i + 1 < span.Length && span[i + 1] == ';') {
            i += 2; // Skip "8;"

            // Parse hyperlink sequence: ESC]8;params;url ESC\text ESC]8;; ESC\
            int urlEnd = -1;

            // Find the semicolon that separates params from URL
            while (i < span.Length && span[i] != ';' && oscLength < MaxOscLength) {
                i++;
                oscLength++;
            }

            if (i < span.Length && span[i] == ';') {
                i++; // Skip the semicolon
                oscLength++;
                int urlStart = i;

                // Find the end of the URL (look for ESC\)
                while (i < span.Length - 1 && oscLength < MaxOscLength) {
                    if (span[i] == ESC && span[i + 1] == '\\') {
                        urlEnd = i;
                        break;
                    }
                    i++;
                    oscLength++;
                }

                if (urlEnd > urlStart && urlEnd - urlStart < MaxOscLength) {
                    string url = span[urlStart..urlEnd].ToString();
                    i = urlEnd + 2; // Skip ESC\

                    // Check if this is a link start (has URL) or link end (empty)
                    if (!string.IsNullOrEmpty(url)) {
                        // This is a link start - find the link text and end sequence
                        int linkTextStart = i;
                        int linkTextEnd = -1;

                        // Look for the closing OSC sequence: ESC]8;;ESC\
                        while (i < span.Length - 6 && oscLength < MaxOscLength)  // Need at least 6 chars for ESC]8;;ESC\
                        {
                            if (span[i] == ESC && span[i + 1] == OSC_START &&
                                span[i + 2] == '8' && span[i + 3] == ';' &&
                                span[i + 4] == ';' && span[i + 5] == ESC &&
                                span[i + 6] == '\\') {
                                linkTextEnd = i;
                                break;
                            }
                            i++;
                            oscLength++;
                        }

                        if (linkTextEnd > linkTextStart) {
                            string linkText = span[linkTextStart..linkTextEnd].ToString();
                            style.Link = url;
                            return new OscResult(linkTextEnd + 7, linkText); // Skip ESC]8;;ESC\
                        }
                    }
                    else {
                        // This is likely a link end sequence: ESC]8;;ESC\
                        style.Link = null;
                        return new OscResult(i);
                    }
                }
            }
        }

        // If we can't parse the OSC sequence, skip to the next ESC\ or end of string
        while (i < span.Length - 1 && oscLength < MaxOscLength) {
            if (span[i] == ESC && span[i + 1] == '\\') {
                return new OscResult(i + 2);
            }
            i++;
            oscLength++;
        }

        return new OscResult(start + 1); // Failed to parse, advance by 1
    }

    /// <summary>
    /// Applies SGR (Select Graphic Rendition) parameters to the style state.
    /// Optimized to work with Span instead of List for zero-allocation processing.
    /// </summary>
    private static void ApplySgrParameters(ReadOnlySpan<int> parameters, ref StyleState style) {
        for (int i = 0; i < parameters.Length; i++) {
            int param = parameters[i];

            switch (param) {
                case 0:
                    // Reset
                    style.Reset();
                    break;
                case 1:
                    // Bold
                    style.Decoration |= Decoration.Bold;
                    break;
                case 2:
                    // Dim
                    style.Decoration |= Decoration.Dim;
                    break;
                case 3:
                    // Italic
                    style.Decoration |= Decoration.Italic;
                    break;
                case 4:
                    // Underline
                    style.Decoration |= Decoration.Underline;
                    break;
                case 5:
                    // Slow blink
                    style.Decoration |= Decoration.SlowBlink;
                    break;
                case 6:
                    // Rapid blink
                    style.Decoration |= Decoration.RapidBlink;
                    break;
                case 7:
                    // Reverse video
                    style.Decoration |= Decoration.Invert;
                    break;
                case 8:
                    // Conceal
                    style.Decoration |= Decoration.Conceal;
                    break;
                case 9:
                    // Strikethrough
                    style.Decoration |= Decoration.Strikethrough;
                    break;
                case 22:
                    // Normal intensity (not bold or dim)
                    style.Decoration &= ~(Decoration.Bold | Decoration.Dim);
                    break;
                case 23:
                    // Not italic
                    style.Decoration &= ~Decoration.Italic;
                    break;
                case 24:
                    // Not underlined
                    style.Decoration &= ~Decoration.Underline;
                    break;
                case 25:
                    // Not blinking
                    style.Decoration &= ~(Decoration.SlowBlink | Decoration.RapidBlink);
                    break;
                case 27:
                    // Not reversed
                    style.Decoration &= ~Decoration.Invert;
                    break;
                case 28:
                    // Not concealed
                    style.Decoration &= ~Decoration.Conceal;
                    break;
                case 29:
                    // Not strikethrough
                    style.Decoration &= ~Decoration.Strikethrough;
                    break;
                case >= 30 and <= 37:
                    // 3-bit foreground colors
                    style.Foreground = GetConsoleColor(param);
                    break;
                case 38:
                    // Extended foreground color
                    if (i + 1 < parameters.Length) {
                        int colorType = parameters[i + 1];
                        if (colorType == 2 && i + 4 < parameters.Length) {
                            // RGB
                            byte r = (byte)Math.Clamp(parameters[i + 2], 0, 255);
                            byte g = (byte)Math.Clamp(parameters[i + 3], 0, 255);
                            byte b = (byte)Math.Clamp(parameters[i + 4], 0, 255);
                            style.Foreground = new Color(r, g, b);
                            i += 4;
                        }
                        else if (colorType == 5 && i + 2 < parameters.Length) {
                            // 256-color
                            int colorIndex = parameters[i + 2];
                            style.Foreground = Get256Color(colorIndex);
                            i += 2;
                        }
                    }
                    break;
                case 39:
                    // Default foreground color
                    style.Foreground = null;
                    break;
                case >= 40 and <= 47:
                    // 3-bit background colors
                    style.Background = GetConsoleColor(param);
                    break;
                case 48:
                    // Extended background color
                    if (i + 1 < parameters.Length) {
                        int colorType = parameters[i + 1];
                        if (colorType == 2 && i + 4 < parameters.Length) // RGB
                        {
                            byte r = (byte)Math.Clamp(parameters[i + 2], 0, 255);
                            byte g = (byte)Math.Clamp(parameters[i + 3], 0, 255);
                            byte b = (byte)Math.Clamp(parameters[i + 4], 0, 255);
                            style.Background = new Color(r, g, b);
                            i += 4;
                        }
                        else if (colorType == 5 && i + 2 < parameters.Length) // 256-color
                        {
                            int colorIndex = parameters[i + 2];
                            style.Background = Get256Color(colorIndex);
                            i += 2;
                        }
                    }
                    break;
                case 49:
                    // Default background color
                    style.Background = null;
                    break;
                case >= 90 and <= 97:
                    // High intensity 3-bit foreground colors
                    style.Foreground = GetConsoleColor(param);
                    break;
                case >= 100 and <= 107:
                    // High intensity 3-bit background colors
                    style.Background = GetConsoleColor(param);
                    break;
                default:
                    break;
            }
        }
    }

    /// <summary>
    /// Gets a Color object for standard console colors.
    /// </summary>
    /// <param name="code"></param>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Color GetConsoleColor(int code) => code switch {
        30 or 40 => Color.Black,
        31 or 41 => Color.DarkRed,
        32 or 42 => Color.DarkGreen,
        33 or 43 => Color.Olive,
        34 or 44 => Color.DarkBlue,
        35 or 45 => Color.Purple,
        36 or 46 => Color.Teal,
        37 or 47 => Color.Silver,
        90 or 100 => Color.Grey,
        91 or 101 => Color.Red,
        92 or 102 => Color.Green,
        93 or 103 => Color.Yellow,
        94 or 104 => Color.Blue,
        95 or 105 => Color.Fuchsia,
        96 or 106 => Color.Aqua,
        97 or 107 => Color.White,
        _ => Color.Default
        // 30 or 40 => Color.Black,
        // 31 or 41 => Color.Red,
        // 32 or 42 => Color.Green,
        // 33 or 43 => Color.Yellow,
        // 34 or 44 => Color.Blue,
        // 35 or 45 => Color.Purple,
        // 36 or 46 => Color.Teal,
        // 37 or 47 => Color.White,
        // 90 or 100 => Color.Grey,
        // 91 or 101 => Color.Red1,
        // 92 or 102 => Color.Green1,
        // 93 or 103 => Color.Yellow1,
        // 94 or 104 => Color.Blue1,
        // 95 or 105 => Color.Fuchsia,
        // 96 or 106 => Color.Aqua,
        // 97 or 107 => Color.White,
        // _ => Color.Default
        // From ConvertFrom-ConsoleColor.ps1
    };

    /// <summary>
    /// Gets a Color object for 256-color palette.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Color Get256Color(int index) {
        if (index is < 0 or > 255)
            return Color.Default;

        // Standard 16 colors
        if (index < 16) {
            return index switch {
                0 => Color.Black,
                1 => Color.Maroon,
                2 => Color.Green,
                3 => Color.Olive,
                4 => Color.Navy,
                5 => Color.Purple,
                6 => Color.Teal,
                7 => Color.Silver,
                8 => Color.Grey,
                9 => Color.Red,
                10 => Color.Lime,
                11 => Color.Yellow,
                12 => Color.Blue,
                13 => Color.Fuchsia,
                14 => Color.Aqua,
                15 => Color.White,
                _ => Color.Default
            };
        }

        // 216 color cube (16-231)
        if (index < 232) {
            int colorIndex = index - 16;
            byte r = (byte)(colorIndex / 36 * 51);
            byte g = (byte)(colorIndex % 36 / 6 * 51);
            byte b = (byte)(colorIndex % 6 * 51);
            return new Color(r, g, b);
        }

        // Grayscale (232-255)
        byte gray = (byte)(((index - 232) * 10) + 8);
        return new Color(gray, gray, gray);
    }

    /// <summary>
    /// Checks if a character is a digit.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool IsDigit(char c) => (uint)(c - '0') <= 9;

    /// <summary>
    /// Represents the current style state during parsing.
    /// Uses mutable fields with init properties for efficient parsing.
    /// </summary>
    private struct StyleState {
        public Color? Foreground;
        public Color? Background;
        public Decoration Decoration;
        public string? Link;

        public readonly bool HasAnyStyle =>
            Foreground.HasValue || Background.HasValue ||
            Decoration != Decoration.None || Link is not null;

        public void Reset() {
            Foreground = null;
            Background = null;
            Decoration = Decoration.None;
            Link = null;
        }

        public readonly Style ToSpectreStyle() =>
            new(Foreground, Background, Decoration, Link);

        public readonly string ToMarkup() {
            // Use StringBuilder to avoid List<string> allocation
            // Typical markup is <64 chars, so inline capacity avoids resizing
            var sb = new StringBuilder(64);

            if (Foreground.HasValue) {
                sb.Append(Foreground.Value.ToMarkup());
            }
            else {
                sb.Append("Default ");
            }

            if (Background.HasValue) {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append("on ").Append(Background.Value.ToMarkup());
            }

            if (Decoration != Decoration.None) {
                if ((Decoration & Decoration.Bold) != 0) {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append("bold");
                }
                if ((Decoration & Decoration.Dim) != 0) {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append("dim");
                }
                if ((Decoration & Decoration.Italic) != 0) {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append("italic");
                }
                if ((Decoration & Decoration.Underline) != 0) {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append("underline");
                }
                if ((Decoration & Decoration.Strikethrough) != 0) {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append("strikethrough");
                }
                if ((Decoration & Decoration.SlowBlink) != 0) {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append("slowblink");
                }
                if ((Decoration & Decoration.RapidBlink) != 0) {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append("rapidblink");
                }
                if ((Decoration & Decoration.Invert) != 0) {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append("invert");
                }
                if ((Decoration & Decoration.Conceal) != 0) {
                    if (sb.Length > 0) sb.Append(' ');
                    sb.Append("conceal");
                }
            }

            if (!string.IsNullOrEmpty(Link)) {
                if (sb.Length > 0) sb.Append(' ');
                sb.Append("link=").Append(Link);
            }

            return sb.ToString();
        }
    }
}
