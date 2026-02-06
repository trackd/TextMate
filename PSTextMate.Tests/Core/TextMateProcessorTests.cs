using PwshSpectreConsole.TextMate.Core;
using TextMateSharp.Grammars;

namespace PwshSpectreConsole.TextMate.Tests.Core;

public class TextMateProcessorTests
{
    [Fact]
    public void ProcessLines_WithValidInput_ReturnsRows()
    {
        // Arrange
        string[] lines = ["$x = 1", "$y = 2"];

        // Act
        var result = TextMateProcessor.ProcessLines(lines, ThemeName.DarkPlus, "powershell", isExtension: false);

        // Assert
        result.Should().NotBeNull();
        result!.Should().HaveCount(2);
    }

    [Fact]
    public void ProcessLines_WithEmptyArray_ReturnsNull()
    {
        // Arrange
        string[] lines = [];

        // Act
        var result = TextMateProcessor.ProcessLines(lines, ThemeName.DarkPlus, "powershell", isExtension: false);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void ProcessLines_WithNullInput_ThrowsArgumentNullException()
    {
        // Arrange
        string[] lines = null!;

        // Act
        Action act = () => TextMateProcessor.ProcessLines(lines, ThemeName.DarkPlus, "powershell", isExtension: false);

        // Assert
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("lines");
    }

    [Fact]
    public void ProcessLines_WithInvalidGrammar_ThrowsInvalidOperationException()
    {
        // Arrange
        string[] lines = ["test"];

        // Act
        Action act = () => TextMateProcessor.ProcessLines(lines, ThemeName.DarkPlus, "invalid-grammar-xyz", isExtension: false);

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Grammar not found*");
    }

    [Fact]
    public void ProcessLines_WithExtension_ResolvesGrammar()
    {
        // Arrange
        string[] lines = ["function Test { }"];

        // Act
        var result = TextMateProcessor.ProcessLines(lines, ThemeName.DarkPlus, ".ps1", isExtension: true);

        // Assert
        result.Should().NotBeNull();
        result!.Should().HaveCount(1);
    }

    [Fact]
    public void ProcessLinesCodeBlock_PreservesRawContent()
    {
        // Arrange
        string[] lines = ["<markup>", "[brackets]"];

        // Act
        var result = TextMateProcessor.ProcessLinesCodeBlock(lines, ThemeName.DarkPlus, "html", isExtension: false);

        // Assert
        result.Should().NotBeNull();
        result!.Should().HaveCount(2);
    }

    [Fact]
    public void ProcessLinesInBatches_WithValidInput_YieldsBatches()
    {
        // Arrange
        var lines = Enumerable.Range(1, 100).Select(i => $"Line {i}");
        int batchSize = 25;

        // Act
        var batches = TextMateProcessor.ProcessLinesInBatches(lines, batchSize, ThemeName.DarkPlus, "powershell", isExtension: false);
        var batchList = batches.ToList();

        // Assert
        batchList.Should().HaveCount(4);
        batchList[0].BatchIndex.Should().Be(0);
        batchList[0].FileOffset.Should().Be(0);
        batchList[1].BatchIndex.Should().Be(1);
        batchList[1].FileOffset.Should().Be(25);
    }

    [Fact]
    public void ProcessLinesInBatches_WithInvalidBatchSize_ThrowsArgumentOutOfRangeException()
    {
        // Arrange
        var lines = new[] { "test" };

        // Act
        Action act = () => { var _ = TextMateProcessor.ProcessLinesInBatches(lines, 0, ThemeName.DarkPlus, "powershell", isExtension: false).ToList(); };

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("batchSize");
    }

    [Fact]
    public void ProcessFileInBatches_WithNonExistentFile_ThrowsFileNotFoundException()
    {
        // Arrange
        string filePath = "non-existent-file.txt";

        // Act
        Action act = () => { var _ = TextMateProcessor.ProcessFileInBatches(filePath, 100, ThemeName.DarkPlus, "powershell", isExtension: false).ToList(); };

        // Assert
        act.Should().Throw<FileNotFoundException>();
    }

    [Theory]
    [InlineData("csharp")]
    [InlineData("python")]
    [InlineData("javascript")]
    [InlineData("markdown")]
    public void ProcessLines_WithDifferentLanguages_Succeeds(string language)
    {
        // Arrange
        string[] lines = ["// comment", "var x = 1;"];

        // Act
        var result = TextMateProcessor.ProcessLines(lines, ThemeName.DarkPlus, language, isExtension: false);

        // Assert
        result.Should().NotBeNull();
    }

    [Theory]
    [InlineData(ThemeName.DarkPlus)]
    [InlineData(ThemeName.Light)]
    [InlineData(ThemeName.Monokai)]
    public void ProcessLines_WithDifferentThemes_Succeeds(ThemeName theme)
    {
        // Arrange
        string[] lines = ["$x = 1"];

        // Act
        var result = TextMateProcessor.ProcessLines(lines, theme, "powershell", isExtension: false);

        // Assert
        result.Should().NotBeNull();
    }
}
