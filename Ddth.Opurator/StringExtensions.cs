namespace Ddth.Opurator;

/// <summary>
/// Extension methods for <see cref="string"/>.
/// </summary>
public static class StringExtensions
{
    /// <summary>
    /// Counts the number of whitespace-separated words in the string.
    /// </summary>
    /// <param name="input">The string to inspect. May be <see langword="null"/>.</param>
    /// <returns>
    /// The number of words, or <c>0</c> when the string is
    /// <see langword="null"/>, empty, or whitespace only.
    /// </returns>
    public static int CountWords(this string? input)
    {
        return WordCounter.CountWords(input);
    }
}
