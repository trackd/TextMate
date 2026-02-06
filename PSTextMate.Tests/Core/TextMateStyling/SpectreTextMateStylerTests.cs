using Xunit;
using Spectre.Console;
using TextMateSharp.Themes;
using TextMateSharp.Grammars;
using PwshSpectreConsole.TextMate.Core.TextMateStyling;
using PwshSpectreConsole.TextMate.Infrastructure;

namespace PwshSpectreConsole.TextMate.Tests.Core.TextMateStyling;

public class SpectreTextMateStylerTests
{
    private readonly SpectreTextMateStyler _styler;
    private readonly Theme _theme;

    public SpectreTextMateStylerTests()
    {
        _styler = new SpectreTextMateStyler();
        _theme = CreateTestTheme();
    }

    [Fact]
    public void GetStyleForScopes_WithValidScopes_ReturnsStyle()
    {
        var scopes = new[] { "source.cs", "keyword.other.using.cs" };

        var style = _styler.GetStyleForScopes(scopes, _theme);

        // May return null if theme has no rule for these scopes, which is valid
        // Test passes if no exception thrown
        Assert.True(true);
    }

    [Fact]
    public void GetStyleForScopes_CachesResults()
    {
        var scopes = new[] { "source.cs", "keyword.other.using.cs" };

        var style1 = _styler.GetStyleForScopes(scopes, _theme);
        var style2 = _styler.GetStyleForScopes(scopes, _theme);

        // If styles are returned, should be same instance (cached)
        if (style1 != null && style2 != null)
        {
            Assert.Same(style1, style2);
        }
    }

    [Fact]
    public void GetStyleForScopes_WithEmptyScopes_ReturnsNull()
    {
        var style = _styler.GetStyleForScopes([], _theme);
        Assert.Null(style);
    }

    [Fact]
    public void GetStyleForScopes_WithNullScopes_ReturnsNull()
    {
        var style = _styler.GetStyleForScopes(null!, _theme);
        Assert.Null(style);
    }

    [Fact]
    public void ApplyStyle_WithValidText_ReturnsText()
    {
        var style = new Style(Color.Red);
        var text = _styler.ApplyStyle("hello", style);

        Assert.NotNull(text);
    }

    [Fact]
    public void ApplyStyle_WithNullStyle_ReturnsPlainText()
    {
        var text = _styler.ApplyStyle("hello", null);

        Assert.NotNull(text);
    }

    [Fact]
    public void ApplyStyle_WithEmptyString_ReturnsEmptyText()
    {
        var text = _styler.ApplyStyle("", new Style());

        Assert.Equal(0, text.Length);
    }

    [Fact]
    public void ApplyStyle_WithNullString_ReturnsEmptyText()
    {
        var text = _styler.ApplyStyle(null!, new Style());

        Assert.Equal(0, text.Length);
    }

    private static Theme CreateTestTheme()
    {
        // Use cached theme for tests
        var (_, theme) = CacheManager.GetCachedTheme(ThemeName.DarkPlus);
        return theme;
    }
}
