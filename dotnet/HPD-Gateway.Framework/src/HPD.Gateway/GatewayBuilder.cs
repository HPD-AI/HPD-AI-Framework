using System.Collections.Immutable;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http.Timeouts;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Gateway;

public sealed class GatewayBuilder
{
    private bool _sealed;
    private GatewayDeclarationFamilies _installedFamilies;
    private GatewayNodeActivationRequest? _initialCandidate;
    private ImmutableArray<string> _requestInspectors = [];
    private ImmutableArray<UpstreamResilienceCapability> _resilienceProfiles = [];
    private ImmutableArray<OutputCacheCapability> _outputCacheProfiles = [];
    private ImmutableArray<DiscoveryProfileCapability> _discoveryProfiles = [];
    private ImmutableArray<string> _protectedCredentialHeaders = [];
    private readonly HashSet<string> _authorizationPolicies = new(StringComparer.Ordinal);
    private readonly HashSet<string> _corsPolicies = new(StringComparer.Ordinal);
    private GatewayTrafficAdmissionRegistry? _trafficAdmission;
    private readonly HashSet<string> _requestTimeoutPolicies = new(StringComparer.Ordinal);
    private bool _allowInspectionFileSpill;

    internal GatewayBuilder(IServiceCollection services) => Services = services;

    internal IServiceCollection Services { get; }

    public GatewayBuilder EnableCoreDeclarations()
    {
        ThrowIfSealed();
        _installedFamilies |= GatewayDeclarationFamilies.RequestTimeout |
            GatewayDeclarationFamilies.RequestTransforms |
            GatewayDeclarationFamilies.ResponseTransforms |
            GatewayDeclarationFamilies.CredentialDisposition;
        return this;
    }

    public GatewayBuilder UseInitialCandidate(GatewayNodeActivationRequest request)
    {
        ThrowIfSealed();
        ArgumentNullException.ThrowIfNull(request);
        if (_initialCandidate is not null)
            throw new InvalidOperationException("An initial Gateway candidate is already registered.");
        _initialCandidate = request with
        {
            Utf8Configuration = request.Utf8Configuration.IsDefault
                ? default
                : ImmutableArray.CreateRange(request.Utf8Configuration.AsSpan().ToArray())
        };
        return this;
    }

    public GatewayBuilder AddRequestInspection(
        Action<GatewayInspectionRegistryBuilder> configure,
        bool allowFileSpill = false)
    {
        ThrowIfSealed();
        ArgumentNullException.ThrowIfNull(configure);
        if (!_requestInspectors.IsEmpty)
            throw new InvalidOperationException("Request inspection is already registered.");
        var registryBuilder = new GatewayInspectionRegistryBuilder();
        configure(registryBuilder);
        var registry = registryBuilder.Build();
        if (registry.Names.IsEmpty)
            throw new InvalidOperationException("At least one request inspector must be registered.");
        Services.AddHpdGatewayYarpInspection(registry);
        _requestInspectors = registry.Names;
        _allowInspectionFileSpill = allowFileSpill;
        _installedFamilies |= GatewayDeclarationFamilies.Inspection;
        return this;
    }

    public GatewayBuilder ProtectCredentialHeaders(params string[] headerNames)
    {
        ThrowIfSealed();
        ArgumentNullException.ThrowIfNull(headerNames);
        if (!_protectedCredentialHeaders.IsEmpty)
            throw new InvalidOperationException("Protected credential headers are already registered.");
        _protectedCredentialHeaders = ImmutableArray.CreateRange(headerNames);
        return this;
    }

    public GatewayBuilder AddAuthorizationPolicy(
        string name,
        Action<AuthorizationPolicyBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        AddPolicyName(name, _authorizationPolicies, GatewayDeclarationFamilies.Authorization);
        Services.AddAuthorizationBuilder().AddPolicy(name, configure);
        return this;
    }

    public GatewayBuilder AddCorsPolicy(string name, Action<CorsPolicyBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        AddPolicyName(name, _corsPolicies, GatewayDeclarationFamilies.Cors);
        Services.AddCors(options => options.AddPolicy(name, configure));
        return this;
    }

    public GatewayBuilder AddTrafficAdmission(Action<GatewayTrafficAdmissionRegistryBuilder> configure)
    {
        ThrowIfSealed();
        ArgumentNullException.ThrowIfNull(configure);
        if (_trafficAdmission is not null)
            throw new InvalidOperationException("Traffic admission is already registered.");
        var builder = new GatewayTrafficAdmissionRegistryBuilder(Services);
        configure(builder);
        var registry = builder.Build();
        if (registry.Capabilities.IsEmpty)
            throw new InvalidOperationException("At least one traffic-admission profile must be registered.");
        _trafficAdmission = registry;
        Services.AddSingleton(registry);
        _installedFamilies |= GatewayDeclarationFamilies.TrafficAdmission;
        return this;
    }

    public GatewayBuilder AddRequestTimeoutPolicy(string name, TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero || timeout > TimeSpan.FromDays(1))
            throw new ArgumentOutOfRangeException(nameof(timeout));
        AddPolicyName(name, _requestTimeoutPolicies, GatewayDeclarationFamilies.RequestTimeout);
        Services.AddRequestTimeouts(options => options.AddPolicy(name, timeout));
        return this;
    }

    internal void AddResilienceCapabilities(ImmutableArray<UpstreamResilienceCapability> capabilities)
    {
        ThrowIfSealed();
        if (!_resilienceProfiles.IsEmpty || capabilities.IsDefaultOrEmpty)
            throw new InvalidOperationException("A nonempty resilience registry may be contributed only once.");
        _resilienceProfiles = capabilities;
        _installedFamilies |= GatewayDeclarationFamilies.UpstreamResilience;
    }

    internal void AddOutputCacheCapabilities(ImmutableArray<OutputCacheCapability> capabilities)
    {
        ThrowIfSealed();
        if (!_outputCacheProfiles.IsEmpty || capabilities.IsDefaultOrEmpty)
            throw new InvalidOperationException("A nonempty Output Cache registry may be contributed only once.");
        _outputCacheProfiles = capabilities;
        _installedFamilies |= GatewayDeclarationFamilies.OutputCache;
    }

    internal void AddDiscoveryCapabilities(ImmutableArray<DiscoveryProfileCapability> capabilities)
    {
        ThrowIfSealed();
        if (!_discoveryProfiles.IsEmpty || capabilities.IsDefaultOrEmpty)
            throw new InvalidOperationException("A nonempty discovery registry may be contributed only once.");
        _discoveryProfiles = capabilities;
    }

    internal GatewayCompositionState Seal()
    {
        ThrowIfSealed();
        _sealed = true;
        return new GatewayCompositionState(
            _installedFamilies,
            _initialCandidate,
            _requestInspectors,
            _resilienceProfiles,
            _outputCacheProfiles,
            _discoveryProfiles,
            _protectedCredentialHeaders,
            _authorizationPolicies.Order(StringComparer.Ordinal).ToImmutableArray(),
            _corsPolicies.Order(StringComparer.Ordinal).ToImmutableArray(),
            _trafficAdmission?.Capabilities ?? [],
            _requestTimeoutPolicies.Order(StringComparer.Ordinal).ToImmutableArray(),
            _allowInspectionFileSpill);
    }

    internal void ThrowIfSealed()
    {
        if (_sealed)
            throw new InvalidOperationException("The HPD Gateway composition is already sealed.");
    }

    private void AddPolicyName(
        string name,
        HashSet<string> names,
        GatewayDeclarationFamilies family)
    {
        ThrowIfSealed();
        if (!GatewayIdentifier.IsCanonical(name) || !names.Add(name))
            throw new ArgumentException("Policy names must be canonical and unique.", nameof(name));
        _installedFamilies |= family;
    }
}

internal sealed record GatewayCompositionState(
    GatewayDeclarationFamilies InstalledFamilies,
    GatewayNodeActivationRequest? InitialCandidate,
    ImmutableArray<string> RequestInspectors,
    ImmutableArray<UpstreamResilienceCapability> ResilienceProfiles,
    ImmutableArray<OutputCacheCapability> OutputCacheProfiles,
    ImmutableArray<DiscoveryProfileCapability> DiscoveryProfiles,
    ImmutableArray<string> ProtectedCredentialHeaders,
    ImmutableArray<string> AuthorizationPolicies,
    ImmutableArray<string> CorsPolicies,
    ImmutableArray<TrafficAdmissionCapability> TrafficAdmissionProfiles,
    ImmutableArray<string> RequestTimeoutPolicies,
    bool AllowInspectionFileSpill);

internal sealed class HpdGatewayCompositionMarker;
