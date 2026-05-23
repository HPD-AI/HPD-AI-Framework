using HPD.Execution.Local.Platforms.Linux;
using Xunit;

namespace HPD.Execution.Local.Tests.Platforms.Linux;

public class UnixSocketBridgeTests
{
    [Fact]
    public void BuildHostSocatArguments_AddsKeepaliveAndUsesSeparateArguments()
    {
        var args = UnixSocketBridge.BuildHostSocatArguments("/tmp/hpd test.sock", 3128);

        Assert.Equal(2, args.Length);
        Assert.Equal("UNIX-LISTEN:/tmp/hpd test.sock,fork,reuseaddr", args[0]);
        Assert.Equal("TCP:localhost:3128,keepalive", args[1]);
    }

    [Fact]
    public void BuildSandboxSocatCommand_QuotesSocketPathAndAddsKeepalive()
    {
        var command = UnixSocketBridge.BuildSandboxSocatCommand(3128, "/tmp/hpd 'quoted'.sock");

        Assert.Contains("TCP-LISTEN:3128,fork,reuseaddr,keepalive", command);
        Assert.Contains("UNIX-CONNECT:'/tmp/hpd '\\''quoted'\\''.sock'", command);
        Assert.EndsWith(" &", command);
    }

    [Fact]
    public async Task GetProxyEnvironmentVariables_IncludesPrivateNetworkNoProxyEntries()
    {
        await using var bridge = new UnixSocketBridge();

        var env = bridge.GetProxyEnvironmentVariables();

        Assert.Contains("169.254.0.0/16", env["NO_PROXY"]);
        Assert.Contains("10.0.0.0/8", env["NO_PROXY"]);
        Assert.Contains("172.16.0.0/12", env["NO_PROXY"]);
        Assert.Contains("192.168.0.0/16", env["NO_PROXY"]);
        Assert.Equal(env["NO_PROXY"], env["no_proxy"]);
    }

    [Fact]
    public async Task InitializeAsync_WhenSocatExitsBeforeSocket_CleansSocketPaths()
    {
        if (!File.Exists("/bin/false"))
            return;

        await using var bridge = new UnixSocketBridge(socatPath: "/bin/false");

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            bridge.InitializeAsync(3128, 1080, CancellationToken.None));

        if (bridge.HttpSocketPath is not null)
            Assert.False(File.Exists(bridge.HttpSocketPath));
        if (bridge.SocksSocketPath is not null)
            Assert.False(File.Exists(bridge.SocksSocketPath));
    }
}
