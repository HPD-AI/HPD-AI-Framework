using Microsoft.AspNetCore.Authorization;

namespace HPD.Auth.ControlPlane;

/// <summary>Immutable validated registry consumed by endpoint composition.</summary>
public sealed class ControlPlaneRegistry
{
    private readonly IReadOnlyDictionary<string, ControlPlaneProfile> _profiles;
    private readonly IReadOnlyDictionary<string, string> _capabilities;

    internal IEnumerable<ControlPlaneProfile> Profiles => _profiles.Values;

    internal ControlPlaneRegistry(HPDControlPlaneOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        ControlPlaneContractValidator.ValidateConfiguration(options);
        _profiles = new Dictionary<string, ControlPlaneProfile>(options.Profiles, StringComparer.Ordinal);
        _capabilities = new Dictionary<string, string>(options.Capabilities, StringComparer.Ordinal);
    }

    public ControlPlaneProfile GetProfile(string name) =>
        _profiles.TryGetValue(name, out var profile)
            ? profile
            : throw new InvalidOperationException("The control-plane profile is not registered.");

    public string GetAuthorizationPolicy(string capability) =>
        _capabilities.TryGetValue(capability, out var policy)
            ? policy
            : throw new InvalidOperationException("The control-plane capability is not mapped.");

    internal void ValidatePolicies(AuthorizationOptions authorization)
    {
        foreach (var mapping in _capabilities)
        {
            var policy = authorization.GetPolicy(mapping.Value)
                ?? throw new InvalidOperationException("A mapped control-plane authorization policy is not registered.");

            if (policy.AuthenticationSchemes.Count != 0)
                throw new InvalidOperationException("A mapped control-plane policy must not select authentication schemes.");
        }
    }
}
