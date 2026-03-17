using HPD.Sandbox.Local.Platforms.MacOS;
using Xunit;

namespace HPD.Sandbox.Local.Tests.Platforms.MacOS;

public class GlobToRegexTests
{
    [Theory]
    [InlineData("*", "^[^/]*$")]
    [InlineData("*.txt", "^[^/]*\\.txt$")]
    [InlineData("*.cs", "^[^/]*\\.cs$")]
    public void Convert_SingleAsterisk_MatchesNonSlashChars(string glob, string expectedRegex)
    {
        var result = GlobToRegex.Convert(glob);
        Assert.Equal(expectedRegex, result);
    }

    [Theory]
    [InlineData("**", "^.*$")]
    [InlineData("**/file.txt", "^(.*/)?file\\.txt$")]
    [InlineData("src/**/*.cs", "^src/(.*/)?[^/]*\\.cs$")]
    public void Convert_DoubleAsterisk_MatchesAnyCharsIncludingSlash(string glob, string expectedRegex)
    {
        var result = GlobToRegex.Convert(glob);
        Assert.Equal(expectedRegex, result);
    }

    [Theory]
    [InlineData("file?.txt", "^file[^/]\\.txt$")]
    [InlineData("???.cs", "^[^/][^/][^/]\\.cs$")]
    public void Convert_QuestionMark_MatchesSingleNonSlashChar(string glob, string expectedRegex)
    {
        var result = GlobToRegex.Convert(glob);
        Assert.Equal(expectedRegex, result);
    }

    [Theory]
    [InlineData("[abc]", "^[abc]$")]
    [InlineData("[a-z]", "^[a-z]$")]
    [InlineData("file[0-9].txt", "^file[0-9]\\.txt$")]
    public void Convert_CharacterClass_PassesThrough(string glob, string expectedRegex)
    {
        var result = GlobToRegex.Convert(glob);
        Assert.Equal(expectedRegex, result);
    }

    [Theory]
    [InlineData("[!abc]", "^[^abc]$")]
    [InlineData("[!0-9]", "^[^0-9]$")]
    public void Convert_NegatedCharacterClass_ConvertsToCaretSyntax(string glob, string expectedRegex)
    {
        var result = GlobToRegex.Convert(glob);
        Assert.Equal(expectedRegex, result);
    }

    [Theory]
    [InlineData("file.txt", "^file\\.txt$")]
    [InlineData("path/to/file", "^path/to/file$")]
    [InlineData("no-glob-here", "^no-glob-here$")]
    public void Convert_NoGlobChars_EscapesSpecialChars(string glob, string expectedRegex)
    {
        var result = GlobToRegex.Convert(glob);
        Assert.Equal(expectedRegex, result);
    }

    [Fact]
    public void Convert_Empty_ReturnsEmptyPattern()
    {
        var result = GlobToRegex.Convert("");
        Assert.Equal("^$", result);
    }

    [Theory]
    [InlineData("*.txt")]
    [InlineData("**/*.cs")]
    [InlineData("file?.log")]
    [InlineData("[abc].txt")]
    public void ContainsGlobChars_ReturnsTrue_ForGlobPatterns(string pattern)
    {
        Assert.True(GlobToRegex.ContainsGlobChars(pattern));
    }

    [Theory]
    [InlineData("file.txt")]
    [InlineData("/path/to/file")]
    [InlineData("no-special-chars")]
    public void ContainsGlobChars_ReturnsFalse_ForNormalPaths(string path)
    {
        Assert.False(GlobToRegex.ContainsGlobChars(path));
    }

    [Fact]
    public void ConvertAndEscape_DoublesBackslashes()
    {
        var result = GlobToRegex.ConvertAndEscape("*.txt");

        // Should have double backslashes for sandbox profile string
        Assert.Contains("\\\\", result);
    }
}
