using PSTextMate.Core;
using PSTextMate.Terminal;
using PSTextMate.Utilities;
using Spectre.Console;
using Spectre.Console.Rendering;
using Xunit;

namespace PSTextMate.InteractiveTests;

public sealed class PagerCoreTests {
    [Fact]
    public void SetQuery_WithMultipleMatches_BuildsHitIndex() {
        PagerDocument document = new([
            new Markup("alpha beta"),
            new Markup("beta gamma")
        ]);

        PagerSearchSession session = new(document);
        session.SetQuery("beta");

        Assert.Equal(2, session.HitCount);

        PagerSearchHit? first = session.MoveNext(topIndex: 0);
        Assert.NotNull(first);
        Assert.Equal(0, first.RenderableIndex);

        PagerSearchHit? second = session.MoveNext(topIndex: 0);
        Assert.NotNull(second);
        Assert.Equal(1, second.RenderableIndex);
    }

    [Fact]
    public void SetQuery_EmptyValue_ClearsHitsAndCurrentSelection() {
        PagerDocument document = new([
            new Markup("alpha beta"),
            new Markup("beta gamma")
        ]);

        PagerSearchSession session = new(document);
        session.SetQuery("beta");
        Assert.Equal(2, session.HitCount);

        session.SetQuery(string.Empty);

        Assert.False(session.HasQuery);
        Assert.Equal(0, session.HitCount);
        Assert.Null(session.CurrentHit);
    }

    [Fact]
    public void SetQuery_RepeatedAndChangedQuery_RebuildsRenderableHitIndexWithoutStaleEntries() {
        PagerDocument document = new([
            new Markup("alpha beta"),
            new Markup("beta beta"),
            new Markup("gamma")
        ]);

        PagerSearchSession session = new(document);

        session.SetQuery("beta");
        Assert.Equal(3, session.HitCount);
        Assert.Single(session.GetHitsForRenderable(0));
        Assert.Equal(2, session.GetHitsForRenderable(1).Count);
        Assert.Empty(session.GetHitsForRenderable(2));

        session.SetQuery("beta");
        Assert.Equal(3, session.HitCount);
        Assert.Single(session.GetHitsForRenderable(0));
        Assert.Equal(2, session.GetHitsForRenderable(1).Count);

        session.SetQuery("gamma");
        Assert.Equal(1, session.HitCount);
        Assert.Empty(session.GetHitsForRenderable(0));
        Assert.Empty(session.GetHitsForRenderable(1));
        Assert.Single(session.GetHitsForRenderable(2));
    }

    [Fact]
    public void SetQuery_CustomRenderableWithoutRenderableText_DoesNotMatch() {
        PagerDocument document = new([
            new ThrowingRenderable("alpha beta"),
            new ThrowingRenderable("gamma")
        ]);

        PagerSearchSession session = new(document);
        session.SetQuery("beta");

        Assert.True(session.HasQuery);
        Assert.Equal(0, session.HitCount);
        Assert.Null(session.MoveNext(topIndex: 0));
    }

    [Fact]
    public void SetQuery_FromHighlightedTextWithSourceLines_FindsMatch() {
        HighlightedText highlighted = new(
            [new ThrowingRenderable("ignored render text")],
            sourceLines: ["search target"]
        );

        var document = PagerDocument.FromHighlightedText(highlighted);
        PagerSearchSession session = new(document);
        session.SetQuery("target");

        Assert.Equal(1, session.HitCount);
        Assert.NotNull(session.MoveNext(topIndex: 0));
    }

    [Fact]
    public void SetQuery_RenderableWithEmptyWriterOutput_DoesNotMatch() {
        PagerDocument document = new([
            new EmptyRenderable("delta epsilon")
        ]);

        PagerSearchSession session = new(document);
        session.SetQuery("epsilon");

        Assert.Equal(0, session.HitCount);
        Assert.Null(session.MoveNext(topIndex: 0));
    }

    [Fact]
    public void PagerDocument_SearchText_IsBuiltLazily() {
        var renderable = new CountingRenderable("lazy search target");

        PagerDocument document = new([renderable]);

        Assert.Equal(0, renderable.RenderCallCount);

        PagerSearchSession session = new(document);
        session.SetQuery("target");

        Assert.True(renderable.RenderCallCount > 0);
        Assert.Equal(1, session.HitCount);
    }

    [Fact]
    public void RecalculateHeights_SameLayout_DoesNotRecomputeRenderHeights() {
        var first = new CountingRenderable("alpha");
        var second = new CountingRenderable("beta");
        IReadOnlyList<IRenderable> renderables = [first, second];

        PagerViewportEngine engine = new(renderables, sourceHighlightedText: null);

        engine.RecalculateHeights(width: 80, contentRows: 20, windowHeight: 40, AnsiConsole.Console);
        int firstPassRenders = first.RenderCallCount + second.RenderCallCount;

        engine.RecalculateHeights(width: 80, contentRows: 20, windowHeight: 40, AnsiConsole.Console);
        int secondPassRenders = first.RenderCallCount + second.RenderCallCount;

        Assert.Equal(firstPassRenders, secondPassRenders);

        engine.RecalculateHeights(width: 81, contentRows: 20, windowHeight: 40, AnsiConsole.Console);
        int thirdPassRenders = first.RenderCallCount + second.RenderCallCount;

        Assert.True(thirdPassRenders > secondPassRenders);
    }

    [Fact]
    public void SetQuery_LinkRenderable_MatchesLabelAndUrl() {
        PagerDocument document = new([
            new Text("Guide"),
            new Osc8Renderable("Guide", "https://example.com/docs")
        ]);
        PagerSearchSession session = new(document);

        session.SetQuery("Guide");
        Assert.True(session.HitCount >= 1);
        Assert.NotNull(session.MoveNext(topIndex: 0));

        session.SetQuery("example.com/docs");
        Assert.Equal(1, session.HitCount);

        PagerSearchHit? urlHit = session.MoveNext(topIndex: 0);
        Assert.NotNull(urlHit);
        Assert.Equal(1, urlHit.RenderableIndex);
    }

    [Fact]
    public void SegmentHighlighter_UrlMatch_HighlightsLinkLabel() {
        var paragraph = new Paragraph();
        SpectreStyleCompat.Append(paragraph, "Guide", Style.Plain, "https://example.com/docs");

        IRenderable highlighted = PagerHighlighting.BuildSegmentHighlightRenderable(
            paragraph,
            "http",
            new Style(Color.White, Color.Grey),
            new Style(Color.Black, Color.Orange1),
            highlightLinkedLabelsOnNoDirectMatch: true
        );

        var options = RenderOptions.Create(AnsiConsole.Console);
        List<Segment> segments = [.. highlighted.Render(options, 120)];

        bool hasHighlightedLabel = segments.Any(segment =>
            !segment.IsControlCode
            && !segment.IsLineBreak
            && segment.Text.Contains("Guide", StringComparison.Ordinal)
            && segment.Style.Foreground == Color.Black
            && segment.Style.Background == Color.Orange1);

        Assert.True(hasHighlightedLabel);
    }

    [Fact]
    public void SegmentHighlighter_DirectMatch_StylesRowTextButNotBorders() {
        Style baseStyle = new(Color.Blue, Color.Black);
        var text = new Text("Guide │ details", baseStyle);

        IRenderable highlighted = PagerHighlighting.BuildSegmentHighlightRenderable(
            text,
            "Guide",
            new Style(Color.White, Color.Grey),
            new Style(Color.Black, Color.Orange1),
            highlightLinkedLabelsOnNoDirectMatch: false
        );

        var options = RenderOptions.Create(AnsiConsole.Console);
        List<Segment> segments = [.. highlighted.Render(options, 120)];

        bool hasMatchSegment = segments.Any(segment =>
            !segment.IsControlCode
            && !segment.IsLineBreak
            && segment.Text.Contains("Guide", StringComparison.Ordinal)
            && segment.Style.Foreground == Color.Black
            && segment.Style.Background == Color.Orange1);

        bool hasRowBackgroundOnNonMatchText = segments.Any(segment =>
            !segment.IsControlCode
            && !segment.IsLineBreak
            && !segment.Text.Contains('│')
            && !segment.Text.Contains("Guide", StringComparison.Ordinal)
            && segment.Style.Background == Color.Grey);

        bool borderKeptOriginalStyle = segments.Any(segment =>
            !segment.IsControlCode
            && !segment.IsLineBreak
            && segment.Text.Contains('│')
            && segment.Style.Equals(baseStyle));

        Assert.True(hasMatchSegment);
        Assert.True(hasRowBackgroundOnNonMatchText);
        Assert.True(borderKeptOriginalStyle);
    }

    private sealed class Osc8Renderable : IRenderable {
        private readonly string _label;
        private readonly string _url;

        public Osc8Renderable(string label, string url) {
            _label = label;
            _url = url;
        }

        public Measurement Measure(RenderOptions options, int maxWidth) {
            int width = Math.Max(1, Math.Min(maxWidth, _label.Length));
            return new Measurement(width, width);
        }

        public IEnumerable<Segment> Render(RenderOptions options, int maxWidth) {
            string esc = "\x1b";
            string osc8 = $"{esc}]8;;{_url}{esc}\\{_label}{esc}]8;;{esc}\\";
            return [new Segment(osc8, Style.Plain)];
        }
    }

    private sealed class ThrowingRenderable : IRenderable {
        private readonly string _text;

        public ThrowingRenderable(string text) {
            _text = text;
        }

        public Measurement Measure(RenderOptions options, int maxWidth) => throw new InvalidOperationException("test render failure");

        public IEnumerable<Segment> Render(RenderOptions options, int maxWidth) => throw new InvalidOperationException("test render failure");

        public override string ToString() => _text;
    }

    private sealed class EmptyRenderable : IRenderable {
        private readonly string _text;

        public EmptyRenderable(string text) {
            _text = text;
        }

        public Measurement Measure(RenderOptions options, int maxWidth) => new(1, 1);

        public IEnumerable<Segment> Render(RenderOptions options, int maxWidth) => [];

        public override string ToString() => _text;
    }

    private sealed class CountingRenderable : IRenderable {
        private readonly string _text;

        public int RenderCallCount { get; private set; }

        public CountingRenderable(string text) {
            _text = text;
        }

        public Measurement Measure(RenderOptions options, int maxWidth) {
            int width = Math.Max(1, Math.Min(maxWidth, _text.Length));
            return new Measurement(width, width);
        }

        public IEnumerable<Segment> Render(RenderOptions options, int maxWidth) {
            RenderCallCount++;
            return [new Segment(_text)];
        }

        public override string ToString() => _text;
    }
}
