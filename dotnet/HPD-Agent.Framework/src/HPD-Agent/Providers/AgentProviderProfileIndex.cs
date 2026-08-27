using System.Collections.Immutable;
using System.Runtime.CompilerServices;

namespace HPD.Agent.Providers;

/// <summary>
/// Represents the canonical, immutable provider/backend profile and family-default index
/// prepared for one built agent runtime.
/// </summary>
public sealed class AgentProviderProfileIndex
{
    private static readonly ConditionalWeakTable<AgentConfig, AgentProviderProfileIndex> RuntimeIndexes = new();
    private readonly ImmutableDictionary<ProviderBackendIdentity, AgentProviderBackendProfile> _profiles;
    private readonly ImmutableDictionary<ProviderClientFamily, ProviderBackendIdentity> _defaults;

    private AgentProviderProfileIndex(
        ImmutableDictionary<ProviderBackendIdentity, AgentProviderBackendProfile> profiles,
        ImmutableDictionary<ProviderClientFamily, ProviderBackendIdentity> defaults)
    {
        _profiles = profiles;
        _defaults = defaults;
    }

    /// <summary>Canonicalizes, validates, snapshots, and indexes an agent's serialized profiles.</summary>
    /// <param name="config">The mutable authoring configuration to snapshot.</param>
    /// <param name="composition">The provider composition that owns identity canonicalization.</param>
    /// <returns>An immutable runtime index whose ordering has no semantic meaning.</returns>
    /// <exception cref="AgentRunConfigurationException">
    /// Thrown when canonical profile identities or family defaults are duplicated, or an inner
    /// family selection redirects a profile to another provider/backend.
    /// </exception>
    public static AgentProviderProfileIndex Create(AgentConfig config, ProviderComposition composition)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(composition);
        return RuntimeIndexes.GetValue(config, value => CreateCore(value, composition));
    }

    private static AgentProviderProfileIndex CreateCore(AgentConfig config, ProviderComposition composition)
    {

        var profiles = ImmutableDictionary.CreateBuilder<ProviderBackendIdentity, AgentProviderBackendProfile>();
        foreach (var profile in config.ProviderProfiles)
        {
            ArgumentNullException.ThrowIfNull(profile);
            var provider = composition.Descriptors.Canonicalize(profile.ProviderKey);
            var backend = profile.BackendKey?.Trim();
            if (string.IsNullOrWhiteSpace(backend))
                throw Error("ProviderProfileBackendRequired", "providerProfiles", "A provider profile requires a backend key.", provider);

            var identity = new ProviderBackendIdentity(provider, backend);
            if (profiles.ContainsKey(identity))
                throw Error("DuplicateProviderProfile", $"providerProfiles.{provider}.{backend}",
                    $"Provider profile '{provider}/{backend}' is duplicated after canonicalization.", provider);

            var clients = SnapshotClients(profile.Clients, provider, composition);
            foreach (var family in Enum.GetValues<ProviderClientFamily>())
            {
                var selection = clients.GetFamilyConfig(family)?.Provider;
                if (selection is null)
                    continue;
                var innerProvider = composition.Descriptors.Canonicalize(selection.Key);
                var innerBackend = string.IsNullOrWhiteSpace(selection.Backend) ? backend : selection.Backend.Trim();
                if (!string.Equals(innerProvider, provider, StringComparison.Ordinal) ||
                    !string.Equals(innerBackend, backend, StringComparison.Ordinal))
                    throw Error("ProviderProfileIdentityConflict", $"providerProfiles.{provider}.{backend}.clients.{family}.provider",
                        "A profile family selection cannot redirect its outer provider/backend identity.", provider);
            }

            profiles.Add(identity, new AgentProviderBackendProfile
            {
                ProviderKey = provider,
                BackendKey = backend,
                Clients = clients
            });
        }

        var defaults = ImmutableDictionary.CreateBuilder<ProviderClientFamily, ProviderBackendIdentity>();
        foreach (var value in config.ProviderDefaults)
        {
            ArgumentNullException.ThrowIfNull(value);
            var provider = composition.Descriptors.Canonicalize(value.ProviderKey);
            var backend = value.BackendKey?.Trim();
            if (string.IsNullOrWhiteSpace(backend))
                throw Error("ProviderDefaultBackendRequired", $"providerDefaults.{value.Family}",
                    "A provider family default requires a backend key.", provider);
            if (!defaults.TryAdd(value.Family, new ProviderBackendIdentity(provider, backend)))
                throw Error("DuplicateProviderDefault", $"providerDefaults.{value.Family}",
                    $"Client family '{value.Family}' has more than one default.", provider);
        }

        return new AgentProviderProfileIndex(profiles.ToImmutable(), defaults.ToImmutable());
    }

    /// <summary>Finds a canonical profile by its exact provider/backend identity.</summary>
    /// <param name="identity">The canonical provider/backend identity.</param>
    /// <returns>The immutable profile snapshot, or <see langword="null"/> when absent.</returns>
    public AgentProviderBackendProfile? FindProfile(ProviderBackendIdentity identity) =>
        _profiles.TryGetValue(identity, out var profile) ? profile : null;

    /// <summary>Finds the explicit canonical default for a provider client family.</summary>
    /// <param name="family">The provider client family.</param>
    /// <param name="identity">Receives the configured provider/backend identity.</param>
    /// <returns><see langword="true"/> when exactly one default was configured.</returns>
    public bool TryGetDefault(ProviderClientFamily family, out ProviderBackendIdentity identity) =>
        _defaults.TryGetValue(family, out identity);

    private static AgentClientsConfig SnapshotClients(
        AgentClientsConfig source,
        string providerKey,
        ProviderComposition composition)
    {
        ArgumentNullException.ThrowIfNull(source);
        var snapshot = new AgentClientsConfig();
        foreach (var family in Enum.GetValues<ProviderClientFamily>())
        {
            var config = source.GetFamilyConfig(family);
            if (config is not null)
                snapshot.SetFamilyConfig(
                    family,
                    ProviderClientConfigSnapshot.Clone(config, providerKey, family, composition));
        }
        return snapshot;
    }

    private static AgentRunConfigurationException Error(string code, string path, string message, string? provider = null) =>
        new(code, path, message, provider);
}
