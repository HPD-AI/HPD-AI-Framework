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
            options.AllowPolicyAbstainAsAllow = true;
        });

        Assert.Same(services, builder.Services);
        Assert.IsAssignableFrom<IHPDBaseRuntimeBuilder>(builder);
        Assert.Equal("test-manifest", builder.Options.ManifestVersion);
        Assert.True(builder.Options.AllowPolicyAbstainAsAllow);
    }

    [Fact]
    public void DefaultOptionsAreStrict()
    {
        var options = HPDBaseRuntimeOptions.CreateDefault();

        Assert.True(options.FailFastOnDescriptorValidation);
        Assert.False(options.AllowPolicyAbstainAsAllow);
        Assert.True(options.Events.Enabled);
        Assert.True(options.Redaction.RedactPublicErrors);
        Assert.True(options.Limits.MaxPageSize > 0);
    }
}
