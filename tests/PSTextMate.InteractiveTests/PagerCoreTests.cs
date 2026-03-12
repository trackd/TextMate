using PSTextMate.Terminal;
using PSTextMate.Core;
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
    public void SetQuery_CustomRenderableWithToStringFallback_FindsMatch() {
        PagerDocument document = new([
            new ThrowingRenderable("alpha beta"),
            new ThrowingRenderable("gamma")
        ]);

        PagerSearchSession session = new(document);
        session.SetQuery("beta");

        Assert.True(session.HasQuery);
        Assert.Equal(1, session.HitCount);

        PagerSearchHit? hit = session.MoveNext(topIndex: 0);
        Assert.NotNull(hit);
        Assert.Equal(0, hit.RenderableIndex);
    }

    [Fact]
    public void SetQuery_FromHighlightedTextWithCustomRenderable_FindsMatch() {
        HighlightedText highlighted = new() {
            Renderables = [new ThrowingRenderable("search target")]
        };

        var document = PagerDocument.FromHighlightedText(highlighted);
        PagerSearchSession session = new(document);
        session.SetQuery("target");

        Assert.Equal(1, session.HitCount);
        Assert.NotNull(session.MoveNext(topIndex: 0));
    }

    [Fact]
    public void SetQuery_RenderableWithEmptyWriterOutput_UsesToStringFallback() {
        PagerDocument document = new([
            new EmptyRenderable("delta epsilon")
        ]);

        PagerSearchSession session = new(document);
        session.SetQuery("epsilon");

        Assert.Equal(1, session.HitCount);
        Assert.NotNull(session.MoveNext(topIndex: 0));
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
