using PwshSpectreConsole.TextMate.Core;
using PwshSpectreConsole.TextMate.Infrastructure;
using TextMateSharp.Grammars;

namespace PwshSpectreConsole.TextMate.Tests.Core;

public class StandardRendererTests
{
    [Fact]
    public void Render_WithValidInput_ReturnsRows()
    {
        // Arrange
        string[] lines = ["function Test-Function {", "    Write-Host 'Hello'", "}"];
        var (grammar, theme) = GetTestGrammarAndTheme();

        // Act
        var result = StandardRenderer.Render(lines, theme, grammar);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(3);
    }

    [Fact]
    public void Render_WithEmptyLines_HandlesGracefully()
    {
        // Arrange
        string[] lines = ["", "test", ""];
        var (grammar, theme) = GetTestGrammarAndTheme();

        // Act
        var result = StandardRenderer.Render(lines, theme, grammar);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(3);
    }

    [Fact]
    public void Render_WithSingleLine_ReturnsOneRow()
    {
        // Arrange
        string[] lines = ["$x = 1"];
        var (grammar, theme) = GetTestGrammarAndTheme();

        // Act
        var result = StandardRenderer.Render(lines, theme, grammar);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(1);
    }

    [Fact]
    public void Render_WithDebugCallback_InvokesCallback()
    {
        // Arrange
        string[] lines = ["$x = 1"];
        var (grammar, theme) = GetTestGrammarAndTheme();
        var debugInfos = new List<TokenDebugInfo>();

        // Act
        var result = StandardRenderer.Render(lines, theme, grammar, info => debugInfos.Add(info));

        // Assert
        result.Should().NotBeNull();
        debugInfos.Should().NotBeEmpty();
    }

    [Fact]
    public void Render_PreservesLineOrder()
    {
        // Arrange
        string[] lines = ["# Line 1", "# Line 2", "# Line 3"];
        var (grammar, theme) = GetTestGrammarAndTheme();

        // Act
        var result = StandardRenderer.Render(lines, theme, grammar);

        // Assert
        result.Should().NotBeNull();
        result.Should().HaveCount(3);
        result.Should().ContainInOrder(result);
    }

    private static (IGrammar grammar, Theme theme) GetTestGrammarAndTheme()
    {
        var (registry, theme) = CacheManager.GetCachedTheme(ThemeName.DarkPlus);
        var grammar = CacheManager.GetCachedGrammar(registry, "powershell", isExtension: false);
        return (grammar!, theme);
    }
}
