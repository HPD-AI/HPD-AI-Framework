using System.Runtime.InteropServices;
using HPD.Sandbox.Local.Security;
using Xunit;

namespace HPD.Sandbox.Local.Tests.Security;

public class PathNormalizerTests
{
    [Fact]
    public void Normalize_ExpandsTilde()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var result = PathNormalizer.Normalize("~/test");

        Assert.StartsWith(home, result);
        Assert.EndsWith("test", result);
    }

    [Fact]
    public void Normalize_ExpandsTildeAlone()
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var result = PathNormalizer.Normalize("~");

        Assert.Equal(home, result);
    }

    [Fact]
    public void Normalize_ConvertsRelativeToAbsolute()
    {
        var result = PathNormalizer.Normalize("./test");

        Assert.True(Path.IsPathRooted(result));
    }

    [Fact]
    public void Normalize_HandlesAbsolutePath()
    {
        var path = RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? @"C:\test\path"
            : "/test/path";
        var result = PathNormalizer.Normalize(path, resolveSymlinks: false);

        Assert.Equal(path, result);
    }

    [Fact]
    public void Normalize_ThrowsOnEmptyPath()
    {
        Assert.Throws<ArgumentException>(() => PathNormalizer.Normalize(""));
    }

    [Fact]
    public void Normalize_ThrowsOnWhitespacePath()
    {
        Assert.Throws<ArgumentException>(() => PathNormalizer.Normalize("   "));
    }

    [Fact]
    public void NormalizeForComparison_ReturnsLowercase()
    {
        var result = PathNormalizer.NormalizeForComparison("/Test/PATH/File.TXT");

        Assert.Equal("/test/path/file.txt", result);
    }

    [Fact]
    public void IsWithinPath_ReturnsTrueForSubpath()
    {
        Assert.True(PathNormalizer.IsWithinPath("/home/user/project/file.txt", "/home/user/project"));
    }

    [Fact]
    public void IsWithinPath_ReturnsTrueForSamePath()
    {
        Assert.True(PathNormalizer.IsWithinPath("/home/user/project", "/home/user/project"));
    }

    [Fact]
    public void IsWithinPath_ReturnsFalseForOutsidePath()
    {
        Assert.False(PathNormalizer.IsWithinPath("/home/user/other", "/home/user/project"));
    }

    [Fact]
    public void IsWithinPath_ReturnsFalseForParentPath()
    {
        Assert.False(PathNormalizer.IsWithinPath("/home/user", "/home/user/project"));
    }

    [Fact]
    public void GetAncestors_ReturnsParentDirectories()
    {
        var ancestors = PathNormalizer.GetAncestors("/home/user/project/src/file.cs").ToList();

        Assert.Contains("/home/user/project/src", ancestors);
        Assert.Contains("/home/user/project", ancestors);
        Assert.Contains("/home/user", ancestors);
        Assert.Contains("/home", ancestors);
    }

    [Fact]
    public void ContainsGlobChars_DetectsAsterisk()
    {
        Assert.True(PathNormalizer.ContainsGlobChars("*.txt"));
    }

    [Fact]
    public void ContainsGlobChars_DetectsDoubleAsterisk()
    {
        Assert.True(PathNormalizer.ContainsGlobChars("**/*.cs"));
    }

    [Fact]
    public void ContainsGlobChars_DetectsQuestionMark()
    {
        Assert.True(PathNormalizer.ContainsGlobChars("file?.txt"));
    }

    [Fact]
    public void ContainsGlobChars_DetectsBrackets()
    {
        Assert.True(PathNormalizer.ContainsGlobChars("file[0-9].txt"));
    }

    [Fact]
    public void ContainsGlobChars_ReturnsFalseForNormalPath()
    {
        Assert.False(PathNormalizer.ContainsGlobChars("/home/user/file.txt"));
    }
}
