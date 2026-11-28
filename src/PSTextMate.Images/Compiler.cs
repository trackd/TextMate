using System.Collections.ObjectModel;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Reflection;
using PSTextMate.Core;

namespace PSTextMate.Images;

/// <summary>
/// Markdown images. Terminals that decode images themselves (Kitty with PNG, iTerm2 inline images) are drawn
/// directly. Sixel, other formats and the portable cell fallback use the optional Sixel module.
/// </summary>
public sealed class Compiler : IImagePresentation {
    private const int MaximumImageBytes = 32 * 1024 * 1024;
    private static readonly Lazy<HttpClient> ImageClient = new(static () => new HttpClient {
        Timeout = TimeSpan.FromSeconds(5),
        MaxResponseContentBufferSize = MaximumImageBytes
    });

    /// <summary>The capabilities TextMate probed once for the session; reading them performs no terminal I/O.</summary>
    public TerminalGraphics GetTerminalGraphics() => TerminalCapabilities.Current.Graphics;

    public HighlightedDocument? Render(HighlightedDocument document, string source, string? baseDirectory, int width, int height = 0) {
        if (height <= 0)
            height = !Console.IsOutputRedirected ? Math.Max(1, Console.WindowHeight - 2) : 30;
        width = Math.Max(1, width);
        TerminalGraphics graphics = TerminalCapabilities.Current.Graphics;
        Stream? image;
        try {
            image = TryOpenImage(source, baseDirectory);
        }
        catch (Exception error) when (error is IOException or HttpRequestException or OperationCanceledException or UnauthorizedAccessException or ArgumentException or NotSupportedException) {
            // Unreachable or unreadable images keep their clickable label.
            return null;
        }
        using (image) {
            if (image is not null && RenderBuiltIn(document, image, graphics, width, height) is { } drawn)
                return drawn;
            if (image is not null)
                image.Position = 0;
            // Direct .NET callers need no PowerShell runtime. Do not JIT PowerShell code until hosted.
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                if (assembly.GetName().Name == "System.Management.Automation")
                    return RenderInRunspace(document, source, baseDirectory, image, graphics, width, height);
            }
            return null;
        }
    }

    /// <summary>
    /// Draws an image the terminal decodes itself, without the Sixel module or an image library: PNG through the
    /// Kitty graphics protocol, or PNG, JPEG and GIF as iTerm2 inline images. The cell rectangle keeps the image's
    /// aspect ratio; the terminal scales the pixels into it.
    /// </summary>
    internal static HighlightedDocument? RenderBuiltIn(HighlightedDocument document, Stream image, TerminalGraphics graphics, int width, int height) {
        if (!graphics.HasCellSize || !(graphics.Kitty || graphics.InlineImage) || image.Length > MaximumImageBytes)
            return null;
        Span<byte> header = stackalloc byte[8];
        long start = image.Position;
        int read = image.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
        image.Position = start;
        TerminalImageWriter.Format format = TerminalImageWriter.Detect(header[..read]);
        TerminalImageProtocol protocol;
        if (graphics.Kitty && format == TerminalImageWriter.Format.Png)
            protocol = TerminalImageProtocol.Kitty;
        else if (graphics.InlineImage && format != TerminalImageWriter.Format.Unknown)
            protocol = TerminalImageProtocol.InlineImage;
        else
            return null;
        if (!ImageHeader.TryRead(image, out ImageDimensions dimensions))
            return null;
        (int columns, int rows) = ImageSizing.Fit(dimensions, graphics with { Sixel = false }, width, height);
        byte[] bytes = new byte[image.Length - start];
        image.ReadExactly(bytes);
        string payload = protocol == TerminalImageProtocol.Kitty
            ? TerminalImageWriter.Kitty(bytes, columns, rows)
            : TerminalImageWriter.InlineImage(bytes, columns, rows);
        // Reserve the rectangle. Outputs that cannot draw graphics show it as blank space below the image's label.
        var builder = new PresentedTextBuilder();
        for (int row = 0; row < rows; row++) {
            if (row > 0)
                builder.NewLine();
            builder.AppendRepeated(' ', columns, TextStyle.Plain);
        }
        builder.AddImage(0, new TerminalImage(columns, rows, payload.AsMemory(), protocol));
        return builder.Build(document);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static HighlightedDocument? RenderInRunspace(HighlightedDocument document, string source, string? baseDirectory,
        Stream? imageStream, TerminalGraphics graphics, int width, int height) {
        if (Runspace.DefaultRunspace is null)
            return null;
        using var pipeline = PowerShell.Create(RunspaceMode.CurrentRunspace);
        try {
            using ConsoleModeLease? modes = Console.IsInputRedirected || Console.IsOutputRedirected ? null : ConsoleModeLease.Enter();
            pipeline.AddCommand("Microsoft.PowerShell.Core\\Get-Module").AddParameter("Name", "Sixel");
            if (pipeline.Invoke().Count == 0) {
                pipeline.Commands.Clear();
                pipeline.Streams.Error.Clear();
                pipeline.AddCommand("Microsoft.PowerShell.Core\\Import-Module")
                    .AddParameter("Name", "Sixel").AddParameter("ErrorAction", ActionPreference.Stop);
                pipeline.Invoke();
            }
            pipeline.Commands.Clear();
            pipeline.Streams.Error.Clear();
            // -Force skips the module's own probe; hand it the cell size TextMate measured so pixel sizes agree.
            TerminalCapabilities capabilities = TerminalCapabilities.Current;
            if (capabilities.CellSizeMeasured) {
                pipeline.AddScript("""
                    param($width, $height)
                    $compatibility = [System.Management.Automation.PSTypeName]::new('Sixel.Terminal.Compatibility').Type
                    if ($null -ne $compatibility -and $null -ne $compatibility.GetMethod('SetCellSize')) {
                        $compatibility::SetCellSize($width, $height)
                    }
                    """, useLocalScope: true).AddArgument(capabilities.CellWidth).AddArgument(capabilities.CellHeight);
                pipeline.Invoke();
                pipeline.Commands.Clear();
                pipeline.Streams.Error.Clear();
            }
            int requestedWidth = width;
            int requestedHeight = Math.Max(1, height);
            if (imageStream is not null && ImageHeader.TryRead(imageStream, out ImageDimensions dimensions))
                (requestedWidth, requestedHeight) = ImageSizing.Fit(dimensions, graphics, requestedWidth, requestedHeight);
            pipeline.AddCommand("Sixel\\ConvertTo-Sixel");
            if (imageStream is not null) {
                pipeline.AddParameter("Stream", imageStream);
            }
            else
                if (Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https") {
                    pipeline.AddParameter("Url", uri).AddParameter("Timeout", TimeSpan.FromSeconds(5));
                }
                else {
                    string path = uri?.IsFile == true ? uri.LocalPath : source;
                    if (!Path.IsPathRooted(path) && baseDirectory is not null)
                        path = Path.Combine(baseDirectory, path);
                    pipeline.AddParameter("Path", path);
                }
            // Portable cell output keeps native Spectre, XenoAtom and built-in output identical.
            // Explicit protocol selection avoids automatic negotiation; the module owns decoding and conversion.
            pipeline.AddParameter("Protocol", "Blocks").AddParameter("Force")
                .AddParameter("Width", requestedWidth).AddParameter("Height", requestedHeight)
                .AddParameter("ErrorAction", ActionPreference.Stop);
            Collection<PSObject> output = pipeline.Invoke();
            HighlightedDocument? fallback = output.Count == 1 && output[0].BaseObject is string text ? Parse(document, text) : null;
            string? protocol = graphics.Kitty ? "KittyGraphicsProtocol" : graphics.InlineImage ? "InlineImageProtocol" : graphics.Sixel ? "Sixel" : null;
            if (fallback is null || protocol is null || !graphics.HasCellSize || imageStream is null)
                return fallback;
            imageStream.Position = 0;
            pipeline.Commands.Clear();
            pipeline.Streams.Error.Clear();
            // The protocol TextMate detected; TextMate only recognizes, places and redraws the sequence returned.
            pipeline.AddCommand("Sixel\\ConvertTo-Sixel").AddParameter("Stream", imageStream)
                .AddParameter("Protocol", protocol).AddParameter("Force")
                .AddParameter("Width", requestedWidth).AddParameter("Height", requestedHeight)
                .AddParameter("ErrorAction", ActionPreference.Stop);
            try {
                output = pipeline.Invoke();
            }
            catch (RuntimeException) {
                return fallback;
            }
            if (output.Count != 1 || output[0].BaseObject is not string sequence
                || !TerminalImageParser.TryFind(sequence, TerminalImageParser.CellMetrics.From(graphics, width, height), out TerminalImage? parsed)) {
                return fallback;
            }
            // The module's reported cell size is authoritative; the sequence's own size is the fallback.
            int imageWidth = output[0].Properties["Width"]?.Value is int reportedWidth and > 0 ? reportedWidth : parsed!.Width;
            int imageHeight = output[0].Properties["Height"]?.Value is int reportedHeight and > 0 ? reportedHeight : parsed!.Height;
            if (imageWidth < 1 || imageWidth > width || imageHeight < 1 || imageHeight > height)
                return fallback;
            ReadOnlyMemory<char> payload = parsed!.Payload;
            var builder = new PresentedTextBuilder();
            builder.Append(fallback);
            int rows = Math.Max(fallback.LineCount, imageHeight);
            for (int row = fallback.LineCount; row < rows; row++) {
                builder.NewLine();
                builder.AppendRepeated(' ', imageWidth, TextStyle.Plain);
            }
            int columns = imageWidth;
            foreach (HighlightedLine line in fallback.Lines.Span)
                columns = Math.Max(columns, TerminalCells.Measure(fallback.Text.AsSpan(line.Start, line.Length)));
            builder.AddImage(0, new TerminalImage(columns, rows, payload, parsed.Protocol));
            return builder.Build(document);
        }
        catch (Exception error) when (error is RuntimeException or IOException or HttpRequestException or OperationCanceledException or UnauthorizedAccessException or ArgumentException or NotSupportedException) {
            // Missing/incompatible modules and decode failures retain the common image link.
            return null;
        }
    }
    private static Stream? TryOpenImage(string source, string? baseDirectory) {
        if (Uri.TryCreate(source, UriKind.Absolute, out Uri? uri) && uri.Scheme is "http" or "https") {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            using HttpResponseMessage response = ImageClient.Value.Send(request);
            response.EnsureSuccessStatusCode();
            using Stream content = response.Content.ReadAsStream();
            var buffer = new MemoryStream();
            content.CopyTo(buffer);
            buffer.Position = 0;
            return buffer;
        }
        string path = uri?.IsFile == true ? uri.LocalPath : source;
        if (!Path.IsPathRooted(path) && baseDirectory is not null)
            path = Path.Combine(baseDirectory, path);
        try {
            return File.OpenRead(path);
        }
        catch (Exception error) when (error is FileNotFoundException or DirectoryNotFoundException) {
            // The module still receives the original path and owns the decode/fallback decision.
            return null;
        }
    }
    /// <summary>Reads Blocks true-color SGR spans without per-cell strings.</summary>
    internal static HighlightedDocument? Parse(HighlightedDocument document, string text) {
        var builder = new PresentedTextBuilder();
        var style = new TextStyle(-1, -1, TextDecoration.None);
        int start = 0;
        for (int position = 0; position < text.Length; position++) {
            if (text[position] != '\x1b') {
                if (char.IsControl(text[position]) && text[position] is not '\r' and not '\n')
                    return null;
                continue;
            }
            builder.Append(text.AsSpan(start, position - start), style);
            int end = text.IndexOf('m', position + 1);
            if (end < 0 || end - position > 80 || position + 1 >= text.Length || text[position + 1] != '[')
                return null;
            ReadOnlySpan<char> parameters = text.AsSpan(position + 2, end - position - 2);
            while (!parameters.IsEmpty) {
                if (!ReadNumber(ref parameters, out int code))
                    return null;
                switch (code) {
                    case 0:
                        style = new(-1, -1, TextDecoration.None);
                        break;
                    case 39:
                        style = style with { Foreground = -1 };
                        break;
                    case 49:
                        style = style with { Background = -1 };
                        break;
                    case 38:
                    case 48:
                        if (!ReadNumber(ref parameters, out int mode) || mode != 2
                            || !ReadNumber(ref parameters, out int red) || red > 255
                            || !ReadNumber(ref parameters, out int green) || green > 255
                            || !ReadNumber(ref parameters, out int blue) || blue > 255) {
                            return null;
                        }

                        int rgb = (red << 16) | (green << 8) | blue;
                        style = code == 38 ? style with { Foreground = rgb } : style with { Background = rgb };
                        break;
                    default:
                        return null;
                }
            }
            position = end;
            start = end + 1;
        }
        builder.Append(text.AsSpan(start).TrimEnd("\r\n"), style);
        return builder.Build(document);
    }

    private static bool ReadNumber(ref ReadOnlySpan<char> parameters, out int value) {
        int separator = parameters.IndexOf(';');
        ReadOnlySpan<char> number = separator < 0 ? parameters : parameters[..separator];
        bool valid = int.TryParse(number, System.Globalization.NumberStyles.None,
            System.Globalization.CultureInfo.InvariantCulture, out value);
        parameters = separator < 0 ? default : parameters[(separator + 1)..];
        return valid;
    }
}
