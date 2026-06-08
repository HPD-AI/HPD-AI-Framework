namespace HPD.Agent.Sandbox.AppleVirtualization.Tests;

using FluentAssertions;
using HPD.Agent;
using HPD.Environment.AppleVirtualization;
using HPD.Environment.Runtime;
using Xunit;

public sealed class AppleVirtualizationSandboxDxTests
{
    [Fact]
    public async Task Expected_builder_dx_builds_agent_with_apple_virtualization_sandbox()
    {
        Agent agent = await new AgentBuilder()
            .WithDeferredProvider()
            .WithAppleVirtualizationSandbox(FakeAppleVirtualizationOptions())
            .BuildAsync();

        agent.Should().NotBeNull();
    }

    [Fact]
    public async Task Expected_middleware_dx_initializes_apple_virtualization_execution_runtime()
    {
        await using var middleware = new AppleVirtualizationSandboxMiddleware(FakeAppleVirtualizationOptions());

        await middleware.InitializeAsync();

        middleware.IsInitialized.Should().BeTrue();
        middleware.Registry.Should().NotBeNull();
        middleware.Runtime.Should().BeOfType<InMemoryEnvironmentRuntime>();
        middleware.Registry!.ProcessProviders.Should().ContainSingle();
        middleware.Registry.RuntimeHostProviders.Should().ContainSingle();
        middleware.Registry.ExecutionUnitProviders.Should().ContainSingle();
    }

    private static AppleVirtualizationProviderOptions FakeAppleVirtualizationOptions() =>
        new()
        {
            HelperTransportMode = AppleVirtualizationHelperTransportMode.InMemoryFake,
            FeatureGates = new AppleVirtualizationProviderFeatureGates
            {
                EnableInMemoryFakeHelper = true,
            },
        };
}
