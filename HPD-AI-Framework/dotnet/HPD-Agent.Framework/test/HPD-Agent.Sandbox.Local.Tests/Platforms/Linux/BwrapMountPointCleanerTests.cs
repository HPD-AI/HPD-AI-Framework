using HPD.Sandbox.Local.Platforms.Linux;
using Xunit;

namespace HPD.Sandbox.Local.Tests.Platforms.Linux;

public sealed class BwrapMountPointCleanerTests : IDisposable
{
    private readonly string _root;

    public BwrapMountPointCleanerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"hpd-bwrap-cleaner-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void CleanupWhenIdle_RemovesTrackedEmptyDirectory()
    {
        var cleaner = new BwrapMountPointCleaner();
        var path = Path.Combine(_root, "empty");
        Directory.CreateDirectory(path);

        cleaner.Track([path]);
        cleaner.CleanupWhenIdle();

        Assert.False(Directory.Exists(path));
        Assert.Equal(0, cleaner.TrackedPathCount);
    }

    [Fact]
    public void CleanupWhenIdle_RemovesTrackedEmptyFile()
    {
        var cleaner = new BwrapMountPointCleaner();
        var path = Path.Combine(_root, "empty-file");
        File.WriteAllText(path, string.Empty);

        cleaner.Track([path]);
        cleaner.CleanupWhenIdle();

        Assert.False(File.Exists(path));
    }

    [Fact]
    public void CleanupWhenIdle_DoesNotRemoveNonEmptyDirectory()
    {
        var cleaner = new BwrapMountPointCleaner();
        var path = Path.Combine(_root, "non-empty");
        Directory.CreateDirectory(path);
        File.WriteAllText(Path.Combine(path, "kept.txt"), "data");

        cleaner.Track([path]);
        cleaner.CleanupWhenIdle();

        Assert.True(Directory.Exists(path));
        Assert.True(File.Exists(Path.Combine(path, "kept.txt")));
        Assert.Equal(0, cleaner.TrackedPathCount);
    }

    [Fact]
    public void CleanupWhenIdle_DoesNotRemoveNonEmptyFile()
    {
        var cleaner = new BwrapMountPointCleaner();
        var path = Path.Combine(_root, "non-empty-file");
        File.WriteAllText(path, "data");

        cleaner.Track([path]);
        cleaner.CleanupWhenIdle();

        Assert.True(File.Exists(path));
    }

    [Fact]
    public void CleanupWhenIdle_DefersWhileInvocationIsActive()
    {
        var cleaner = new BwrapMountPointCleaner();
        var path = Path.Combine(_root, "active");
        Directory.CreateDirectory(path);

        cleaner.Track([path]);
        using (cleaner.BeginInvocation())
        {
            cleaner.CleanupWhenIdle();

            Assert.True(Directory.Exists(path));
            Assert.Equal(1, cleaner.TrackedPathCount);
        }

        Assert.False(Directory.Exists(path));
        Assert.Equal(0, cleaner.TrackedPathCount);
    }

    [Fact]
    public void ForceCleanup_RemovesTrackedPathEvenWhileInvocationIsActive()
    {
        var cleaner = new BwrapMountPointCleaner();
        var path = Path.Combine(_root, "force");
        Directory.CreateDirectory(path);

        cleaner.Track([path]);
        using (cleaner.BeginInvocation())
        {
            cleaner.ForceCleanup();
        }

        Assert.False(Directory.Exists(path));
        Assert.Equal(0, cleaner.TrackedPathCount);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }
}
