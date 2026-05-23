using HPD.Execution.Local.Platforms.Linux;
using HPD.Execution.Local.Security;
using Xunit;

namespace HPD.Execution.Local.Tests.Platforms.Linux;

public class BubblewrapFilesystemPlannerTests : IDisposable
{
    private readonly string _root;
    private readonly List<string> _cleanupPaths = [];

    public BubblewrapFilesystemPlannerTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"hpd-bwrap-plan-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    [Fact]
    public void PlanReadDenyMounts_Directory_IsTmpfsMount()
    {
        var denied = Path.Combine(_root, "secret");
        Directory.CreateDirectory(denied);

        var plan = BubblewrapFilesystemPlanner.PlanReadDenyMounts([denied]);

        var mount = Assert.Single(plan.Mounts);
        Assert.Equal(BubblewrapMountKind.Tmpfs, mount.Kind);
        Assert.Null(mount.SourcePath);
        Assert.Equal(PathNormalizer.Normalize(denied), mount.DestinationPath);
        Assert.Empty(plan.CleanupPaths);
    }

    [Fact]
    public void PlanReadDenyMounts_File_IsDevNullReadOnlyBind()
    {
        var denied = Path.Combine(_root, "secret.txt");
        File.WriteAllText(denied, "secret");

        var plan = BubblewrapFilesystemPlanner.PlanReadDenyMounts([denied]);

        var mount = Assert.Single(plan.Mounts);
        Assert.Equal(BubblewrapMountKind.ReadOnlyBind, mount.Kind);
        Assert.Equal("/dev/null", mount.SourcePath);
        Assert.Equal(PathNormalizer.Normalize(denied), mount.DestinationPath);
    }

    [Fact]
    public void PlanReadDenyMounts_Root_ExpandsChildrenAndSkipsPlatformRequiredPaths()
    {
        var plan = BubblewrapFilesystemPlanner.PlanReadDenyMounts(["/"]);

        Assert.NotEmpty(plan.Mounts);
        Assert.DoesNotContain(plan.Mounts, mount => mount.DestinationPath == "/");
        Assert.DoesNotContain(plan.Mounts, mount => mount.DestinationPath is "/dev" or "/proc" or "/sys");
        Assert.All(plan.Mounts, mount =>
            Assert.True(mount.Kind is BubblewrapMountKind.Tmpfs or BubblewrapMountKind.ReadOnlyBind));
    }

    [Fact]
    public void PlanReadDenyMounts_GlobPattern_ExpandsToConcreteMatches()
    {
        var nested = Path.Combine(_root, "nested");
        Directory.CreateDirectory(nested);
        var first = Path.Combine(_root, "root.secret");
        var second = Path.Combine(nested, "nested.secret");
        var ignored = Path.Combine(nested, "visible.txt");
        File.WriteAllText(first, "secret");
        File.WriteAllText(second, "secret");
        File.WriteAllText(ignored, "visible");

        var plan = BubblewrapFilesystemPlanner.PlanReadDenyMounts(
            [Path.Combine(_root, "**", "*.secret")]);

        Assert.Contains(plan.Mounts, mount => mount.DestinationPath == PathNormalizer.Normalize(first));
        Assert.Contains(plan.Mounts, mount => mount.DestinationPath == PathNormalizer.Normalize(second));
        Assert.DoesNotContain(plan.Mounts, mount => mount.DestinationPath == PathNormalizer.Normalize(ignored));
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void PlanReadAllowMounts_GlobPattern_ExpandsToReadOnlyBinds()
    {
        var allowedDir = Path.Combine(_root, "public");
        Directory.CreateDirectory(allowedDir);
        var first = Path.Combine(allowedDir, "a.json");
        var second = Path.Combine(allowedDir, "b.json");
        File.WriteAllText(first, "{}");
        File.WriteAllText(second, "{}");

        var plan = BubblewrapFilesystemPlanner.PlanReadAllowMounts(
            [Path.Combine(allowedDir, "*.json")]);

        Assert.Contains(plan.Mounts, mount =>
            mount.Kind == BubblewrapMountKind.ReadOnlyBind &&
            mount.SourcePath == PathNormalizer.Normalize(first) &&
            mount.DestinationPath == PathNormalizer.Normalize(first));
        Assert.Contains(plan.Mounts, mount =>
            mount.Kind == BubblewrapMountKind.ReadOnlyBind &&
            mount.SourcePath == PathNormalizer.Normalize(second) &&
            mount.DestinationPath == PathNormalizer.Normalize(second));
        Assert.Empty(plan.Warnings);
    }

    [Fact]
    public void PlanReadDenyMounts_UnmatchedGlob_ReportsWarning()
    {
        var plan = BubblewrapFilesystemPlanner.PlanReadDenyMounts(
            [Path.Combine(_root, "**", "*.missing")]);

        Assert.Empty(plan.Mounts);
        Assert.Contains(plan.Warnings, warning => warning.Contains("did not match", StringComparison.Ordinal));
    }

    [Fact]
    public void PlanSandboxFilesystemMounts_RebindsWritableRootAfterReadDenyOverlay()
    {
        var workspace = Path.Combine(_root, "workspace");
        var output = Path.Combine(workspace, "output");
        Directory.CreateDirectory(output);

        var plan = BubblewrapFilesystemPlanner.PlanSandboxFilesystemMounts(
            allowWritePaths: [output],
            denyReadPaths: [workspace],
            allowReadPaths: [],
            denyWritePaths: []);

        var firstWriteBind = IndexOfMount(plan.Mounts, BubblewrapMountKind.Bind, output);
        var readDenyOverlay = IndexOfMount(plan.Mounts, BubblewrapMountKind.Tmpfs, workspace);
        var lastWriteBind = LastIndexOfMount(plan.Mounts, BubblewrapMountKind.Bind, output);

        Assert.True(firstWriteBind >= 0);
        Assert.True(readDenyOverlay > firstWriteBind);
        Assert.True(lastWriteBind > readDenyOverlay);
    }

    [Fact]
    public void PlanSandboxFilesystemMounts_AppliesAllowReadAfterWritableRebindAndWriteDenyLast()
    {
        var workspace = Path.Combine(_root, "workspace");
        var output = Path.Combine(workspace, "output");
        var allowed = Path.Combine(workspace, "public");
        var deniedWrite = Path.Combine(output, ".npmrc");
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(allowed);
        File.WriteAllText(deniedWrite, "blocked");

        var plan = BubblewrapFilesystemPlanner.PlanSandboxFilesystemMounts(
            allowWritePaths: [output],
            denyReadPaths: [workspace],
            allowReadPaths: [allowed],
            denyWritePaths: [deniedWrite]);

        var readDenyOverlay = IndexOfMount(plan.Mounts, BubblewrapMountKind.Tmpfs, workspace);
        var lastWriteBind = LastIndexOfMount(plan.Mounts, BubblewrapMountKind.Bind, output);
        var allowReadBind = IndexOfMount(plan.Mounts, BubblewrapMountKind.ReadOnlyBind, allowed);
        var writeDenyBind = LastIndexOfMount(plan.Mounts, BubblewrapMountKind.ReadOnlyBind, deniedWrite);

        Assert.True(readDenyOverlay >= 0);
        Assert.True(lastWriteBind > readDenyOverlay);
        Assert.True(allowReadBind > lastWriteBind);
        Assert.True(writeDenyBind > allowReadBind);
    }

    [Fact]
    public void PlanWriteDenyMounts_ExistingPathUnderWritableRoot_IsReadOnlyBind()
    {
        var denied = Path.Combine(_root, ".gitconfig");
        File.WriteAllText(denied, "test");

        var plan = BubblewrapFilesystemPlanner.PlanWriteDenyMounts([denied], [_root]);

        var mount = Assert.Single(plan.Mounts);
        Assert.Equal(BubblewrapMountKind.ReadOnlyBind, mount.Kind);
        Assert.Equal(PathNormalizer.Normalize(denied), mount.SourcePath);
        Assert.Equal(PathNormalizer.Normalize(denied), mount.DestinationPath);
        Assert.Empty(plan.CleanupPaths);
    }

    [Fact]
    public void PlanWriteDenyMounts_NonExistentLeafUnderWritableRoot_BindsDevNullAtLeaf()
    {
        var denied = Path.Combine(_root, ".npmrc");

        var plan = BubblewrapFilesystemPlanner.PlanWriteDenyMounts([denied], [_root]);

        var mount = Assert.Single(plan.Mounts);
        Assert.Equal(BubblewrapMountKind.ReadOnlyBind, mount.Kind);
        Assert.Equal("/dev/null", mount.SourcePath);
        Assert.Equal(Path.Combine(PathNormalizer.Normalize(_root), ".npmrc"), mount.DestinationPath);
        Assert.Empty(plan.CleanupPaths);
    }

    [Fact]
    public void PlanWriteDenyMounts_MissingIntermediateUnderWritableRoot_BindsEmptyDirectoryAtFirstMissingComponent()
    {
        var denied = Path.Combine(_root, ".claude", "commands", "deploy.md");

        var plan = BubblewrapFilesystemPlanner.PlanWriteDenyMounts([denied], [_root]);

        var mount = Assert.Single(plan.Mounts);
        var cleanup = Assert.Single(plan.CleanupPaths);
        _cleanupPaths.Add(cleanup);
        Assert.Equal(BubblewrapMountKind.ReadOnlyBind, mount.Kind);
        Assert.Equal(cleanup, mount.SourcePath);
        Assert.True(Directory.Exists(cleanup));
        Assert.Equal(Path.Combine(PathNormalizer.Normalize(_root), ".claude"), mount.DestinationPath);
    }

    [Fact]
    public void PlanWriteDenyMounts_DuplicateMissingIntermediate_EmitsSingleMountAndCleanupSource()
    {
        var first = Path.Combine(_root, ".claude", "commands", "deploy.md");
        var second = Path.Combine(_root, ".claude", "agents", "review.md");

        var plan = BubblewrapFilesystemPlanner.PlanWriteDenyMounts([first, second], [_root]);
        _cleanupPaths.AddRange(plan.CleanupPaths);

        var mount = Assert.Single(plan.Mounts);
        var cleanup = Assert.Single(plan.CleanupPaths);
        Assert.Equal(Path.Combine(PathNormalizer.Normalize(_root), ".claude"), mount.DestinationPath);
        Assert.Equal(cleanup, mount.SourcePath);
        Assert.True(Directory.Exists(cleanup));
    }

    [Fact]
    public void PlanWriteDenyMounts_PathOutsideWritableRoots_IsSkipped()
    {
        var denied = Path.Combine(Path.GetTempPath(), $"hpd-outside-{Guid.NewGuid():N}", ".npmrc");

        var plan = BubblewrapFilesystemPlanner.PlanWriteDenyMounts([denied], [_root]);

        Assert.Empty(plan.Mounts);
        Assert.Empty(plan.CleanupPaths);
    }

    [Fact]
    public void PlanWriteDenyMounts_SymlinkComponentUnderWritableRoot_ProtectsSymlinkItself()
    {
        var decoy = Path.Combine(_root, "decoy");
        Directory.CreateDirectory(decoy);
        var symlink = Path.Combine(_root, ".claude");
        if (!TryCreateDirectorySymlink(symlink, decoy))
            return;
        var denied = Path.Combine(symlink, "commands", "deploy.md");

        var plan = BubblewrapFilesystemPlanner.PlanWriteDenyMounts([denied], [_root]);
        _cleanupPaths.AddRange(plan.CleanupPaths);

        Assert.Contains(plan.Mounts, mount =>
            mount.Kind == BubblewrapMountKind.ReadOnlyBind &&
            mount.SourcePath == "/dev/null" &&
            mount.DestinationPath == symlink);
    }

    [Fact]
    public void PlanWriteDenyMounts_SymlinkPointingOutsideWritableRoot_StillProtectsSymlinkItself()
    {
        var outside = Path.Combine(Path.GetTempPath(), $"hpd-bwrap-outside-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outside);
        _cleanupPaths.Add(outside);
        var symlink = Path.Combine(_root, ".claude");
        if (!TryCreateDirectorySymlink(symlink, outside))
            return;
        var denied = Path.Combine(symlink, "commands", "deploy.md");

        var plan = BubblewrapFilesystemPlanner.PlanWriteDenyMounts([denied], [_root]);

        Assert.Contains(plan.Mounts, mount =>
            mount.Kind == BubblewrapMountKind.ReadOnlyBind &&
            mount.SourcePath == "/dev/null" &&
            mount.DestinationPath == symlink);
        Assert.DoesNotContain(plan.Mounts, mount =>
            mount.DestinationPath.StartsWith(outside, StringComparison.Ordinal));
    }

    public void Dispose()
    {
        foreach (var cleanupPath in _cleanupPaths)
        {
            if (Directory.Exists(cleanupPath))
                Directory.Delete(cleanupPath, recursive: true);
        }

        if (Directory.Exists(_root))
            Directory.Delete(_root, recursive: true);
    }

    private static bool TryCreateDirectorySymlink(string symlink, string target)
    {
        try
        {
            Directory.CreateSymbolicLink(symlink, target);
            return true;
        }
        catch (Exception ex) when (
            ex is IOException ||
            ex is UnauthorizedAccessException ||
            ex is PlatformNotSupportedException)
        {
            return false;
        }
    }

    private static int IndexOfMount(
        IReadOnlyList<BubblewrapMount> mounts,
        BubblewrapMountKind kind,
        string destinationPath)
    {
        var normalizedDestination = PathNormalizer.Normalize(destinationPath);
        for (var i = 0; i < mounts.Count; i++)
        {
            if (mounts[i].Kind == kind && mounts[i].DestinationPath == normalizedDestination)
                return i;
        }

        return -1;
    }

    private static int LastIndexOfMount(
        IReadOnlyList<BubblewrapMount> mounts,
        BubblewrapMountKind kind,
        string destinationPath)
    {
        var normalizedDestination = PathNormalizer.Normalize(destinationPath);
        for (var i = mounts.Count - 1; i >= 0; i--)
        {
            if (mounts[i].Kind == kind && mounts[i].DestinationPath == normalizedDestination)
                return i;
        }

        return -1;
    }
}
