using HPDAgent.Graph.Abstractions.Handlers;
using HPDAgent.Graph.Abstractions.Routing;
using HPDAgent.Graph.Core.Context;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Graph.Tests.Helpers;

/// <summary>
/// Creates a test service provider with common test handlers registered.
/// </summary>
public static class TestServiceProvider
{
    public static IServiceProvider Create(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();

        // Register test handlers as IGraphNodeHandler<GraphContext>
        services.AddTransient<IGraphNodeHandler<GraphContext>, SuccessHandler>();
        services.AddTransient<IGraphNodeHandler<GraphContext>, FailureHandler>();
        services.AddTransient<IGraphNodeHandler<GraphContext>, TransientFailureHandler>();
        services.AddTransient<IGraphNodeHandler<GraphContext>, SuspendingHandler>();
        services.AddTransient<IGraphNodeHandler<GraphContext>, EchoHandler>();
        services.AddTransient<IGraphNodeHandler<GraphContext>, CounterHandler>();
        services.AddTransient<IGraphNodeHandler<GraphContext>, ListProducerHandler>();
        services.AddTransient<IGraphNodeHandler<GraphContext>, ChannelReaderHandler>();

        // Register delay handler factory
        services.AddTransient<IGraphNodeHandler<GraphContext>>(sp => new DelayHandler(TimeSpan.FromMilliseconds(100)));

        // Allow custom configuration
        configure?.Invoke(services);

        return services.BuildServiceProvider();
    }

    public static IServiceProvider CreateWithHandler<THandler>(THandler instance)
        where THandler : class, IGraphNodeHandler<GraphContext>
    {
        var services = new ServiceCollection();
        services.AddSingleton<IGraphNodeHandler<GraphContext>>(instance);
        return services.BuildServiceProvider();
    }

    public static IServiceProvider CreateWithCustomHandler<THandler>(THandler instance)
        where THandler : class, IGraphNodeHandler<GraphContext>
    {
        return Create(services =>
        {
            services.AddSingleton<IGraphNodeHandler<GraphContext>>(instance);
        });
    }

    public static IServiceProvider CreateWithRouters(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();

        // Register all standard test handlers
        services.AddTransient<IGraphNodeHandler<GraphContext>, SuccessHandler>();
        services.AddTransient<IGraphNodeHandler<GraphContext>, FailureHandler>();
        services.AddTransient<IGraphNodeHandler<GraphContext>, ListProducerHandler>();

        // Register heterogeneous map test handlers
        services.AddTransient<IGraphNodeHandler<GraphContext>, MixedTypeListProducerHandler>();
        services.AddTransient<IGraphNodeHandler<GraphContext>, StringProcessorHandler>();
        services.AddTransient<IGraphNodeHandler<GraphContext>, IntProcessorHandler>();
        services.AddTransient<IGraphNodeHandler<GraphContext>, DefaultProcessorHandler>();
        services.AddTransient<IGraphNodeHandler<GraphContext>, DocumentListProducerHandler>();
        services.AddTransient<IGraphNodeHandler<GraphContext>, PdfProcessorHandler>();
        services.AddTransient<IGraphNodeHandler<GraphContext>, ImageProcessorHandler>();

        // Register test routers as Singleton (same as proposed in spec)
        services.AddSingleton<IMapRouter, TypeBasedRouter>();
        services.AddSingleton<IMapRouter, PropertyBasedRouter>();
        services.AddSingleton<IMapRouter, PriorityRouter>();

        // Allow custom configuration
        configure?.Invoke(services);

        return services.BuildServiceProvider();
    }
}

/// <summary>
/// Extension methods for building test service providers.
/// </summary>
public static class TestServiceProviderExtensions
{
    public static TestServiceBuilder AddHandler<THandler>(this IServiceProvider provider, THandler instance)
        where THandler : class, IGraphNodeHandler<GraphContext>
    {
        var builder = new TestServiceBuilder();
        builder.Services.AddSingleton<IGraphNodeHandler<GraphContext>>(instance);
        return builder;
    }
}

/// <summary>
/// Builder for creating test service providers with multiple handlers.
/// </summary>
public class TestServiceBuilder
{
    public ServiceCollection Services { get; } = new ServiceCollection();

    public TestServiceBuilder AddHandler<THandler>(THandler instance)
        where THandler : class, IGraphNodeHandler<GraphContext>
    {
        Services.AddSingleton<IGraphNodeHandler<GraphContext>>(instance);
        return this;
    }

    public IServiceProvider Build()
    {
        return Services.BuildServiceProvider();
    }

    public static implicit operator ServiceProvider(TestServiceBuilder builder)
    {
        return builder.Services.BuildServiceProvider();
    }
}
