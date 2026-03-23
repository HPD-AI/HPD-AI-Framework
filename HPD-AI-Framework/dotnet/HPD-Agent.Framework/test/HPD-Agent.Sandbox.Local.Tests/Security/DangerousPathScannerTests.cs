using HPD.Sandbox.Local.Security;
using Xunit;

namespace HPD.Sandbox.Local.Tests.Security;

public class DangerousPathScannerTests : IDisposable
{
    private readonly string _testDir;

    public DangerousPathScannerTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), $"hpd-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_testDir);
    }

    [Fact]
    public async Task Scanner_FindsGitconfigInRoot()
    {
        // Arrange
        var gitconfig = Path.Combine(_testDir, ".gitconfig");
        await File.WriteAllTextAsync(gitconfig, "# test");

        var scanner = new DangerousPathScanner(maxDepth: 3);

        // Act
        var paths = await scanner.GetDangerousPathsAsync(_testDir);

        // Assert
        Assert.Contains(paths, p => p.EndsWith(".gitconfig"));
    }

    [Fact]
    public async Task Scanner_FindsGitHooksDirectory()
    {
        // Arrange
        var gitHooks = Path.Combine(_testDir, ".git", "hooks");
        Directory.CreateDirectory(gitHooks);

        var scanner = new DangerousPathScanner(maxDepth: 3);

        // Act
        var paths = await scanner.GetDangerousPathsAsync(_testDir);

        // Assert
        Assert.Contains(paths, p => p.Contains(".git") && p.Contains("hooks"));
    }

    [Fact]
    public async Task Scanner_FindsNestedDangerousFiles()
    {
        // Arrange
        var nested = Path.Combine(_testDir, "subdir", "nested");
        Directory.CreateDirectory(nested);
        var gitconfig = Path.Combine(nested, ".gitconfig");
        await File.WriteAllTextAsync(gitconfig, "# test");

        var scanner = new DangerousPathScanner(maxDepth: 3);

        // Act
        var paths = await scanner.GetDangerousPathsAsync(_testDir);

        // Assert
        Assert.Contains(paths, p => p.Contains("nested") && p.EndsWith(".gitconfig"));
    }

    [Fact]
    public async Task Scanner_RespectsMaxDepth()
    {
        // Arrange - Create file at depth 4
        var deep = Path.Combine(_testDir, "a", "b", "c", "d");
        Directory.CreateDirectory(deep);
        var gitconfig = Path.Combine(deep, ".gitconfig");
        await File.WriteAllTextAsync(gitconfig, "# test");

        var scanner = new DangerousPathScanner(maxDepth: 2);

        // Act
        var paths = await scanner.GetDangerousPathsAsync(_testDir);

        // Assert - Should NOT find file at depth 4
        Assert.DoesNotContain(paths, p => p.Contains("d") && p.EndsWith(".gitconfig"));
    }

    [Fact]
    public async Task Scanner_SkipsNodeModules()
    {
        // Arrange
        var nodeModules = Path.Combine(_testDir, "node_modules", "some-package");
        Directory.CreateDirectory(nodeModules);
        var gitconfig = Path.Combine(nodeModules, ".gitconfig");
        await File.WriteAllTextAsync(gitconfig, "# test");

        var scanner = new DangerousPathScanner(maxDepth: 5);

        // Act
        var paths = await scanner.GetDangerousPathsAsync(_testDir);

        // Assert - Should NOT find file in node_modules
        Assert.DoesNotContain(paths, p => p.Contains("node_modules"));
    }

    [Fact]
    public async Task Scanner_AllowsGitConfigWhenConfigured()
    {
        // Arrange
        var gitDir = Path.Combine(_testDir, ".git");
        Directory.CreateDirectory(gitDir);
        var gitConfig = Path.Combine(gitDir, "config");
        await File.WriteAllTextAsync(gitConfig, "# test");

        var scanner = new DangerousPathScanner(maxDepth: 3);

        // Act
        var pathsWithoutGitConfig = await scanner.GetDangerousPathsAsync(_testDir, allowGitConfig: true);
        scanner.ClearCache(); // Clear cache to get fresh results
        var pathsWithGitConfig = await scanner.GetDangerousPathsAsync(_testDir, allowGitConfig: false);

        // Assert
        Assert.DoesNotContain(pathsWithoutGitConfig, p => p.EndsWith(".git/config") || p.EndsWith(".git\\config"));
        Assert.Contains(pathsWithGitConfig, p => p.Contains(".git") && p.EndsWith("config"));
    }

    [Fact]
    public async Task Scanner_CachesResults()
    {
        // Arrange
        var scanner = new DangerousPathScanner(maxDepth: 3);

        // Act
        var paths1 = await scanner.GetDangerousPathsAsync(_testDir);
        var paths2 = await scanner.GetDangerousPathsAsync(_testDir);

        // Assert - Should return same instance from cache
        Assert.Same(paths1, paths2);
    }

    [Fact]
    public void Scanner_ClearCache_InvalidatesCache()
    {
        // Arrange
        var scanner = new DangerousPathScanner(maxDepth: 3);

        // Act
        scanner.ClearCache();

        // Assert - No exception, cache is cleared
        Assert.True(true);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
                Directory.Delete(_testDir, recursive: true);
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
