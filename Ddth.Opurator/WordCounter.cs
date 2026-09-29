namespace Ddth.Opurator;

/// <summary>
/// Provides word counting functionality.
/// </summary>
public static class WordCounter
{
    /// <summary>
    /// Counts the number of words in the supplied text.
    /// </summary>
    /// <param name="input">The text to inspect. May be <see langword="null"/>.</param>
    /// <returns>
    /// The number of whitespace-separated words. Returns <c>0</c> when
    /// <paramref name="input"/> is <see langword="null"/>, empty, or whitespace only.
    /// </returns>
    public static int CountWords(string? input)
    {
        if (string.IsNullOrWhiteSpace(input))
        {
            return 0;
        }

        return input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;
    }
}
