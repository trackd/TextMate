using PwshSpectreConsole.TextMate.Extensions;

namespace PwshSpectreConsole.TextMate.Tests.Extensions;

public class StringBuilderExtensionsTests
{
    [Fact]
    public void AppendLink_ValidUrlAndText_GeneratesCorrectMarkup()
    {
        // Arrange
        var builder = new StringBuilder();
        var url = "https://example.com";
        var text = "Example Link";

        // Act
        builder.AppendLink(url, text);

        // Assert
        var result = builder.ToString();
        result.Should().Be("[link=https://example.com]Example Link[/]");
    }

    [Fact]
    public void AppendWithStyle_NoStyle_AppendsTextOnly()
    {
        // Arrange
        var builder = new StringBuilder();
        var text = "Hello World";

        // Act
        builder.AppendWithStyle(null, text);

        // Assert
        builder.ToString().Should().Be("Hello World");
    }

    [Fact]
    public void AppendWithStyle_WithStyle_GeneratesStyledMarkup()
    {
        // Arrange
        var builder = new StringBuilder();
        var style = new Style(Color.Red, Color.Blue, Decoration.Bold);
        var text = "Styled Text";

        // Act
        builder.AppendWithStyle(style, text);

        // Assert
        var result = builder.ToString();
        result.Should().Contain("red");
        result.Should().Contain("blue");
        result.Should().Contain("bold");
        result.Should().Contain("Styled Text");
        result.Should().StartWith("[");
        result.Should().EndWith("[/]");
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void AppendWithStyle_NullOrEmptyText_HandlesGracefully(string? text)
    {
        // Arrange
        var builder = new StringBuilder();
        var style = new Style(Color.Red);

        // Act
        builder.AppendWithStyle(style, text);

        // Assert
        var result = builder.ToString();
        result.Should().NotBeNull();
        result.Should().StartWith("[");
        result.Should().EndWith("[/]");
    }

    [Fact]
    public void AppendWithStyle_SpecialCharacters_EscapesMarkup()
    {
        // Arrange
        var builder = new StringBuilder();
        var text = "[brackets] and <angles>";

        // Act
        builder.AppendWithStyle(null, text);

        // Assert
        var result = builder.ToString();
        result.Should().Be("[brackets] and <angles>");
    }
}
