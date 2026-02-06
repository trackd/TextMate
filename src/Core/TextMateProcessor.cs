using System.Text;
using PSTextMate.Core;
using PSTextMate.Utilities;
using Spectre.Console;
using Spectre.Console.Rendering;
using TextMateSharp.Grammars;
using TextMateSharp.Themes;

namespace PSTextMate.Core;

/// <summary>
/// Main entry point for TextMate processing operations.
/// Provides high-performance text processing using TextMate grammars and themes.
/// </summary>
public static class TextMateProcessor {
    /// <summary>
    /// Processes string lines for code blocks without escaping markup characters.
    /// This preserves raw source code content for proper syntax highlighting.
    /// </summary>
    /// <param name="lines">Array of text lines to process</param>
    /// <param name="themeName">Theme to apply for styling</param>
    /// <param name="grammarId">Language ID or file extension for grammar selection</param>
    /// <param name="isExtension">True if grammarId is a file extension, false if it's a language ID</param>
    /// <returns>Rendered rows with syntax highlighting, or null if processing fails</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="lines"/> is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when grammar cannot be found or processing encounters an error</exception>
    public static IRenderable[]? ProcessLines(string[] lines, ThemeName themeName, string grammarId, bool isExtension, bool forceAlternate = false) {
        ArgumentNullException.ThrowIfNull(lines, nameof(lines));

        if (lines.Length == 0 || lines.AllIsNullOrEmpty()) {
            return null;
        }

        try {
            (TextMateSharp.Registry.Registry registry, Theme theme) = CacheManager.GetCachedTheme(themeName);
            // Resolve grammar using CacheManager which knows how to map language ids and extensions
            IGrammar? grammar = CacheManager.GetCachedGrammar(registry, grammarId, isExtension) ?? throw new InvalidOperationException(isExtension ? $"Grammar not found for file extension: {grammarId}" : $"Grammar not found for language: {grammarId}");

            // if alternate it will use TextMate for markdown as well.
            return grammar.GetName() == "Markdown" && forceAlternate
                ? StandardRenderer.Render(lines, theme, grammar)
                : (grammar.GetName() == "Markdown")
                ? MarkdownRenderer.Render(lines, theme, grammar, themeName)
                : StandardRenderer.Render(lines, theme, grammar);
        }
        catch (InvalidOperationException) {
            throw;
        }
        catch (ArgumentException ex) {
            throw new InvalidOperationException($"Argument error processing lines with grammar '{grammarId}': {ex.Message}", ex);
        }
        catch (Exception ex) {
            throw new InvalidOperationException($"Unexpected error processing lines with grammar '{grammarId}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Processes string lines for code blocks without escaping markup characters.
    /// This preserves raw source code content for proper syntax highlighting.
    /// </summary>
    /// <param name="lines">Array of text lines to process</param>
    /// <param name="themeName">Theme to apply for styling</param>
    /// <param name="grammarId">Language ID or file extension for grammar selection</param>
    /// <param name="isExtension">True if grammarId is a file extension, false if it's a language ID</param>
    /// <returns>Rendered rows with syntax highlighting, or null if processing fails</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="lines"/> is null</exception>
    /// <exception cref="InvalidOperationException">Thrown when grammar cannot be found or processing encounters an error</exception>
    public static IRenderable[]? ProcessLinesCodeBlock(string[] lines, ThemeName themeName, string grammarId, bool isExtension = false) {
        ArgumentNullException.ThrowIfNull(lines, nameof(lines));

        try {
            (TextMateSharp.Registry.Registry registry, Theme theme) = CacheManager.GetCachedTheme(themeName);
            IGrammar? grammar = CacheManager.GetCachedGrammar(registry, grammarId, isExtension);

            if (grammar is null) {
                string errorMessage = isExtension
                    ? $"Grammar not found for file extension: {grammarId}"
                    : $"Grammar not found for language: {grammarId}";
                throw new InvalidOperationException(errorMessage);
            }

            // Always use StandardRenderer for code blocks, never MarkdownRenderer
            return RenderCodeBlock(lines, theme, grammar);
        }
        catch (InvalidOperationException) {
            throw;
        }
        catch (ArgumentException ex) {
            throw new InvalidOperationException($"Argument error processing code block with grammar '{grammarId}': {ex.Message}", ex);
        }
        catch (Exception ex) {
            throw new InvalidOperationException($"Unexpected error processing code block with grammar '{grammarId}': {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Renders code block lines without escaping markup characters.
    /// </summary>
    private static IRenderable[] RenderCodeBlock(string[] lines, Theme theme, IGrammar grammar) {
        StringBuilder builder = StringBuilderPool.Rent();
        try {
            List<IRenderable> rows = new(lines.Length);
            IStateStack? ruleStack = null;

            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++) {
                string line = lines[lineIndex];
                ITokenizeLineResult result = grammar.TokenizeLine(line, ruleStack, TimeSpan.MaxValue);
                ruleStack = result.RuleStack;
                TokenProcessor.ProcessTokensBatch(result.Tokens, line, theme, builder, lineIndex, escapeMarkup: false);
                string lineMarkup = builder.ToString();
                // Use Markup to parse the color codes generated by TextMateProcessor
                // If markup is empty, use an empty Text object instead
                rows.Add(string.IsNullOrEmpty(lineMarkup) ? Text.Empty : new Markup(lineMarkup));
                builder.Clear();
            }

            return [.. rows];
        }
        finally {
            StringBuilderPool.Return(builder);
        }
    }

    /// <summary>
    /// Processes an enumerable of lines in batches to support streaming/low-memory processing.
    /// Yields a HighlightedText result for each processed batch with metadata.
    /// </summary>
    /// <param name="lines">Enumerable of text lines to process</param>
    /// <param name="batchSize">Number of lines to process per batch (default: 1000 lines balances memory usage with throughput)</param>
    /// <param name="themeName">Theme to apply for styling</param>
    /// <param name="grammarId">Language ID or file extension for grammar selection</param>
    /// <param name="isExtension">True if grammarId is a file extension, false if it's a language ID</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests</param>
    /// <param name="progress">Optional progress reporter for tracking processing status</param>
    /// <returns>Enumerable of HighlightedText objects containing processed lines with batch metadata</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="lines"/> is null</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when <paramref name="batchSize"/> is less than or equal to zero</exception>
    /// <exception cref="InvalidOperationException">Thrown when grammar cannot be found</exception>
    /// <exception cref="OperationCanceledException">Thrown when cancellation is requested</exception>
    /// <remarks>
    /// Batch size considerations:
    /// - Smaller batches (100-500): Lower memory, more frequent progress updates, slightly higher overhead
    /// - Default (1000): Balanced approach for most scenarios
    /// - Larger batches (2000-5000): Better throughput for large files, higher memory usage
    /// </remarks>
    public static IEnumerable<HighlightedText> ProcessLinesInBatches(
        IEnumerable<string> lines,
        int batchSize,
        ThemeName themeName,
        string grammarId,
        bool isExtension = false,
        bool forceAlternate = false,
        bool useMarkdownLayout = false,
        IProgress<(int batchIndex, long linesProcessed)>? progress = null,
        CancellationToken cancellationToken = default) {
        ArgumentNullException.ThrowIfNull(lines, nameof(lines));
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(batchSize, nameof(batchSize));

        var buffer = new List<string>(batchSize);
        int batchIndex = 0;
        long fileOffset = 0; // starting line index for the next batch

        // Load theme and registry once and then resolve the requested grammar scope
        // directly on the registry. Avoid using the global grammar cache here because
        // TextMateSharp's Registry manages its own internal grammar store and repeated
        // LoadGrammar calls or cross-registry caching can cause duplicate-key exceptions.
        (TextMateSharp.Registry.Registry registry, Theme theme) = CacheManager.GetCachedTheme(themeName);
        // Resolve grammar using CacheManager which knows how to map language ids and extensions
        IGrammar? grammar = CacheManager.GetCachedGrammar(registry, grammarId, isExtension) ?? throw new InvalidOperationException(isExtension ? $"Grammar not found for file extension: {grammarId}" : $"Grammar not found for language: {grammarId}");
        bool useMarkdownRenderer = grammar.GetName() == "Markdown" && !forceAlternate;
        // If explicitly requested, prefer the Markdown layout even when forceAlternate is used
        if (grammar.GetName() == "Markdown" && useMarkdownLayout) {
            useMarkdownRenderer = true;
        }

        foreach (string? line in lines) {
            cancellationToken.ThrowIfCancellationRequested();

            buffer.Add(line ?? string.Empty);
            if (buffer.Count >= batchSize) {
                // Render the batch using the already-loaded grammar and theme
                IRenderable[]? result = useMarkdownRenderer
                    ? MarkdownRenderer.Render([.. buffer], theme, grammar, themeName)
                    : StandardRenderer.Render([.. buffer], theme, grammar);

                if (result is not null) {
                    yield return new HighlightedText {
                        Renderables = result,
                        BatchIndex = batchIndex,
                        FileOffset = fileOffset
                    };
                    progress?.Report((batchIndex, fileOffset + batchSize));
                    batchIndex++;
                }
                buffer.Clear();
                fileOffset += batchSize;
            }
        }

        // Process remaining lines
        if (buffer.Count > 0) {
            cancellationToken.ThrowIfCancellationRequested();

            IRenderable[]? result = useMarkdownRenderer
                ? MarkdownRenderer.Render([.. buffer], theme, grammar, themeName)
                : StandardRenderer.Render([.. buffer], theme, grammar);

            if (result is not null) {
                yield return new HighlightedText {
                    Renderables = result,
                    BatchIndex = batchIndex,
                    FileOffset = fileOffset
                };
                progress?.Report((batchIndex, fileOffset + buffer.Count));
            }
        }
    }

    /// <summary>
    /// Backward compatibility overload without cancellation and progress support.
    /// </summary>
    public static IEnumerable<HighlightedText> ProcessLinesInBatches(IEnumerable<string> lines, int batchSize, ThemeName themeName, string grammarId, bool isExtension = false)
        => ProcessLinesInBatches(lines, batchSize, themeName, grammarId, isExtension, false, false, null, CancellationToken.None);

    /// <summary>
    /// Helper to stream a file by reading lines lazily and processing them in batches.
    /// </summary>
    /// <param name="filePath">Path to the file to process</param>
    /// <param name="batchSize">Number of lines to process per batch</param>
    /// <param name="themeName">Theme to apply for styling</param>
    /// <param name="grammarId">Language ID or file extension for grammar selection</param>
    /// <param name="isExtension">True if grammarId is a file extension, false if it's a language ID</param>
    /// <param name="cancellationToken">Token to monitor for cancellation requests</param>
    /// <param name="progress">Optional progress reporter for tracking processing status</param>
    /// <returns>Enumerable of HighlightedText objects containing processed lines with batch metadata</returns>
    /// <exception cref="FileNotFoundException">Thrown when the specified file does not exist</exception>
    /// <exception cref="ArgumentNullException">Thrown when lines enumerable is null</exception>
    /// <exception cref="ArgumentOutOfRangeException">Thrown when batchSize is less than or equal to zero</exception>
    /// <exception cref="InvalidOperationException">Thrown when grammar cannot be found</exception>
    /// <exception cref="OperationCanceledException">Thrown when cancellation is requested</exception>
    public static IEnumerable<HighlightedText> ProcessFileInBatches(
        string filePath,
        int batchSize,
        ThemeName themeName,
        string grammarId,
        bool isExtension = false,
        bool forceAlternate = false,
        bool useMarkdownLayout = false,
        IProgress<(int batchIndex, long linesProcessed)>? progress = null,
        CancellationToken cancellationToken = default) {
        return !File.Exists(filePath)
            ? throw new FileNotFoundException(filePath)
            : ProcessLinesInBatches(File.ReadLines(filePath), batchSize, themeName, grammarId, isExtension, forceAlternate, useMarkdownLayout, progress, cancellationToken);
    }

    /// <summary>
    /// Backward compatibility overload without cancellation and progress support.
    /// </summary>
    public static IEnumerable<HighlightedText> ProcessFileInBatches(string filePath, int batchSize, ThemeName themeName, string grammarId, bool isExtension = false) => ProcessFileInBatches(filePath, batchSize, themeName, grammarId, isExtension, false, false, null, CancellationToken.None);
}
