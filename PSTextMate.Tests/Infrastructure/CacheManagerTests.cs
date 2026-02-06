using PwshSpectreConsole.TextMate.Infrastructure;
using TextMateSharp.Grammars;

namespace PwshSpectreConsole.TextMate.Tests.Infrastructure;

public class CacheManagerTests
{
    [Fact]
    public void GetCachedTheme_ReturnsSameInstanceOnRepeatedCalls()
    {
        // Arrange
        var themeName = ThemeName.DarkPlus;

        // Act
        var (registry1, theme1) = CacheManager.GetCachedTheme(themeName);
        var (registry2, theme2) = CacheManager.GetCachedTheme(themeName);

        // Assert
        registry1.Should().BeSameAs(registry2);
        theme1.Should().BeSameAs(theme2);
    }

    [Theory]
    [InlineData(ThemeName.DarkPlus)]
    [InlineData(ThemeName.Light)]
    [InlineData(ThemeName.Monokai)]
    public void GetCachedTheme_WorksForAllThemes(ThemeName themeName)
    {
        // Act
        var (registry, theme) = CacheManager.GetCachedTheme(themeName);

        // Assert
        registry.Should().NotBeNull();
        theme.Should().NotBeNull();
    }

    [Fact]
    public void GetCachedGrammar_ReturnsSameInstanceOnRepeatedCalls()
    {
        // Arrange
        var (registry, _) = CacheManager.GetCachedTheme(ThemeName.DarkPlus);
        string grammarId = "powershell";

        // Act
        var grammar1 = CacheManager.GetCachedGrammar(registry, grammarId, isExtension: false);
        var grammar2 = CacheManager.GetCachedGrammar(registry, grammarId, isExtension: false);

        // Assert
        grammar1.Should().BeSameAs(grammar2);
    }

    [Fact]
    public void GetCachedGrammar_WithExtension_LoadsCorrectGrammar()
    {
        // Arrange
        var (registry, _) = CacheManager.GetCachedTheme(ThemeName.DarkPlus);
        string extension = ".ps1";

        // Act
        var grammar = CacheManager.GetCachedGrammar(registry, extension, isExtension: true);

        // Assert
        grammar.Should().NotBeNull();
        grammar!.GetName().Should().Be("PowerShell");
    }

    [Fact]
    public void GetCachedGrammar_WithLanguageId_LoadsCorrectGrammar()
    {
        // Arrange
        var (registry, _) = CacheManager.GetCachedTheme(ThemeName.DarkPlus);
        string languageId = "csharp";

        // Act
        var grammar = CacheManager.GetCachedGrammar(registry, languageId, isExtension: false);

        // Assert
        grammar.Should().NotBeNull();
    }

    [Fact]
    public void GetCachedGrammar_WithInvalidGrammar_ReturnsNull()
    {
        // Arrange
        var (registry, _) = CacheManager.GetCachedTheme(ThemeName.DarkPlus);
        string invalidGrammar = "invalid-grammar-xyz";

        // Act
        var grammar = CacheManager.GetCachedGrammar(registry, invalidGrammar, isExtension: false);

        // Assert
        grammar.Should().BeNull();
    }

    [Fact]
    public void ClearCache_RemovesAllCachedItems()
    {
        // Arrange
        var (registry1, theme1) = CacheManager.GetCachedTheme(ThemeName.DarkPlus);
        var grammar1 = CacheManager.GetCachedGrammar(registry1, "powershell", isExtension: false);

        // Act
        CacheManager.ClearCache();
        var (registry2, theme2) = CacheManager.GetCachedTheme(ThemeName.DarkPlus);
        var grammar2 = CacheManager.GetCachedGrammar(registry2, "powershell", isExtension: false);

        // Assert - new instances after clear
        registry1.Should().NotBeSameAs(registry2);
        theme1.Should().NotBeSameAs(theme2);
    }
}
