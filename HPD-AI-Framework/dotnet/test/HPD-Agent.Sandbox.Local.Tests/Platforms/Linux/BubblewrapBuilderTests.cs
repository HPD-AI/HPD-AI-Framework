using HPD.Sandbox.Local.Platforms.Linux;
using Xunit;

namespace HPD.Sandbox.Local.Tests.Platforms.Linux;

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
        Assert.Contains("--proc", cmd);
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
}
