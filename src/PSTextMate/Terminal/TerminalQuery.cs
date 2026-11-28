using System.Diagnostics;

namespace PSTextMate.Core;

/// <summary>The console as seen by a terminal query. Tests substitute a scripted terminal.</summary>
internal interface ITerminalQueryTransport {
    bool IsRedirected { get; }
    bool KeyAvailable { get; }
    long ElapsedMilliseconds { get; }
    char Read();
    void Write(string text);
    void Wait();
    IDisposable? EnterQueryMode();
}

internal sealed class ConsoleQueryTransport : ITerminalQueryTransport {
    private readonly Stopwatch _timer = Stopwatch.StartNew();
    public bool IsRedirected => Console.IsInputRedirected || Console.IsOutputRedirected;
    public bool KeyAvailable => Console.KeyAvailable;
    public long ElapsedMilliseconds => _timer.ElapsedMilliseconds;
    public char Read() => Console.ReadKey(intercept: true).KeyChar;
    public void Write(string text) {
        Console.Out.Write(text);
        Console.Out.Flush();
    }
    public void Wait() => Thread.Sleep(1);
    public IDisposable? EnterQueryMode() => ConsoleModeLease.EnterQuery();
}

/// <summary>
/// One bounded batch of terminal queries, ported from the Sixel module. Device attributes (DA1) come last and
/// fence the batch: every terminal answers them, so once they arrive no late reply can reach the line editor.
/// One batch, one timeout, no retry.
/// </summary>
internal static class TerminalQuery {
    internal const int TimeoutMilliseconds = 1500;
    internal const uint KittyQueryId = 31;

    /// <summary>Kitty graphics query, XTVERSION, cell size, window pixels, window cells, then DA1.</summary>
    internal const string Probe = "\u001b_Gi=31,s=1,v=1,a=q,t=d,f=24;AAAA\u001b\\\u001b[>q\u001b[16t\u001b[14t\u001b[18t\u001b[c";

    /// <summary>The replies, ending with DA1; empty when the terminal did not answer in time or input was lost.</summary>
    internal static string Read(ITerminalQueryTransport transport) {
        try {
            // Pending input belongs to the user: never drain it to make room for replies.
            if (transport.IsRedirected || transport.KeyAvailable)
                return string.Empty;
            using IDisposable? mode = transport.EnterQueryMode();
            var result = new StringBuilder();
            var sequence = new StringBuilder();
            long started = transport.ElapsedMilliseconds;
            transport.Write(Probe);
            while (transport.ElapsedMilliseconds - started < TimeoutMilliseconds) {
                if (!transport.KeyAvailable) {
                    transport.Wait();
                    continue;
                }
                char value = transport.Read();
                if (sequence.Length == 0 && value != '\u001b')
                    return string.Empty; // The user typed: input ownership was lost.
                sequence.Append(value);
                if (sequence.Length > 4096)
                    return string.Empty;
                if (!IsComplete(sequence))
                    continue;
                string reply = sequence.ToString();
                sequence.Clear();
                result.Append(reply);
                if (reply.StartsWith("\u001b[?", StringComparison.Ordinal) && reply[^1] == 'c')
                    return result.ToString();
            }
        }
        catch (InvalidOperationException) {
        }
        catch (IOException) {
        }
        return string.Empty;
    }

    private static bool IsComplete(StringBuilder sequence) {
        int length = sequence.Length;
        if (length < 3)
            return false;
        char last = sequence[length - 1];
        if (sequence[1] == '[')
            return last is >= '@' and <= '~';
        return (last == '\\' && sequence[length - 2] == '\u001b') || (sequence[1] == ']' && last == '\a');
    }

    /// <summary>What a probe reply proves. Null fields were not answered.</summary>
    internal sealed record Result(bool? Kitty, bool? Sixel, bool? GoodImage, string? Identity, int CellWidth, int CellHeight);

    internal static Result Parse(string response) {
        bool? kitty = null, sixel = null, good = null;
        string? identity = null;
        int cellWidth = 0, cellHeight = 0, pixelWidth = 0, pixelHeight = 0, columns = 0, rows = 0;
        foreach (string segment in response.Split('\u001b')) {
            if (segment.StartsWith("_G", StringComparison.Ordinal)) {
                int separator = segment.IndexOf(';');
                if (separator > 2 && segment.AsSpan(2, separator - 2).ToString().Split(',').Contains("i=" + KittyQueryId))
                    kitty = segment.AsSpan(separator + 1).StartsWith("OK", StringComparison.Ordinal);
            }
            else if (segment.StartsWith("[?", StringComparison.Ordinal) && segment.EndsWith('c')) {
                string[] attributes = segment[2..^1].Split(';').Skip(1).ToArray();
                sixel = attributes.Contains("4");
                good = attributes.Contains("90");
            }
            else if (segment.StartsWith("P>|", StringComparison.Ordinal)) {
                identity = segment[3..];
            }
            else if (TryParseSize(segment, '6', out int height, out int width)) {
                (cellWidth, cellHeight) = (width, height);
            }
            else if (TryParseSize(segment, '4', out height, out width)) {
                (pixelWidth, pixelHeight) = (width, height);
            }
            else if (TryParseSize(segment, '8', out height, out width)) {
                (columns, rows) = (width, height);
            }
        }
        // Without a cell size report, derive it from the window's pixels and cells.
        if (cellWidth <= 0 && columns > 0 && rows > 0 && pixelWidth >= columns && pixelHeight >= rows)
            (cellWidth, cellHeight) = (pixelWidth / columns, pixelHeight / rows);
        // The kitty reply is only conclusive once the fence arrived; a missing reply then means no support.
        if (kitty is null && sixel is not null)
            kitty = false;
        return new(kitty, sixel, good, identity, cellWidth, cellHeight);
    }

    // CSI report ; height ; width t
    private static bool TryParseSize(string segment, char report, out int height, out int width) {
        height = width = 0;
        if (segment.Length < 6 || segment[0] != '[' || segment[1] != report || segment[2] != ';' || segment[^1] != 't')
            return false;
        string[] parts = segment[3..^1].Split(';');
        return parts.Length == 2
            && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out height)
            && int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out width)
            && height > 0 && width > 0;
    }
}
