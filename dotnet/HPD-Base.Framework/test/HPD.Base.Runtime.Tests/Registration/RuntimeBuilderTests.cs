using HPD.Base.Runtime.Builder;
using HPD.Base.Runtime.Configuration;
using HPD.Base.Runtime.DependencyInjection;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Runtime.Tests.Registration;

public sealed class RuntimeBuilderTests
{
    [Fact]
    public void AddHPDBaseRuntimeReturnsBuilderWithServicesAndConfiguredOptions()
    {
        var services = new ServiceCollection();

        var builder = services.AddHPDBaseRuntime(options =>
        {
            options.ManifestVersion = "test-manifest";
        });

        Assert.Same(services, builder.Services);
        Assert.IsAssignableFrom<IHPDBaseRuntimeBuilder>(builder);
        Assert.Equal("test-manifest", builder.Options.ManifestVersion);
    }

    [Fact]
    public void DefaultOptionsAreStrict()
    {
        var options = HPDBaseRuntimeOptions.CreateDefault();

        Assert.True(options.FailFastOnDescriptorValidation);
        Assert.True(options.Events.Enabled);
        Assert.True(options.Redaction.RedactPublicErrors);
        Assert.True(options.Limits.MaxPageSize > 0);
    }

    [Fact]
    public void UseFailClosedPolicyDisablesAbstainAsAllow()
    {
        var services = new ServiceCollection();

        var builder = services.AddHPDBaseRuntime()
            .UseDevelopmentPolicyAbstainAsAllow()
            .UseFailClosedPolicy();

        var evaluator = typeof(HPDBaseRuntimeOptions)
            .GetProperty("AllowPolicyAbstainAsAllowForDevelopment", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.False((bool)evaluator!.GetValue(builder.Options)!);
    }

    [Fact]
    public void UseDevelopmentPolicyAbstainAsAllowEnablesExplicitDevelopmentEscapeHatch()
    {
        var services = new ServiceCollection();

        var builder = services.AddHPDBaseRuntime()
            .UseDevelopmentPolicyAbstainAsAllow();

        var evaluator = typeof(HPDBaseRuntimeOptions)
            .GetProperty("AllowPolicyAbstainAsAllowForDevelopment", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        Assert.True((bool)evaluator!.GetValue(builder.Options)!);
    }
}
