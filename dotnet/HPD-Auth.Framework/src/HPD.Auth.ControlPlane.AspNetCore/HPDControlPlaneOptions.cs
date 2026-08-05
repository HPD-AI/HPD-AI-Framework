using System.Collections.ObjectModel;

namespace HPD.Auth.ControlPlane;

/// <summary>Collects static control-plane profiles and capability mappings.</summary>
public sealed class HPDControlPlaneOptions
{
    private readonly Dictionary<string, ControlPlaneProfile> _profiles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _capabilities = new(StringComparer.Ordinal);

    public bool StrictOpenApiValidation { get; set; }

    public IReadOnlyDictionary<string, ControlPlaneProfile> Profiles =>
        new ReadOnlyDictionary<string, ControlPlaneProfile>(_profiles);

    public IReadOnlyDictionary<string, string> Capabilities =>
        new ReadOnlyDictionary<string, string>(_capabilities);

    public void AddProfile(string name, Action<ControlPlaneProfileBuilder> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        if (_profiles.ContainsKey(name))
            throw new InvalidOperationException("A control-plane profile with this name is already registered.");

        var builder = new ControlPlaneProfileBuilder();
        configure(builder);
        _profiles.Add(name, builder.Build(name));
    }

    public void MapCapability(string capability, string authorizationPolicy)
    {
        if (_capabilities.ContainsKey(capability))
            throw new InvalidOperationException("A control-plane capability is already mapped.");

        _capabilities.Add(capability, authorizationPolicy);
    }
}
