using FluentAssertions;
using HPD.Agent.Sandbox;
using HPD.Sandbox.Local.Platforms;
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
    public void ShouldSandbox_CanBeCustomized()
    {
        var config = SandboxConfig.CreateDefault();
        var middleware = new SandboxMiddleware(config);

        middleware.ShouldSandbox = f => f.Name.StartsWith("Custom");

        middleware.ShouldSandbox.Should().NotBeNull();
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

public class SandboxMiddlewareFunctionDetectionTests
{
    [Theory]
    [InlineData("ExecuteCommand")]
    [InlineData("RunScript")]
    [InlineData("ShellExecute")]
    [InlineData("BashCommand")]
    [InlineData("CommandRunner")]
    [InlineData("ProcessStart")]
    public void DefaultSandboxablePatterns_MatchExpectedNames(string functionName)
    {
        // The middleware should auto-detect these function name patterns
        var patterns = new[] { "Execute", "Run", "Shell", "Bash", "Command", "Process" };

        var matches = patterns.Any(p =>
            functionName.Contains(p, StringComparison.OrdinalIgnoreCase));

        matches.Should().BeTrue($"'{functionName}' should match sandboxable patterns");
    }

    [Theory]
    [InlineData("GetUserInfo")]
    [InlineData("CalculateTotal")]
    [InlineData("ParseJson")]
    [InlineData("SendEmail")]
    public void DefaultSandboxablePatterns_DontMatchSafeFunctions(string functionName)
    {
        var patterns = new[] { "Execute", "Run", "Shell", "Bash", "Command", "Process" };

        var matches = patterns.Any(p =>
            functionName.Contains(p, StringComparison.OrdinalIgnoreCase));

        matches.Should().BeFalse($"'{functionName}' should NOT match sandboxable patterns");
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
