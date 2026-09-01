using HPD.Agent.Providers;

namespace HPD.Agent.Tests.Core;

public sealed class ExplicitSecretBuilderTests
{
    [Fact]
    public async Task RegisterExplicitApiKey_ReturnsOpaqueRuntimeReference()
    {
        var builder = new AgentBuilder();
        var authentication = builder.RegisterExplicitApiKey("explicit".AsSpan());

        var agent = await builder.BuildAsync();

        Assert.StartsWith("runtime-secret:", authentication.RuntimeRegistrationName, StringComparison.Ordinal);
        Assert.DoesNotContain("explicit", authentication.RuntimeRegistrationName, StringComparison.Ordinal);
        await agent.DisposeAsync();
    }

    [Fact]
    public async Task RegisterExplicitApiKey_AfterBuild_IsRejected()
    {
        var builder = new AgentBuilder();
        var agent = await builder.BuildAsync();

        Assert.Throws<InvalidOperationException>(() =>
            builder.RegisterExplicitApiKey("late".AsSpan()));
        await agent.DisposeAsync();
    }

    [Fact]
    public async Task RuntimeRegistry_CopiesAndClearsOwnedSecret()
    {
        var registry = new ProviderRuntimeSecretRegistry();
        var source = "secret".ToCharArray();
        var name = registry.Register(source);
        source.AsSpan().Clear();

        await using var lease = registry.Acquire(name);
        Assert.Equal("secret", lease.Value.ToString());

        await registry.DisposeAsync();
        Assert.Throws<ObjectDisposedException>(() => registry.Acquire(name));
    }
}
