using System.Collections.Immutable;
using HPD.Gateway.Abstractions;
using HPD.Gateway.Core;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Net.Http.Headers;

namespace HPD.Gateway.OutputCaching;

public sealed record GatewayOutputCacheProfile
{
    public required string Name { get; init; }
    public required int Version { get; init; }
    public TimeSpan Expiration { get; init; } = TimeSpan.FromMinutes(1);
    public ImmutableArray<string> QueryKeys { get; init; } = [];
    public ImmutableArray<string> HeaderNames { get; init; } = [];
}

public sealed class GatewayOutputCacheRegistryBuilder
{
    private readonly Dictionary<string, GatewayOutputCacheProfile> _profiles = new(StringComparer.Ordinal);

    public long MaximumBodyBytes { get; set; } = 1_048_576;
    public long StoreCapacityBytes { get; set; } = 16_777_216;

    public GatewayOutputCacheRegistryBuilder Add(GatewayOutputCacheProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        var normalized = ValidateAndNormalize(profile);
        if (!_profiles.TryAdd(normalized.Name, normalized))
            throw new ArgumentException("Output Cache profile names must be unique.", nameof(profile));
        return this;
    }

    internal GatewayOutputCacheRegistry Build()
    {
        if (MaximumBodyBytes is < 1_024 or > 67_108_864 ||
            StoreCapacityBytes < MaximumBodyBytes || StoreCapacityBytes > 1_073_741_824)
            throw new ArgumentOutOfRangeException(nameof(MaximumBodyBytes), "Output Cache body and store bounds are invalid.");
        if (_profiles.Count == 0) throw new InvalidOperationException("At least one Output Cache profile must be registered.");
        return new GatewayOutputCacheRegistry(
            _profiles.ToImmutableDictionary(StringComparer.Ordinal),
            MaximumBodyBytes,
            StoreCapacityBytes);
    }

    private static GatewayOutputCacheProfile ValidateAndNormalize(GatewayOutputCacheProfile profile)
    {
        if (!GatewayIdentifier.IsCanonical(profile.Name)) throw new ArgumentException("Profile name must be canonical.", nameof(profile));
        if (profile.Version <= 0 || profile.Expiration < TimeSpan.FromSeconds(1) || profile.Expiration > TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(profile));
        return profile with
        {
            QueryKeys = Normalize(profile.QueryKeys, header: false),
            HeaderNames = Normalize(profile.HeaderNames, header: true)
        };
    }

    private static ImmutableArray<string> Normalize(ImmutableArray<string> values, bool header)
    {
        if (values.IsDefault || values.Length > 16) throw new ArgumentException("Cache dimensions must be initialized and bounded.", nameof(values));
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 128 || !IsToken(value) || !names.Add(value) ||
                header && IsCredentialHeader(value))
                throw new ArgumentException("Cache dimensions must be bounded, token-valid, unique, and noncredential.", nameof(values));
        }
        return names.Select(static value => value.ToLowerInvariant()).Order(StringComparer.Ordinal).ToImmutableArray();
    }

    private static bool IsCredentialHeader(string value) =>
        value.Equals("Authorization", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Proxy-Authorization", StringComparison.OrdinalIgnoreCase) ||
        value.Equals("Cookie", StringComparison.OrdinalIgnoreCase);

    private static bool IsToken(string value)
    {
        foreach (var c in value)
            if (!(char.IsAsciiLetterOrDigit(c) || c is '!' or '#' or '$' or '%' or '&' or '\'' or '*' or '+' or '-' or '.' or '^' or '_' or '`' or '|' or '~'))
                return false;
        return value.Length > 0;
    }
}

internal sealed class GatewayOutputCacheRegistry(
    ImmutableDictionary<string, GatewayOutputCacheProfile> profiles,
    long maximumBodyBytes,
    long storeCapacityBytes) : IGatewayOutputCacheRuntimeCapabilityProvider
{
    internal ImmutableDictionary<string, GatewayOutputCacheProfile> Profiles { get; } = profiles;
    internal long MaximumBodyBytes { get; } = maximumBodyBytes;
    internal long StoreCapacityBytes { get; } = storeCapacityBytes;
    private ImmutableArray<OutputCacheCapability> CapabilityArray => Profiles.Values
        .OrderBy(static profile => profile.Name, StringComparer.Ordinal)
        .Select(profile => new OutputCacheCapability(
            profile.Name,
            profile.Version,
            true,
            "memory",
            OutputCacheStoreScope.ProcessLocal,
            profile.Expiration,
            MaximumBodyBytes,
            StoreCapacityBytes,
            profile.QueryKeys,
            profile.HeaderNames))
        .ToImmutableArray();

    internal ImmutableArray<OutputCacheCapability> Capabilities => CapabilityArray;

    ImmutableDictionary<string, OutputCacheCapability> IGatewayOutputCacheRuntimeCapabilityProvider.Capabilities =>
        CapabilityArray.ToImmutableDictionary(static capability => capability.Name, StringComparer.Ordinal);
}

public sealed class GatewayConservativeOutputCachePolicy : IOutputCachePolicy
{
    public GatewayConservativeOutputCachePolicy() { }

    public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        var request = context.HttpContext.Request;
        if (HttpMethods.IsConnect(request.Method) || request.Protocol.Equals(HttpProtocol.Http3, StringComparison.Ordinal) ||
            request.Headers.ContainsKey(HeaderNames.Range) ||
            request.Headers.ContainsKey(HeaderNames.Upgrade) || request.Headers.Connection.Contains("Upgrade", StringComparer.OrdinalIgnoreCase))
            Disable(context);
        else if (IsUnsupportedNegotiation(request))
            Disable(context);
        return ValueTask.CompletedTask;
    }

    public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken cancellationToken) => ValueTask.CompletedTask;

    public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken cancellationToken)
    {
        var response = context.HttpContext.Response;
        var contentType = response.ContentType;
        if (response.StatusCode != StatusCodes.Status200OK || response.Headers.ContainsKey(HeaderNames.ContentRange) ||
            response.Headers.ContainsKey(HeaderNames.Trailer) ||
            contentType?.StartsWith("text/event-stream", StringComparison.OrdinalIgnoreCase) == true ||
            contentType?.StartsWith("application/grpc", StringComparison.OrdinalIgnoreCase) == true)
            context.AllowCacheStorage = false;
        return ValueTask.CompletedTask;
    }

    private static void Disable(OutputCacheContext context)
    {
        context.EnableOutputCaching = false;
        context.AllowCacheLookup = false;
        context.AllowCacheStorage = false;
        context.AllowLocking = false;
    }

    private static bool IsUnsupportedNegotiation(HttpRequest request)
    {
        if (request.ContentType?.StartsWith("application/grpc", StringComparison.OrdinalIgnoreCase) == true)
            return true;
        foreach (var accept in request.Headers.Accept)
        {
            if (accept is not null &&
                (accept.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase) ||
                 accept.Contains("application/grpc", StringComparison.OrdinalIgnoreCase)))
                return true;
        }
        return false;
    }
}

internal sealed class HpdOutputCachePipelineMarker :
    IGatewayEndpointMappingParticipant,
    IGatewayApplicationPipelineParticipant
{
    internal int UseCount { get; set; }
    internal bool IsMapped { get; set; }
    bool IGatewayEndpointMappingParticipant.IsMapped => IsMapped;
    void IGatewayEndpointMappingParticipant.MarkMapped() => IsMapped = true;

    void IGatewayApplicationPipelineParticipant.Configure(IApplicationBuilder application)
    {
        UseCount++;
        if (UseCount > 1) throw new InvalidOperationException("HPD Output Cache middleware can be installed only once.");
        application.UseOutputCache();
    }
}

internal sealed class HpdOutputCacheStartupGuard(
    HpdOutputCachePipelineMarker marker,
    IEnumerable<IOutputCacheStore> stores) : IHostedService
{
    private readonly IOutputCacheStore[] _stores = stores.ToArray();

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (marker.UseCount != 1 || !marker.IsMapped) throw new InvalidOperationException("HPD Output Cache middleware and managed reverse-proxy endpoint must each be installed exactly once.");
        if (_stores.Length != 1 || _stores[0].GetType().Assembly != typeof(IOutputCacheStore).Assembly ||
            !_stores[0].GetType().Name.Equals("MemoryOutputCacheStore", StringComparison.Ordinal))
            throw new InvalidOperationException("Decision 0007 requires exactly one ASP.NET Core memory Output Cache store.");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

public static class GatewayOutputCacheExtensions
{
    public static IServiceCollection AddHpdGatewayOutputCaching(
        this IServiceCollection services,
        Action<GatewayOutputCacheRegistryBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configure);
        if (services.Any(static descriptor => descriptor.ServiceType == typeof(GatewayOutputCacheRegistry)))
            throw new InvalidOperationException("HPD Output Cache may be registered only once.");
        var builder = new GatewayOutputCacheRegistryBuilder();
        configure(builder);
        var registry = builder.Build();
        services.AddSingleton(registry);
        services.AddSingleton<IGatewayOutputCacheRuntimeCapabilityProvider>(registry);
        services.AddOutputCache(options =>
        {
            options.MaximumBodySize = registry.MaximumBodyBytes;
            options.SizeLimit = registry.StoreCapacityBytes;
            foreach (var profile in registry.Profiles.Values)
            {
                options.AddPolicy(profile.Name, policy =>
                {
                    policy.SetCacheKeyPrefix($"hpd:{profile.Name}:v{profile.Version}");
                    policy.SetVaryByHost(true);
                    policy.SetVaryByQuery(profile.QueryKeys.ToArray());
                    if (!profile.HeaderNames.IsEmpty) policy.SetVaryByHeader(profile.HeaderNames.ToArray());
                    policy.Expire(profile.Expiration);
                    policy.SetLocking(true);
                    policy.AddPolicy<GatewayConservativeOutputCachePolicy>();
                });
            }
        });
        services.AddSingleton<GatewayConservativeOutputCachePolicy>();
        services.AddSingleton<HpdOutputCachePipelineMarker>();
        services.AddSingleton<IGatewayEndpointMappingParticipant>(static provider => provider.GetRequiredService<HpdOutputCachePipelineMarker>());
        services.AddSingleton<IHostedService, HpdOutputCacheStartupGuard>();
        return services;
    }

    public static ImmutableArray<OutputCacheCapability> GetHpdGatewayOutputCacheCapabilities(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);
        return services.GetRequiredService<GatewayOutputCacheRegistry>().Capabilities;
    }

    public static IApplicationBuilder UseHpdGatewayOutputCaching(this IApplicationBuilder application)
    {
        ArgumentNullException.ThrowIfNull(application);
        var marker = application.ApplicationServices.GetRequiredService<HpdOutputCachePipelineMarker>();
        ((IGatewayApplicationPipelineParticipant)marker).Configure(application);
        return application;
    }
}
