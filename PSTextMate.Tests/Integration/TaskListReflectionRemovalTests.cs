using PwshSpectreConsole.TextMate.Core;
using TextMateSharp.Grammars;

namespace PwshSpectreConsole.TextMate.Tests.Integration;

/// <summary>
/// Simple tests to verify that TaskList functionality works without reflection errors.
/// These tests use the public API to ensure the reflection code has been properly removed.
/// </summary>
public class TaskListReflectionRemovalTests
{
    [Fact]
    public void TextMateProcessor_MarkdownWithTaskList_ProcessesWithoutReflectionErrors()
    {
        // Arrange
        var markdown = """
            # Task List Test

            - [x] Completed task
            - [ ] Incomplete task
            - [X] Another completed task
            """;

        // Act & Assert - This should not throw reflection-related exceptions
        var exception = Record.Exception(() =>
        {
            var result = TextMateProcessor.ProcessLinesCodeBlock(
                lines: [markdown],
                themeName: ThemeName.DarkPlus,
                grammarId: "markdown",
                isExtension: false);

            // Verify result is not null
            result.Should().NotBeNull();
        });        // Assert - No exceptions should be thrown
        exception.Should().BeNull("TaskList processing should work without reflection errors");
    }

    [Theory]
    [InlineData("- [x] Completed task")]
    [InlineData("- [ ] Incomplete task")]
    [InlineData("- [X] Uppercase completed")]
    [InlineData("- Regular bullet point")]
    public void TextMateProcessor_VariousListFormats_ProcessesWithoutErrors(string listItem)
    {
        // Arrange
        var lines = new[] { "# Test", "", listItem };

        // Act & Assert - Should not throw any exceptions
        var exception = Record.Exception(() =>
        {
            var result = TextMateProcessor.ProcessLinesCodeBlock(
                lines: lines,
                themeName: ThemeName.DarkPlus,
                grammarId: "markdown",
                isExtension: false);

            result.Should().NotBeNull();
        });        exception.Should().BeNull($"Processing list item '{listItem}' should not throw exceptions");
    }

    [Fact]
    public void TextMateProcessor_ComplexMarkdownWithNestedTaskLists_ProcessesSuccessfully()
    {
        // Arrange
        var complexMarkdown = new[]
        {
            "# Complex Task List Example",
            "",
            "## Main Tasks",
            "- [x] Setup project",
            "  - [x] Initialize repository",
            "  - [x] Add .gitignore",
            "  - [ ] Configure CI/CD",
            "",
            "## Development Tasks",
            "1. [x] Write core functionality",
            "2. [ ] Add comprehensive tests",
            "   - [x] Unit tests",
            "   - [ ] Integration tests",
            "3. [ ] Documentation",
            "",
            "### Code Review Checklist",
            "- [X] Code follows style guidelines",
            "- [ ] Tests pass",
            "- [ ] Documentation updated"
        };

        // Act & Assert - This complex structure should process without any reflection errors
        var exception = Record.Exception(() =>
        {
            var result = TextMateProcessor.ProcessLinesCodeBlock(
                lines: complexMarkdown,
                themeName: ThemeName.DarkPlus,
                grammarId: "markdown",
                isExtension: false);

            result.Should().NotBeNull();
        });        exception.Should().BeNull("Complex nested TaskList processing should work without reflection");
    }
}
