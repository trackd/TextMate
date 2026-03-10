using PSTextMate.Terminal;
using PSTextMate.Utilities;
using Spectre.Console;
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
}
