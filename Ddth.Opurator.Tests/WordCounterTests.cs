using Ddth.Opurator;

namespace Ddth.Opurator.Tests;

public class WordCounterTests
{
    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("hello", 1)]
    [InlineData("hello world", 2)]
    [InlineData("  hello   world  ", 2)]
    [InlineData("one two three four five", 5)]
    [InlineData("line1\nline2\tline3", 3)]
    public void CountWords_ReturnsExpectedCount(string? input, int expected)
    {
        Assert.Equal(expected, WordCounter.CountWords(input));
    }

    [Theory]
    [InlineData(null, 0)]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    [InlineData("hello", 1)]
    [InlineData("hello world", 2)]
    [InlineData("  hello   world  ", 2)]
    [InlineData("one two three four five", 5)]
    [InlineData("line1\nline2\tline3", 3)]
    public void CountWords_Extension_ReturnsExpectedCount(string? input, int expected)
    {
        Assert.Equal(expected, input.CountWords());
    }
}
