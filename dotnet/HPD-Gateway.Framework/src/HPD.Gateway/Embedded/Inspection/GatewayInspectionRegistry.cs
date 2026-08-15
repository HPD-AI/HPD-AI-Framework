using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Gateway;

public sealed class GatewayInspectionRegistryBuilder
{
    private readonly Dictionary<string, IGatewayRequestInspector> _inspectors = new(StringComparer.Ordinal);

    public GatewayInspectionRegistryBuilder Add(string name, IGatewayRequestInspector inspector)
    {
        if (!GatewayIdentifier.IsCanonical(name)) throw new ArgumentException("Inspector name must be a canonical identifier.", nameof(name));
        ArgumentNullException.ThrowIfNull(inspector);
        if (!_inspectors.TryAdd(name, inspector)) throw new ArgumentException("Inspector names must be unique.", nameof(name));
        return this;
    }

    internal GatewayInspectionRegistry Build() => new(_inspectors.ToImmutableDictionary(StringComparer.Ordinal));
}

internal sealed class GatewayInspectionRegistry(ImmutableDictionary<string, IGatewayRequestInspector> inspectors)
{
    private readonly ImmutableDictionary<string, IGatewayRequestInspector> _inspectors = inspectors;
    internal ImmutableArray<string> Names { get; } = inspectors.Keys.Order(StringComparer.Ordinal).ToImmutableArray();
    internal bool TryGet(string name, out IGatewayRequestInspector inspector) => _inspectors.TryGetValue(name, out inspector!);
}

internal static class GatewayInspectionServiceCollectionExtensions
{
    public static IServiceCollection AddHpdGatewayInspection(this IServiceCollection services, Action<GatewayInspectionRegistryBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        var builder = new GatewayInspectionRegistryBuilder();
        configure(builder);
        services.AddSingleton(builder.Build());
        services.AddSingleton<GatewayInspectionExecutor>();
        return services;
    }
}
