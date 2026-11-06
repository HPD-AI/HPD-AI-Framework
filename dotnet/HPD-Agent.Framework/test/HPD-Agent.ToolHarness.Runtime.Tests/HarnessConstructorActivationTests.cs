using HPD.Agent.Tests.Infrastructure;
using HPD.Agent.Middleware;
using System.Text.Json;
using HPD.Agent.Tests.Middleware.V2;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Agent.Tests;

public sealed class HarnessConstructorActivationTests
{
    [Fact]
    public async Task GeneratedConstructor_UsesLocalOverride_WithoutResolvingPreconstructedDiOwner()
    {
        var application = new ActivationProbe("application");
        var local = new ActivationProbe("local");
        await using var services = new ServiceCollection()
            .AddSingleton(application)
            .AddSingleton<ConstructorActivationHarness>(_ => throw new Exception("DI owner must not be selected"))
            .BuildServiceProvider();
        await using (var agent = await Builder().WithServiceProvider(services)
            .WithToolHarness<ConstructorActivationHarness>(options => options.UseDependency(local))
            .BuildAsync())
        {
            var tool = Assert.Single(agent.DefaultOptions!.Tools!.OfType<AIFunction>(), function => function.Name == "Probe");
            Assert.Equal("local", (await InvokeAsync(tool))?.ToString());
            Assert.Equal(0, local.HarnessDisposals);
        }
        Assert.Equal(1, local.HarnessDisposals);
        Assert.Equal(0, local.ServiceDisposals);
        Assert.Equal(0, application.HarnessDisposals);
    }

    [Fact]
    public async Task FailedBuild_DisposesConstructedOwner_ButNotBorrowedDependencies()
    {
        var probe = new ActivationProbe("rollback");
        var builder = Builder()
            .WithToolHarness<ConstructorActivationHarness>(options => options.UseDependency(probe))
            .WithToolHarness<MissingDependencyHarness>();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => builder.BuildAsync());
        Assert.Contains(nameof(IMissingActivationDependency), error.ToString());
        Assert.Equal(1, probe.HarnessDisposals);
        Assert.Equal(0, probe.ServiceDisposals);
    }

    [Fact]
    public async Task ExplicitInstance_IsBorrowed_AndUsesSameProjection()
    {
        var probe = new ActivationProbe("borrowed");
        var instance = new ConstructorActivationHarness(probe);
        await using (var agent = await Builder().WithToolHarness(instance).BuildAsync())
            Assert.Contains(agent.DefaultOptions!.Tools!, function => function.Name == "Probe");
        Assert.Equal(0, probe.HarnessDisposals);
        instance.Dispose();
        Assert.Equal(1, probe.HarnessDisposals);
    }

    [Fact]
    public async Task ConfigurableExplicitInstance_WithoutFrameworkConfiguration_UsesSuppliedState()
    {
        var instance = new ConfigurableExplicitHarness(
            new FixedHarnessConfig<ConfigurableExplicitSettings>(new() { Value = 7 }));

        await using var agent = await Builder().WithToolHarness(instance).BuildAsync();

        var tool = Assert.Single(
            agent.DefaultOptions!.Tools!.OfType<AIFunction>(),
            function => function.Name == "ConfiguredProbe");
        Assert.Equal("7", (await InvokeAsync(tool))?.ToString());

        var run = new AgentRunConfig();
        run.Harnesses.For<ConfigurableExplicitHarness>().Override(
            new ConfigurableExplicitSettingsOverrides { Value = 9 });
        var error = Assert.Throws<AgentRunConfigurationException>(() => agent.CaptureRunConfig(run));
        Assert.Equal("HarnessRunOverrideNotSupported", error.Code);
    }

    [Fact]
    public async Task ConfigurableExplicitInstance_RejectsRequestedConstructionConfiguration()
    {
        var instance = new ConfigurableExplicitHarness(
            new FixedHarnessConfig<ConfigurableExplicitSettings>(new() { Value = 7 }));
        var config = new AgentConfig
        {
            ToolHarnesses =
            [
                new ToolHarnessReference
                {
                    Name = nameof(ConfigurableExplicitHarness),
                    Config = JsonDocument.Parse("""{"value":9}""").RootElement.Clone()
                }
            ]
        };

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Builder(config).WithToolHarness(instance).BuildAsync());

        var rejection = Assert.IsType<InvalidOperationException>(error.InnerException);
        Assert.Contains("cannot replace constructor configuration or dependencies", rejection.Message);
    }

    [Fact]
    public async Task DefaultFactory_UsesLocalResolver_AndIsDisposedAfterOwner()
    {
        var probe = new ActivationProbe("default");
        await using (var agent = await Builder()
            .WithToolHarness<DefaultActivationHarness>(options => options.UseDependency(probe)).BuildAsync())
        {
            var tool = Assert.Single(agent.DefaultOptions!.Tools!.OfType<AIFunction>(), function => function.Name == "DefaultProbe");
            Assert.Equal("default", (await InvokeAsync(tool))?.ToString());
        }
        Assert.Equal(new[] { "owner", "dependency" }, probe.Order);
        Assert.Equal(0, probe.ServiceDisposals);
    }

    [Fact]
    public async Task ApplicationDependency_WinsOverGeneratedDefault()
    {
        var probe = new ActivationProbe("probe");
        await using var services = new ServiceCollection()
            .AddSingleton<IDefaultActivationService>(
                new DefaultActivationService(new ActivationProbe("application")))
            .BuildServiceProvider();
        await using var agent = await Builder()
            .WithServiceProvider(services)
            .WithToolHarness<DefaultActivationHarness>(options => options.UseDependency(probe))
            .BuildAsync();

        var tool = Assert.Single(
            agent.DefaultOptions!.Tools!.OfType<AIFunction>(),
            function => function.Name == "DefaultProbe");
        Assert.Equal("application", (await InvokeAsync(tool))?.ToString());
    }

    [Fact]
    public async Task ScopedConstructorDependency_FailsUnderHostScopeValidation()
    {
        await using var services = new ServiceCollection()
            .AddScoped<ScopedActivationService>()
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Builder()
                .WithServiceProvider(services)
                .WithToolHarness<ScopedConstructorHarness>()
                .BuildAsync());

        Assert.Contains("Cannot resolve scoped service", error.ToString());
    }

    private static ValueTask<object?> InvokeAsync(AIFunction function)
    {
        var request = MiddlewareTestHelpers.CreateFunctionRequest(function);
        var context = new FunctionExecutionContext(MiddlewareTestHelpers.CreateBeforeIterationContext(), request);
        var arguments = new AIFunctionArguments();
        using var document = JsonDocument.Parse("{}");
        arguments.SetJson(document.RootElement.Clone());
        return Assert.IsType<HPDAIFunctionFactory.HPDAIFunction>(function)
            .InvokeAsync(arguments, context);
    }

    private static AgentBuilder Builder(AgentConfig? config = null) => new AgentBuilder(config ?? new AgentConfig())
        .WithChatClient(new MiddlewareTestHelpers.TestChatClient())
        .WithEventComposition(TestEventComposition.Current);
}

public sealed class ActivationProbe(string value) : IDisposable
{
    public string Value { get; } = value;
    public List<string> Order { get; } = [];
    public int HarnessDisposals { get; set; }
    public int ServiceDisposals { get; private set; }
    public void Dispose() => ServiceDisposals++;
}

public sealed class ConstructorActivationHarness(ActivationProbe probe) : IDisposable
{
    [AIFunction]
    public string Probe() => probe.Value;
    public void Dispose() => probe.HarnessDisposals++;
}

public interface IMissingActivationDependency;

public sealed class MissingDependencyHarness(IMissingActivationDependency dependency)
{
    [AIFunction]
    public string Missing() => dependency.ToString()!;
}

public interface IDefaultActivationService { string Value { get; } }

public sealed class DefaultActivationService(ActivationProbe probe) : IDefaultActivationService, IDisposable
{
    public string Value => probe.Value;
    public void Dispose() => probe.Order.Add("dependency");
}

[ToolHarnessDependency(typeof(IDefaultActivationService), typeof(DefaultActivationService))]
public sealed class DefaultActivationHarness(IDefaultActivationService dependency, ActivationProbe probe) : IDisposable
{
    [AIFunction]
    public string DefaultProbe() => dependency.Value;
    public void Dispose() => probe.Order.Add("owner");
}

public sealed record ConfigurableExplicitSettings
{
    [HarnessRunSetting]
    public int Value { get; init; } = 1;
}

public sealed class ConfigurableExplicitHarness(IHarnessConfig<ConfigurableExplicitSettings> configuration)
{
    [AIFunction]
    public string ConfiguredProbe() =>
        configuration.For(new NoopHarnessConfigurationContext()).Value
            .ToString(System.Globalization.CultureInfo.InvariantCulture);
}

public sealed class ScopedActivationService;

public sealed class ScopedConstructorHarness(ScopedActivationService dependency)
{
    [AIFunction]
    public string Probe() => dependency.ToString()!;
}

internal sealed class FixedHarnessConfig<T>(T value) : IHarnessConfig<T> where T : class
{
    public T AgentDefaults => value;
    public T For(IHarnessConfigurationContext context) => value;
}

internal sealed class NoopHarnessConfigurationContext : IHarnessConfigurationContext
{
    public HarnessConfigurationBinding? HarnessConfiguration => null;
}
