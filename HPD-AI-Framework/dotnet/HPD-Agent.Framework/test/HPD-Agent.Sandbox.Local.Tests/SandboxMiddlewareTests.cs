using FluentAssertions;
using HPD.Agent;
using HPD.Agent.Sandbox;
using HPD.Sandbox.Local.Platforms;
using Microsoft.Extensions.AI;
using Xunit;

namespace HPD.Sandbox.Local.Tests;

public class SandboxMiddlewareTests
{
    [Fact]
    public void Constructor_ThrowsOnNullConfig()
    {
        var act = () => new SandboxMiddleware(null!);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Constructor_ValidatesConfig()
    {
        var invalidConfig = new SandboxConfig { AllowWrite = [] };

        var act = () => new SandboxMiddleware(invalidConfig);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Constructor_AcceptsValidConfig()
    {
        var config = SandboxConfig.CreateDefault();

        var middleware = new SandboxMiddleware(config);

        middleware.Configuration.Should().Be(config);
        middleware.IsInitialized.Should().BeFalse();
    }

    [Fact]
    public void Configuration_ReturnsProvidedConfig()
    {
        var config = SandboxConfig.CreateDefault() with
        {
            NetworkMode = SandboxNetworkMode.Filtered,
            AllowedDomains = ["api.example.com"]
        };

        var middleware = new SandboxMiddleware(config);

        middleware.Configuration.AllowedDomains.Should().Contain("api.example.com");
    }

    [Fact]
    public void Platform_ReturnsCurrentPlatform()
    {
        var config = SandboxConfig.CreateDefault();
        var middleware = new SandboxMiddleware(config);

        middleware.Platform.Should().Be(PlatformDetector.Current);
    }

    [Fact]
    public void IsInitialized_DefaultsFalse()
    {
        var config = SandboxConfig.CreateDefault();
        var middleware = new SandboxMiddleware(config);

        middleware.IsInitialized.Should().BeFalse();
    }

    [Fact]
    public async Task DisposeAsync_CanBeCalledMultipleTimes()
    {
        var config = SandboxConfig.CreateDefault();
        var middleware = new SandboxMiddleware(config);

        await middleware.DisposeAsync();
        var act = async () => await middleware.DisposeAsync();

        await act.Should().NotThrowAsync();
    }
}

public class SandboxMiddlewareConfigTests
{
    [Fact]
    public void SandboxableFunctions_CanBeConfigured()
    {
        var config = SandboxConfig.CreateDefault() with
        {
            SandboxableFunctions = ["MyCustomTool", "AnotherTool*"]
        };

        config.SandboxableFunctions.Should().Contain("MyCustomTool");
        config.SandboxableFunctions.Should().Contain("AnotherTool*");
    }

    [Fact]
    public void ExcludedFunctions_CanBeConfigured()
    {
        var config = SandboxConfig.CreateDefault() with
        {
            ExcludedFunctions = ["SafeExecute", "Trusted*"]
        };

        config.ExcludedFunctions.Should().Contain("SafeExecute");
        config.ExcludedFunctions.Should().Contain("Trusted*");
    }

    [Fact]
    public void OnViolation_BlockAndEmit_IsAvailable()
    {
        var config = SandboxConfig.CreateDefault() with
        {
            OnViolation = SandboxViolationBehavior.BlockAndEmit
        };

        config.OnViolation.Should().Be(SandboxViolationBehavior.BlockAndEmit);
    }

    [Fact]
    public void OnInitializationFailure_WarnOption_IsAvailable()
    {
        var config = SandboxConfig.CreateDefault() with
        {
            OnInitializationFailure = SandboxFailureBehavior.Warn
        };

        config.OnInitializationFailure.Should().Be(SandboxFailureBehavior.Warn);
    }

    [Fact]
    public void OnInitializationFailure_IgnoreOption_IsAvailable()
    {
        var config = SandboxConfig.CreateDefault() with
        {
            OnInitializationFailure = SandboxFailureBehavior.Ignore
        };

        config.OnInitializationFailure.Should().Be(SandboxFailureBehavior.Ignore);
    }
}

public class AgentBuilderSandboxExtensionsTests
{
    [Fact]
    public void WithSandbox_AddsSandboxMiddlewareWithDefaultConfig()
    {
        var builder = new AgentBuilder();

        builder.WithSandbox();

        var middleware = builder.Middlewares.OfType<SandboxMiddleware>().Should().ContainSingle().Subject;
        middleware.Configuration.Should().BeEquivalentTo(SandboxConfig.CreateDefault());
    }

    [Fact]
    public void WithSandbox_Config_AddsSandboxMiddlewareWithProvidedConfig()
    {
        var config = SandboxConfig.CreateDefault() with
        {
            NetworkMode = SandboxNetworkMode.Filtered,
            AllowedDomains = ["api.github.com"]
        };
        var builder = new AgentBuilder();

        builder.WithSandbox(config);

        var middleware = builder.Middlewares.OfType<SandboxMiddleware>().Should().ContainSingle().Subject;
        middleware.Configuration.Should().Be(config);
    }

    [Fact]
    public void WithSandbox_ReplacesExistingSandboxMiddleware()
    {
        var first = SandboxConfig.CreateDefault();
        var second = SandboxConfig.CreateDefault() with
        {
            DenyRead = ["~/.ssh", "~/.aws", "~/.gnupg", "~/.config"]
        };
        var builder = new AgentBuilder();

        builder.WithSandbox(first);
        builder.WithSandbox(second);

        var middleware = builder.Middlewares.OfType<SandboxMiddleware>().Should().ContainSingle().Subject;
        middleware.Configuration.Should().Be(second);
    }

    [Fact]
    public void WithSandbox_Configure_DerivesFromDefaultConfig()
    {
        var builder = new AgentBuilder();

        builder.WithSandbox(config => config with
        {
            NetworkMode = SandboxNetworkMode.Filtered,
            AllowedDomains = ["registry.npmjs.org"]
        });

        var middleware = builder.Middlewares.OfType<SandboxMiddleware>().Should().ContainSingle().Subject;
        middleware.Configuration.NetworkMode.Should().Be(SandboxNetworkMode.Filtered);
        middleware.Configuration.AllowedDomains.Should().ContainSingle("registry.npmjs.org");
        middleware.Configuration.DenyRead.Should().BeEquivalentTo(SandboxConfig.CreateDefault().DenyRead);
    }
}

public class SandboxMiddlewareFunctionConfigTests
{
    [Fact]
    public void TryGetFunctionSandboxOverride_ReturnsNullWhenFunctionIsNotSandboxable()
    {
        var function = AIFunctionFactory.Create(() => "ok", new AIFunctionFactoryOptions
        {
            Name = "ReadData"
        });

        var config = SandboxMiddleware.TryGetFunctionSandboxOverride(function);

        config.Should().BeNull();
    }

    [Fact]
    public void TryGetFunctionSandboxOverride_ReturnsSparseOverrideWhenOnlyMarkerIsPresent()
    {
        var function = AIFunctionFactory.Create(() => "ok", new AIFunctionFactoryOptions
        {
            Name = "RunCommand",
            AdditionalProperties = new Dictionary<string, object>
            {
                ["IsSandboxable"] = true
            }
        });

        var config = SandboxMiddleware.TryGetFunctionSandboxOverride(function);

        config.Should().NotBeNull();
        config!.NetworkMode.Should().BeNull();
        config.AllowWrite.Should().BeNull();
        config.DenyRead.Should().BeNull();
        config.AllowPty.Should().BeNull();
    }

    [Fact]
    public void TryGetFunctionSandboxOverride_MapsGeneratedMetadata()
    {
        var function = AIFunctionFactory.Create(() => "ok", new AIFunctionFactoryOptions
        {
            Name = "RunCommand",
            AdditionalProperties = new Dictionary<string, object>
            {
                ["IsSandboxable"] = true,
                ["SandboxNetworkMode"] = "Filtered",
                ["SandboxAllowedDomains"] = new[] { "api.github.com", "registry.npmjs.org" },
                ["SandboxDeniedDomains"] = new[] { "evil.github.com" },
                ["SandboxAllowWrite"] = new[] { "./workspace", "/tmp" },
                ["SandboxDenyRead"] = new[] { "~/.ssh", "~/.aws" },
                ["SandboxAllowRead"] = new[] { "./workspace/public" },
                ["SandboxDenyWrite"] = new[] { ".git/hooks", ".npmrc" },
                ["SandboxAllowUnixSockets"] = new[] { "/var/run/docker.sock" },
                ["SandboxAllowMachLookup"] = new[] { "com.example.*" },
                ["SandboxAllowPty"] = true,
                ["SandboxAllowLocalBinding"] = true,
                ["SandboxAllowAllUnixSockets"] = true,
                ["SandboxAllowMacOSTrustdLookup"] = true,
                ["SandboxMandatoryDenySearchDepth"] = 5
            }
        });

        var config = SandboxMiddleware.TryGetFunctionSandboxOverride(function);

        config.Should().NotBeNull();
        config!.NetworkMode.Should().Be(SandboxNetworkMode.Filtered);
        config.AllowedDomains.Should().BeEquivalentTo(["api.github.com", "registry.npmjs.org"]);
        config.DeniedDomains.Should().BeEquivalentTo(["evil.github.com"]);
        config.AllowWrite.Should().BeEquivalentTo(["./workspace", "/tmp"]);
        config.DenyRead.Should().BeEquivalentTo(["~/.ssh", "~/.aws"]);
        config.AllowRead.Should().BeEquivalentTo(["./workspace/public"]);
        config.DenyWrite.Should().BeEquivalentTo([".git/hooks", ".npmrc"]);
        config.AllowUnixSockets.Should().BeEquivalentTo(["/var/run/docker.sock"]);
        config.AllowMachLookup.Should().BeEquivalentTo(["com.example.*"]);
        config.AllowPty.Should().BeTrue();
        config.AllowLocalBinding.Should().BeTrue();
        config.AllowAllUnixSockets.Should().BeTrue();
        config.AllowMacOSTrustdLookup.Should().BeTrue();
        config.MandatoryDenySearchDepth.Should().Be(5);
    }
}
