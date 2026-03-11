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

        PagerDocument document = PagerDocument.FromHighlightedText(highlighted);
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

    private sealed class ThrowingRenderable : IRenderable {
        private readonly string _text;

        public ThrowingRenderable(string text) {
            _text = text;
        }

        public Measurement Measure(RenderOptions options, int maxWidth) {
            throw new InvalidOperationException("test render failure");
        }

        public IEnumerable<Segment> Render(RenderOptions options, int maxWidth) {
            throw new InvalidOperationException("test render failure");
        }

        public override string ToString() {
            return _text;
        }
    }

    private sealed class EmptyRenderable : IRenderable {
        private readonly string _text;

        public EmptyRenderable(string text) {
            _text = text;
        }

        public Measurement Measure(RenderOptions options, int maxWidth) {
            return new Measurement(1, 1);
        }

        public IEnumerable<Segment> Render(RenderOptions options, int maxWidth) {
            return [];
        }

        public override string ToString() {
            return _text;
        }
    }
}
