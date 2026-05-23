using HPD.Execution.Local.Platforms.Linux;
using HPD.Execution.Local.Security;
using Xunit;

namespace HPD.Execution.Local.Tests.Platforms.Linux;

public class BubblewrapBuilderTests
{
    [Fact]
    public void Build_IncludesNewSession()
    {
        var builder = new BubblewrapBuilder();
        var cmd = builder.Build("echo test");

        Assert.Contains("--new-session", cmd);
    }

    [Fact]
    public void Build_IncludesDieWithParent()
    {
        var builder = new BubblewrapBuilder();
        var cmd = builder.Build("echo test");

        Assert.Contains("--die-with-parent", cmd);
    }

    [Fact]
    public void BuildCommand_PreservesSandboxExecutableAndArgumentList()
    {
        var builder = new BubblewrapBuilder();
        var command = builder.BuildCommand("'node' 'script.js' 'safe; touch /tmp/pwned'");

        Assert.Equal("bwrap", command.FileName);
        Assert.Contains("--", command.ArgumentList);
        Assert.Contains("/bin/sh", command.ArgumentList);
        Assert.Contains("-c", command.ArgumentList);
        Assert.Equal("'node' 'script.js' 'safe; touch /tmp/pwned'", command.ArgumentList[^1]);
    }

    [Fact]
    public void WithReadOnlyRoot_AddsRoBind()
    {
        var builder = new BubblewrapBuilder();
        builder.WithReadOnlyRoot();
        var cmd = builder.Build("echo test");

        Assert.Contains("--ro-bind", cmd);
        Assert.Contains("'/'", cmd);
    }

    [Fact]
    public void WithWritablePath_AddsBindMount()
    {
        // Use a path that exists on any system (temp directory)
        var existingPath = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var builder = new BubblewrapBuilder();
        builder.WithWritablePath(existingPath);
        var cmd = builder.Build("echo test");

        Assert.Contains("--bind", cmd);
        // The path gets normalized and quoted
        Assert.Contains(existingPath, cmd);
    }

    [Fact]
    public void WithTmpfs_AddsTmpfsMount()
    {
        var builder = new BubblewrapBuilder();
        builder.WithTmpfs("/tmp");
        var cmd = builder.Build("echo test");

        Assert.Contains("--tmpfs", cmd);
        Assert.Contains("'/tmp'", cmd);
    }

    [Fact]
    public void WithAllowedReadPath_AddsReadOnlyBindMount()
    {
        var existingPath = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var builder = new BubblewrapBuilder();
        builder.WithAllowedReadPath(existingPath);
        var cmd = builder.Build("echo test");

        Assert.Contains("--ro-bind", cmd);
        Assert.Contains(existingPath, cmd);
    }

    [Fact]
    public void WithAllowedReadPath_AfterDeniedReadPath_RebindsAllowPathAfterTmpfs()
    {
        using var tempRoot = new TempDirectory();
        var denied = Path.Combine(tempRoot.Path, "secret");
        var allowed = Path.Combine(denied, "public");
        Directory.CreateDirectory(allowed);

        var builder = new BubblewrapBuilder();
        builder.WithDeniedReadPath(denied);
        builder.WithAllowedReadPath(allowed);
        var args = builder.GetArguments();

        var tmpfsIndex = args.Select((arg, index) => (arg, index))
            .First(item => item.arg == "--tmpfs")
            .index;
        var normalizedAllowed = PathNormalizer.Normalize(allowed);
        var allowedIndex = args.Select((arg, index) => (arg, index))
            .Last(item => item.arg == normalizedAllowed)
            .index;

        Assert.True(tmpfsIndex >= 0);
        Assert.True(allowedIndex > tmpfsIndex);
    }

    [Fact]
    public void WithDeniedReadPath_Root_DoesNotTmpfsRoot()
    {
        var builder = new BubblewrapBuilder();
        builder.WithDeniedReadPath("/");
        var args = builder.GetArguments();

        Assert.DoesNotContain(args.Select((arg, index) => (arg, index)), item =>
            item.arg == "--tmpfs" &&
            item.index + 1 < args.Count &&
            args[item.index + 1] == "/");
    }

    [Fact]
    public void WithDeniedReadPaths_UnmatchedGlob_StoresFilesystemWarning()
    {
        using var tempRoot = new TempDirectory();
        var builder = new BubblewrapBuilder();
        builder.WithDeniedReadPaths([Path.Combine(tempRoot.Path, "**", "*.missing")]);

        Assert.Contains(builder.GetFilesystemWarnings(), warning =>
            warning.Contains("did not match", StringComparison.Ordinal));
    }

    [Fact]
    public void WithFilesystemPlan_AppliesWriteDenyAfterAllowRead()
    {
        using var tempRoot = new TempDirectory();
        var workspace = Path.Combine(tempRoot.Path, "workspace");
        var output = Path.Combine(workspace, "output");
        var allowed = Path.Combine(workspace, "public");
        var deniedWrite = Path.Combine(output, ".npmrc");
        Directory.CreateDirectory(output);
        Directory.CreateDirectory(allowed);
        File.WriteAllText(deniedWrite, "blocked");

        var builder = new BubblewrapBuilder();
        builder.WithFilesystemPlan(
            allowWritePaths: [output],
            denyReadPaths: [workspace],
            allowReadPaths: [allowed],
            denyWritePaths: [deniedWrite]);
        var args = builder.GetArguments();

        var allowReadIndex = LastIndexOfArg(args, PathNormalizer.Normalize(allowed));
        var writeDenyIndex = LastIndexOfArg(args, PathNormalizer.Normalize(deniedWrite));

        Assert.True(allowReadIndex >= 0);
        Assert.True(writeDenyIndex > allowReadIndex);
    }

    [Fact]
    public void WithNetworkIsolation_AddsUnshareNet()
    {
        var builder = new BubblewrapBuilder();
        builder.WithNetworkIsolation();
        var cmd = builder.Build("echo test");

        Assert.Contains("--unshare-net", cmd);
    }

    [Fact]
    public void WithPidIsolation_AddsUnsharePid()
    {
        var builder = new BubblewrapBuilder();
        builder.WithPidIsolation();
        var cmd = builder.Build("echo test");

        Assert.Contains("--unshare-pid", cmd);
        Assert.Contains("--unshare-uts", cmd);
        Assert.Contains("--proc", cmd);
    }

    [Fact]
    public void WithWeakerNestedSandbox_AddsUserNamespaceAndProcBind()
    {
        var builder = new BubblewrapBuilder();
        builder.WithWeakerNestedSandbox();
        var args = builder.GetArguments();

        Assert.Contains("--unshare-user", args);
        Assert.DoesNotContain("--unshare-pid", args);
        Assert.Contains(args.Select((arg, index) => (arg, index)), item =>
            item.arg == "--bind" &&
            item.index + 2 < args.Count &&
            args[item.index + 1] == "/proc" &&
            args[item.index + 2] == "/proc");
    }

    [Fact]
    public void WithDevices_AddsDevMount()
    {
        var builder = new BubblewrapBuilder();
        builder.WithDevices();
        var cmd = builder.Build("echo test");

        Assert.Contains("--dev", cmd);
        Assert.Contains("'/dev'", cmd);
    }

    [Fact]
    public void WithEnvironmentVariable_AddsSetsEnv()
    {
        var builder = new BubblewrapBuilder();
        builder.WithEnvironmentVariable("TEST_VAR", "test_value");
        var cmd = builder.Build("echo test");

        Assert.Contains("--setenv", cmd);
        Assert.Contains("'TEST_VAR'", cmd);
        Assert.Contains("'test_value'", cmd);
    }

    [Fact]
    public void Build_IncludesShellAndCommand()
    {
        var builder = new BubblewrapBuilder();
        var cmd = builder.Build("echo hello", "/bin/bash");

        Assert.Contains("'/bin/bash'", cmd);
        Assert.Contains("-c", cmd);
        Assert.Contains("'echo hello'", cmd);
    }

    [Fact]
    public void BuildWithSetup_IncludesSetupScript()
    {
        var builder = new BubblewrapBuilder();
        var cmd = builder.BuildWithSetup("export FOO=bar", "echo $FOO");

        Assert.Contains("export FOO=bar", cmd);
        Assert.Contains("echo $FOO", cmd);
    }

    [Fact]
    public void BuildWithSeccomp_IncludesSeccompHelper()
    {
        var builder = new BubblewrapBuilder();
        var cmd = builder.BuildWithSeccomp("# setup", "echo test", "/path/to/apply-seccomp");

        Assert.Contains("/path/to/apply-seccomp", cmd);
        Assert.Contains("exec", cmd);
    }

    [Fact]
    public void FluentInterface_AllowsChaining()
    {
        var existingPath = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var cmd = new BubblewrapBuilder()
            .WithReadOnlyRoot()
            .WithWritablePath(existingPath)
            .WithNetworkIsolation()
            .WithDevices()
            .Build("echo test");

        Assert.Contains("--ro-bind", cmd);
        Assert.Contains("--bind", cmd);
        Assert.Contains("--unshare-net", cmd);
        Assert.Contains("--dev", cmd);
    }

    [Fact]
    public void GetArguments_ReturnsCurrentArgs()
    {
        var builder = new BubblewrapBuilder();
        builder.WithReadOnlyRoot();
        builder.WithNetworkIsolation();

        var args = builder.GetArguments();

        Assert.Contains("--new-session", args);
        Assert.Contains("--die-with-parent", args);
        Assert.Contains("--ro-bind", args);
        Assert.Contains("--unshare-net", args);
    }

    private sealed class TempDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"hpd-bwrap-test-{Guid.NewGuid():N}");

        public TempDirectory()
        {
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }

    private static int LastIndexOfArg(IReadOnlyList<string> args, string value)
    {
        for (var i = args.Count - 1; i >= 0; i--)
        {
            if (args[i] == value)
                return i;
        }

        return -1;
    }
}
