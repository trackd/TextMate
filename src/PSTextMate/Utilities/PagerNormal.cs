using System.Globalization;
using Spectre.Console;
using Spectre.Console.Rendering;
using System.Linq;
using PSTextMate.Core;

namespace PSTextMate.Utilities;

public class PagerNormal : IDisposable {
    private readonly IReadOnlyList<IRenderable> _renderables;
    private readonly HighlightedText? _sourceHighlightedText;
    private readonly Measurement[] _measurements;
    // private readonly int? _originalLineNumberStart;
    // private readonly int? _originalLineNumberWidth;
    private readonly object _lock = new();

    public PagerNormal(HighlightedText highlightedText) {
        _sourceHighlightedText = highlightedText;
        // _originalLineNumberStart = highlightedText.LineNumberStart;
        // _originalLineNumberWidth = highlightedText.LineNumberWidth;
        _renderables = highlightedText.Renderables;
        _measurements = ComputeMeasurements(GetPagerSize().width);
    }

    public PagerNormal(IEnumerable<IRenderable> renderables) {
        var list = renderables?.ToList();
        _renderables = list is null ? [] : (IReadOnlyList<IRenderable>)list;
        _measurements = ComputeMeasurements(GetPagerSize().width);
    }

    private Measurement[] ComputeMeasurements(int width) {
        if (_sourceHighlightedText is not null) {
            return _sourceHighlightedText.MeasureRenderablesFull(width);
        }

        Capabilities caps = AnsiConsole.Console.Profile.Capabilities;
        var size = new Size(width, Math.Max(1, Console.WindowHeight));
        var options = new RenderOptions(caps, size);

        IReadOnlyList<IRenderable> source = _renderables;
        var list = new List<Measurement>(source.Count);
        foreach (IRenderable? r in source) {
            if (r is null) {
                list.Add(new Measurement(1, 1));
                continue;
            }

            try {
                Measurement m = r.Measure(options, width);
                list.Add(m);
            }
            catch {
                list.Add(new Measurement(1, 1));
            }
        }

        return [.. list];
    }

    private int GetRenderableRowCount(int index, int width) {
        if (_measurements == null || index < 0 || index >= _measurements.Length) return 1;
        Measurement m = _measurements[index];
        int maxWidth = Math.Max(1, m.Max);
        return maxWidth <= width ? 1 : (int)Math.Ceiling((double)maxWidth / width);
    }

    private int CountRenderablesForHeight(int startIndex, int availableRows, int width) {
        int sum = 0;
        int count = 0;
        if (_measurements == null) return 1;
        for (int i = startIndex; i < _measurements.Length; i++) {
            int r = GetRenderableRowCount(i, width);
            if (sum + r > availableRows) break;
            sum += r;
            count++;
        }

        // Always show at least one renderable
        return Math.Max(1, count);
    }

    private static (int width, int height) GetPagerSize() {
        int width = Console.WindowWidth > 0 ? Console.WindowWidth : 80;
        int height = Console.WindowHeight > 0 ? Console.WindowHeight : 40;
        return (width, height);
    }

    // private static void ScrollUp(int lines) => Console.Write($"\x1b[{lines}S");
    // private static void ScrollDown(int lines) => Console.Write($"\x1b[{lines}T");
    private IRenderable BuildContent(int top, int contentRows) {
        if (_sourceHighlightedText is not null) {
            return _sourceHighlightedText.Slice(top, contentRows, _sourceHighlightedText.LineNumberWidth);
        }

        var slice = _renderables.Skip(top).Take(contentRows).ToList();
        return new Rows(slice);
    }
    private static int ProcessKey(ConsoleKey key, int top, int availableRows, int totalCount, out bool running) {
        running = true;
        switch (key) {
            case ConsoleKey.DownArrow:
                if (top < Math.Max(0, totalCount - 1)) top++;
                break;
            case ConsoleKey.UpArrow:
                if (top > 0) top--;
                break;
            case ConsoleKey.PageDown:
            case ConsoleKey.Spacebar:
                top = Math.Min(Math.Max(0, totalCount - availableRows), top + availableRows);
                break;
            case ConsoleKey.PageUp:
                top = Math.Max(0, top - availableRows);
                break;
            case ConsoleKey.Home:
                top = 0;
                break;
            case ConsoleKey.End:
                top = Math.Max(0, totalCount - availableRows);
                break;
            case ConsoleKey.Q:
            case ConsoleKey.Escape:
                running = false;
                break;
        }

        return top;
    }
    private string BuildFooterLine(int top, int contentRows, int width) {
        string keys = "Up/Down: ↑↓  PgUp/PgDn: PgUp/PgDn/Spacebar  Home/End: Home/End  q/Esc: Quit";
        string status = $" {Math.Min(top + 1, _renderables.Count)}-{Math.Min(top + contentRows, _renderables.Count)}/{_renderables.Count} ";
        int remaining = Math.Max(0, width - keys.Length - status.Length - 2);
        string spacer = new(' ', remaining);
        string footerLine = keys + spacer + status;
        if (footerLine.Length > width) footerLine = footerLine[..width];
        return footerLine;
    }
    public void Show() {
        // VTHelpers.HideCursor();
        (int width, int height) = GetPagerSize();
        AnsiConsole.Console.Profile.Width = width;
        int availableRows = Math.Max(1, Console.WindowHeight - 1);
        int top = 0;

        int totalCount = _sourceHighlightedText is not null ? _sourceHighlightedText.Renderables.Length : _renderables.Count;

        // compute how many renderables fit in the availableRows starting at top
        int initialCount = CountRenderablesForHeight(top, availableRows, width);
        var composite = new CompositeRenderable(_sourceHighlightedText, _renderables, top, initialCount, width, BuildFooterLine);

        AnsiConsole.Live(composite)
            .AutoClear(true)
            .Overflow(VerticalOverflow.Ellipsis)
            .Start(ctx => {
                bool running = true;
                while (running) {
                    if (!Console.KeyAvailable) {
                        Thread.Sleep(50);
                        continue;
                    }

                    ConsoleKeyInfo key = Console.ReadKey(true);
                    int newTop = ProcessKey(key.Key, top, availableRows, totalCount, out running);

                    lock (_lock) {
                        top = newTop;
                        int count = CountRenderablesForHeight(top, availableRows, width);
                        composite.Update(top, count, width);
                        if (!running) {
                            return;
                        }
                        ctx.Refresh();
                    }
                }
            });

    }
    public void Dispose() => GC.SuppressFinalize(this);
}


// Composite renderable that presents content and a footer line as a single
// Spectre `IRenderable`. Navigation updates modify its internal state and
// calling `ctx.Refresh()` will cause Live to re-render using the current
// content/top values.
internal sealed class CompositeRenderable : IRenderable {
    private readonly HighlightedText? _source;
    private readonly IReadOnlyList<IRenderable>? _list;
    private readonly Func<int, int, int, string> _footerBuilder;
    private int _top;
    private int _contentRows;
    private int _width;

    public CompositeRenderable(HighlightedText? source, IReadOnlyList<IRenderable> list, int top, int contentRows, int width, Func<int, int, int, string> footerBuilder) {
        _source = source;
        _list = list;
        _top = top;
        _contentRows = contentRows;
        _width = width;
        _footerBuilder = footerBuilder;
    }

    public void Update(int top, int contentRows, int width) {
        _top = top;
        _contentRows = contentRows;
        _width = width;
    }

    public IEnumerable<Segment> Render(RenderOptions options, int maxWidth) {
        IRenderable content;
        if (_source is not null) {
            content = _source.Slice(_top, _contentRows, _source.LineNumberWidth);
        }
        else {
            IReadOnlyList<IRenderable> sourceList = _list ?? [];
            var slice = sourceList.Skip(_top).Take(_contentRows).ToList();
            content = new Rows(slice);
        }

        var footer = new Panel(new Text(_footerBuilder(_top, _contentRows, _width))) { Padding = new Padding(0, 0) };
        var rows = new Rows(content, footer);
        return ((IRenderable)rows).Render(options, maxWidth);
    }

    public Measurement Measure(RenderOptions options, int maxWidth) {
        IRenderable content;
        if (_source is not null) {
            content = _source.Slice(_top, _contentRows, _source.LineNumberWidth);
        }
        else {
            IReadOnlyList<IRenderable> sourceList = _list ?? [];
            var slice = sourceList.Skip(_top).Take(_contentRows).ToList();
            content = new Rows(slice);
        }

        var footer = new Panel(new Text(_footerBuilder(_top, _contentRows, _width))) { Padding = new Padding(0, 0) };
        var rows = new Rows(content, footer);
        return ((IRenderable)rows).Measure(options, maxWidth);
    }
}
