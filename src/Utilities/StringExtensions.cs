using System.Text;

namespace PSTextMate.Utilities;

/// <summary>
/// Provides optimized string manipulation methods using modern .NET performance patterns.
/// Uses Span and ReadOnlySpan to minimize memory allocations during text processing.
/// </summary>
public static class StringExtensions
{
    /// <summary>
    /// Efficiently extracts substring using Span to avoid string allocations.
    /// This is significantly faster than traditional substring operations for large text processing.
    /// </summary>
    /// <param name="source">Source string to extract from</param>
    /// <param name="startIndex">Starting index for substring</param>
    /// <param name="endIndex">Ending index for substring</param>
    /// <returns>ReadOnlySpan representing the substring</returns>
    public static ReadOnlySpan<char> SpanSubstring(this string source, int startIndex, int endIndex)
    {
        return startIndex < 0 || endIndex > source.Length || startIndex > endIndex
            ? []
            : source.AsSpan(startIndex, endIndex - startIndex);
    }

    /// <summary>
    /// Optimized substring method that works with spans internally but returns a string.
    /// Provides better performance than traditional substring while maintaining string return type.
    /// </summary>
    /// <param name="source">Source string to extract from</param>
    /// <param name="startIndex">Starting index for substring</param>
    /// <param name="endIndex">Ending index for substring</param>
    /// <returns>Substring as string, or empty string if invalid indexes</returns>
    public static string SubstringAtIndexes(this string source, int startIndex, int endIndex)
    {
        ReadOnlySpan<char> span = source.SpanSubstring(startIndex, endIndex);
        return span.IsEmpty ? string.Empty : span.ToString();
    }

    /// <summary>
    /// Checks if all strings in the array are null or empty.
    /// Uses modern pattern matching for cleaner, more efficient code.
    /// </summary>
    /// <param name="strings">Array of strings to check</param>
    /// <returns>True if all strings are null or empty, false otherwise</returns>
    public static bool AllIsNullOrEmpty(this string[] strings)
        => strings.All(string.IsNullOrEmpty);

    /// <summary>
    /// Joins string arrays using span operations for better performance.
    /// Avoids multiple string allocations during concatenation.
    /// </summary>
    /// <param name="values">Array of strings to join</param>
    /// <param name="separator">Separator character</param>
    /// <returns>Joined string</returns>
    public static string SpanJoin(this string[] values, char separator)
    {
        if (values.Length == 0) return string.Empty;
        if (values.Length == 1) return values[0] ?? string.Empty;

        // Calculate total capacity to avoid StringBuilder reallocations
        int totalLength = values.Length - 1; // separators
        foreach (string value in values)
            totalLength += value?.Length ?? 0;

        var builder = new StringBuilder(totalLength);

        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0) builder.Append(separator);
            if (values[i] is not null)
                builder.Append(values[i].AsSpan());
        }

        return builder.ToString();
    }

    /// <summary>
    /// Splits strings using span operations with pre-allocated results array.
    /// Provides better performance for known maximum split counts.
    /// </summary>
    /// <param name="source">Source string to split</param>
    /// <param name="separators">Array of separator characters</param>
    /// <param name="options">String split options</param>
    /// <param name="maxSplits">Maximum expected number of splits for optimization</param>
    /// <returns>Array of split strings</returns>
    public static string[] SpanSplit(this string source, char[] separators, StringSplitOptions options = StringSplitOptions.None, int maxSplits = 16)
    {
        if (string.IsNullOrEmpty(source))
            return [];

        // Use span-based operations for better performance
        ReadOnlySpan<char> sourceSpan = source.AsSpan();
        var results = new List<string>(Math.Min(maxSplits, 64)); // Cap initial capacity

        int start = 0;
        for (int i = 0; i <= sourceSpan.Length; i++)
        {
            bool isSeparator = i < sourceSpan.Length && separators.Contains(sourceSpan[i]);
            bool isEnd = i == sourceSpan.Length;

            if (isSeparator || isEnd)
            {
                ReadOnlySpan<char> segment = sourceSpan[start..i];

                if (options.HasFlag(StringSplitOptions.RemoveEmptyEntries) && segment.IsEmpty)
                {
                    start = i + 1;
                    continue;
                }

                if (options.HasFlag(StringSplitOptions.TrimEntries))
                    segment = segment.Trim();

                results.Add(segment.ToString());
                start = i + 1;
            }
        }

        return [.. results];
    }

    /// <summary>
    /// Trims whitespace using span operations and returns the result as a string.
    /// More efficient than traditional Trim() for subsequent string operations.
    /// </summary>
    /// <param name="source">Source string to trim</param>
    /// <returns>Trimmed string</returns>
    public static string SpanTrim(this string source)
    {
        if (string.IsNullOrEmpty(source))
            return source ?? string.Empty;

        ReadOnlySpan<char> trimmed = source.AsSpan().Trim();
        return trimmed.Length == source.Length ? source : trimmed.ToString();
    }

    /// <summary>
    /// Efficiently checks if a string contains any of the specified characters using spans.
    /// </summary>
    /// <param name="source">Source string to search</param>
    /// <param name="chars">Characters to search for</param>
    /// <returns>True if any character is found</returns>
    public static bool SpanContainsAny(this string source, ReadOnlySpan<char> chars)
        => !string.IsNullOrEmpty(source) && !chars.IsEmpty && source.AsSpan().IndexOfAny(chars) >= 0;

    /// <summary>
    /// Replaces characters in a string using span operations for better performance.
    /// </summary>
    /// <param name="source">Source string</param>
    /// <param name="oldChar">Character to replace</param>
    /// <param name="newChar">Replacement character</param>
    /// <returns>String with replacements</returns>
    public static string SpanReplace(this string source, char oldChar, char newChar)
    {
        if (string.IsNullOrEmpty(source))
            return source ?? string.Empty;

        ReadOnlySpan<char> sourceSpan = source.AsSpan();
        int firstIndex = sourceSpan.IndexOf(oldChar);

        if (firstIndex < 0)
            return source; // No replacement needed

        // Use span-based building for efficiency
        var result = new StringBuilder(source.Length);
        int lastIndex = 0;

        do
        {
            result.Append(sourceSpan[lastIndex..firstIndex]);
            result.Append(newChar);
            lastIndex = firstIndex + 1;

            if (lastIndex >= sourceSpan.Length)
                break;

            firstIndex = sourceSpan[lastIndex..].IndexOf(oldChar);
            if (firstIndex >= 0)
                firstIndex += lastIndex;

        } while (firstIndex >= 0);

        if (lastIndex < sourceSpan.Length)
            result.Append(sourceSpan[lastIndex..]);

        return result.ToString();
    }
}
