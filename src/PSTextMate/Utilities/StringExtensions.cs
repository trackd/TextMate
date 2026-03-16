namespace PSTextMate.Utilities;

public static class StringExtensions {

    /// <summary>
    /// Checks if all strings in the array are null or empty.
    /// Uses modern pattern matching for cleaner, more efficient code.
    /// </summary>
    /// <param name="strings">Array of strings to check</param>
    /// <returns>True if all strings are null or empty, false otherwise</returns>
    public static bool AllIsNullOrEmpty(this string[] strings)
        => strings.All(string.IsNullOrEmpty);

}
