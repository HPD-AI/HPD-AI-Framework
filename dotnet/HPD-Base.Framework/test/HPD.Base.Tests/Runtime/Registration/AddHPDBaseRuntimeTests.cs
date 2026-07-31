using HPD.Base;
using HPD.Events;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Tests.Registration;

public sealed class AddHPDBaseRuntimeTests
{
    [Fact]
    public void RegistersDefaultRuntimeServices()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHPDBaseRuntime();

        using var provider = services.BuildServiceProvider();

        Assert.NotNull(provider.GetRequiredService<IHPDBaseRuntime>());
        Assert.NotNull(provider.GetRequiredService<IBaseDescriptorRegistry>());
        Assert.NotNull(provider.GetRequiredService<IBaseDescriptorProvider>());
        Assert.NotNull(provider.GetRequiredService<IBaseSchemaProvider>());
        Assert.NotNull(provider.GetRequiredService<IBaseCapabilityProvider>());
        Assert.NotNull(provider.GetRequiredService<IBaseRecordRuntime>());
        Assert.NotNull(provider.GetRequiredService<IBaseHealthProvider>());
        Assert.NotNull(provider.GetRequiredService<IBaseDiagnosticProvider>());
        Assert.NotNull(provider.GetRequiredService<IBaseJsonOptionsProvider>());
        Assert.NotNull(provider.GetRequiredService<IBaseResultFactory>());
        Assert.IsType<HPDEventsBaseEventPublisher>(provider.GetRequiredService<IBaseEventPublisher>());
        Assert.NotNull(provider.GetRequiredService<IEventCoordinator>());
        Assert.NotNull(provider.GetRequiredService<IEventPublisher>());
    }

    [Fact]
    public void ExistingDefaultsRemainReplaceable()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        var replacement = new ReplacementRecordRuntime();
        services.AddSingleton<IBaseRecordRuntime>(replacement);

        services.AddHPDBaseRuntime();

        using var provider = services.BuildServiceProvider();

        Assert.Same(replacement, provider.GetRequiredService<IBaseRecordRuntime>());
    }
}
