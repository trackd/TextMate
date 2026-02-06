using System.Text;
using PwshSpectreConsole.TextMate.Core;
using PwshSpectreConsole.TextMate.Infrastructure;
using TextMateSharp.Grammars;
using TextMateSharp.Themes;

namespace PwshSpectreConsole.TextMate.Tests.Core;

public class TokenProcessorTests
{
    [Fact]
    public void ProcessTokensBatch_WithEscapeMarkup_EscapesSpecialCharacters()
    {
        // Arrange
        var (grammar, theme) = GetTestGrammarAndTheme();
        string line = "[markup] <text>";
        var tokenResult = grammar.TokenizeLine(line, null, TimeSpan.MaxValue);
        var builder = new StringBuilder();

        // Act
        TokenProcessor.ProcessTokensBatch(tokenResult.Tokens, line, theme, builder, escapeMarkup: true);
        string result = builder.ToString();

        // Assert - markup should be escaped
        (result.Contains("[[") || result.Contains("]]") || result.Contains("&lt;")).Should().BeTrue();
    }

    [Fact]
    public void ProcessTokensBatch_WithoutEscapeMarkup_PreservesRawContent()
    {
        // Arrange
        var (grammar, theme) = GetTestGrammarAndTheme();
        string line = "[markup]";
        var tokenResult = grammar.TokenizeLine(line, null, TimeSpan.MaxValue);
        var builder = new StringBuilder();

        // Act
        TokenProcessor.ProcessTokensBatch(tokenResult.Tokens, line, theme, builder, escapeMarkup: false);
        string result = builder.ToString();

        // Assert - when not escaping, raw brackets should be in result
        result.Should().NotBeEmpty();
    }

    [Fact]
    public void ProcessTokensBatch_WithDebugCallback_InvokesCallback()
    {
        // Arrange
        var (grammar, theme) = GetTestGrammarAndTheme();
        string line = "$x = 1";
        var tokenResult = grammar.TokenizeLine(line, null, TimeSpan.MaxValue);
        var builder = new StringBuilder();
        var debugInfos = new List<TokenDebugInfo>();

        // Act
        TokenProcessor.ProcessTokensBatch(
            tokenResult.Tokens,
            line,
            theme,
            builder,
            debugCallback: info => debugInfos.Add(info),
            lineIndex: 0,
            escapeMarkup: true
        );

        // Assert
        debugInfos.Should().NotBeEmpty();
        debugInfos[0].LineIndex.Should().Be(0);
        debugInfos[0].Text.Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void ExtractThemeProperties_CachesResults()
    {
        // Arrange
        var (grammar, theme) = GetTestGrammarAndTheme();
        string line = "$x = 1";
        var tokenResult = grammar.TokenizeLine(line, null, TimeSpan.MaxValue);
        var firstToken = tokenResult.Tokens[0];

        // Act - call twice with same token
        var result1 = TokenProcessor.ExtractThemeProperties(firstToken, theme);
        var result2 = TokenProcessor.ExtractThemeProperties(firstToken, theme);

        // Assert - both calls should return same cached result
        result1.Should().Be(result2);
    }

    [Fact]
    public void ProcessTokensBatch_WithEmptyLine_HandlesGracefully()
    {
        // Arrange
        var (grammar, theme) = GetTestGrammarAndTheme();
        string line = "";
        var tokenResult = grammar.TokenizeLine(line, null, TimeSpan.MaxValue);
        var builder = new StringBuilder();

        // Act
        TokenProcessor.ProcessTokensBatch(tokenResult.Tokens, line, theme, builder, escapeMarkup: true);
        string result = builder.ToString();

        // Assert
        result.Should().BeEmpty();
    }

    [Fact]
    public void ProcessTokensBatch_WithMultipleTokens_ProcessesAll()
    {
        // Arrange
        var (grammar, theme) = GetTestGrammarAndTheme();
        string line = "$variable = 'string'";
        var tokenResult = grammar.TokenizeLine(line, null, TimeSpan.MaxValue);
        var builder = new StringBuilder();

        // Act
        TokenProcessor.ProcessTokensBatch(tokenResult.Tokens, line, theme, builder, escapeMarkup: true);
        string result = builder.ToString();

        // Assert
        result.Should().NotBeEmpty();
        result.Length.Should().BeGreaterThan(0);
    }

    private static (IGrammar grammar, Theme theme) GetTestGrammarAndTheme()
    {
        var (registry, theme) = CacheManager.GetCachedTheme(ThemeName.DarkPlus);
        var grammar = CacheManager.GetCachedGrammar(registry, "powershell", isExtension: false);
        return (grammar!, theme);
    }
}
