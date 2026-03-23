using FluentAssertions;
using Xunit;

namespace HPD.Sandbox.Local.Tests;

public class DangerousFilesTests
{
    [Fact]
    public void EnvironmentFiles_ContainsCommonEnvFiles()
    {
        DangerousFiles.EnvironmentFiles.Should().Contain(".env");
        DangerousFiles.EnvironmentFiles.Should().Contain(".env.local");
        DangerousFiles.EnvironmentFiles.Should().Contain(".env.production");
        DangerousFiles.EnvironmentFiles.Should().Contain(".envrc");
    }

    [Fact]
    public void DangerousDirectories_ContainsClaudeDir()
    {
        DangerousFiles.DangerousDirectories.Should().Contain(".claude");
    }

    [Fact]
    public void GitPaths_ContainsHooksAndConfig()
    {
        DangerousFiles.GitPaths.Should().Contain(".git/hooks");
        DangerousFiles.GitPaths.Should().Contain(".git/config");
    }

    [Fact]
    public void GetAllBlockedPaths_IncludesAbsolutePaths()
    {
        var workingDir = "/home/user/project";
        var paths = DangerousFiles.GetAllBlockedPaths(workingDir).ToList();

        paths.Should().Contain("/home/user/project/.env");
        paths.Should().Contain("/home/user/project/.claude");
        paths.Should().Contain("/home/user/project/.git/hooks");
    }

    [Fact]
    public void GetAllBlockedPaths_IncludesGlobPatterns()
    {
        var workingDir = "/home/user/project";
        var paths = DangerousFiles.GetAllBlockedPaths(workingDir).ToList();

        // Should include recursive glob patterns
        paths.Should().Contain("**/.env");
        paths.Should().Contain("**/.claude/**");
        paths.Should().Contain("**/.git/hooks");
    }

    [Fact]
    public void GetAllBlockedPaths_HandlesWindowsStylePaths()
    {
        var workingDir = "C:\\Users\\dev\\project";
        var paths = DangerousFiles.GetAllBlockedPaths(workingDir).ToList();

        // Should still work with Path.Combine
        paths.Should().Contain(p => p.Contains(".env") && p.StartsWith("C:"));
    }

    [Theory]
    [InlineData(".env")]
    [InlineData(".env.local")]
    [InlineData(".env.development")]
    [InlineData(".env.production")]
    [InlineData(".env.staging")]
    public void EnvironmentFiles_AllCommonPatternsIncluded(string envFile)
    {
        DangerousFiles.EnvironmentFiles.Should().Contain(envFile);
    }
}
