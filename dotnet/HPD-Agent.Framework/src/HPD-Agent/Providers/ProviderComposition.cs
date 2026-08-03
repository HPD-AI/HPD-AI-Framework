using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace HPD.Agent.Providers;

/// <summary>Provides immutable lookup of composed provider descriptors.</summary>
public interface IProviderDescriptorRegistry
{
    /// <summary>Gets all canonical provider descriptors.</summary>
    IReadOnlyCollection<IProviderDescriptor> Providers { get; }

    /// <summary>Converts a canonical key or alias to its canonical provider key.</summary>
    /// <exception cref="KeyNotFoundException">The key is not registered.</exception>
    string Canonicalize(string providerKey);

    /// <summary>Attempts to find a provider using either its canonical key or an alias.</summary>
    bool TryGet(string providerKey, out IProviderDescriptor? descriptor);
}

/// <summary>Provides immutable lookup of statically reachable provider factories.</summary>
public interface IProviderRuntimeRegistry
{
    /// <summary>Gets the factory for a provider and client family.</summary>
    /// <exception cref="KeyNotFoundException">No matching factory is registered.</exception>
    ProviderRuntimeFactoryRegistration GetFactory(string providerKey, ProviderClientFamily family);

    /// <summary>Attempts to find the factory for a provider and client family.</summary>
    bool TryGetFactory(
        string providerKey,
        ProviderClientFamily family,
        out ProviderRuntimeFactoryRegistration? registration);
}

/// <summary>Represents an invalid provider composition.</summary>
public sealed class ProviderCompositionException : InvalidOperationException
{
    /// <summary>Initializes a composition exception with a stable error code.</summary>
    public ProviderCompositionException(string code, string message) : base(message) => Code = code;

    /// <summary>Gets the stable error code.</summary>
    public string Code { get; }
}

/// <summary>Contains the immutable descriptor and runtime registries for one host.</summary>
public sealed class ProviderComposition
{
    private ProviderComposition(
        IReadOnlyList<ProviderManifestFragment> fragments,
        IProviderDescriptorRegistry descriptors,
        IProviderRuntimeRegistry runtime)
    {
        Fragments = fragments;
        Descriptors = descriptors;
        Runtime = runtime;
    }

    /// <summary>Gets the manifest fragments used to construct this composition.</summary>
    public IReadOnlyList<ProviderManifestFragment> Fragments { get; }

    /// <summary>Gets the composed descriptor registry.</summary>
    public IProviderDescriptorRegistry Descriptors { get; }

    /// <summary>Gets the composed runtime registry.</summary>
    public IProviderRuntimeRegistry Runtime { get; }

    /// <summary>Creates an isolated immutable composition from manifest fragments.</summary>
    public static ProviderComposition Create(IReadOnlyList<ProviderManifestFragment> fragments)
    {
        ArgumentNullException.ThrowIfNull(fragments);
        var fragmentCopy = new List<ProviderManifestFragment>(fragments).AsReadOnly();
        var registry = DescriptorRegistry.Create(fragmentCopy);
        return new ProviderComposition(fragmentCopy, registry, RuntimeRegistry.Create(fragmentCopy, registry));
    }

    private sealed class DescriptorRegistry : IProviderDescriptorRegistry
    {
        private readonly IReadOnlyDictionary<string, IProviderDescriptor> _providers;
        private readonly IReadOnlyDictionary<string, string> _canonicalKeys;

        private DescriptorRegistry(
            IReadOnlyDictionary<string, IProviderDescriptor> providers,
            IReadOnlyDictionary<string, string> canonicalKeys)
        {
            _providers = providers;
            _canonicalKeys = canonicalKeys;
            Providers = Array.AsReadOnly(providers.Values.OrderBy(x => x.ProviderKey, StringComparer.Ordinal).ToArray());
        }

        public IReadOnlyCollection<IProviderDescriptor> Providers { get; }

        public string Canonicalize(string providerKey)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);
            return _canonicalKeys.TryGetValue(providerKey, out var canonical)
                ? canonical
                : throw new KeyNotFoundException($"Provider key or alias '{providerKey}' is not registered.");
        }

        public bool TryGet(string providerKey, out IProviderDescriptor? descriptor)
        {
            descriptor = null;
            return !string.IsNullOrWhiteSpace(providerKey) &&
                _canonicalKeys.TryGetValue(providerKey, out var canonical) &&
                _providers.TryGetValue(canonical, out descriptor);
        }

        public static DescriptorRegistry Create(IReadOnlyList<ProviderManifestFragment> fragments)
        {
            var groups = fragments.SelectMany(x => x.Descriptors)
                .GroupBy(x => x.ProviderKey, StringComparer.OrdinalIgnoreCase);
            var providers = new Dictionary<string, IProviderDescriptor>(StringComparer.OrdinalIgnoreCase);
            var keys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var group in groups)
            {
                var contributions = group.ToArray();
                var canonical = contributions[0].ProviderKey;
                var displayName = contributions[0].DisplayName;
                var documentationUri = contributions[0].DocumentationUri;
                var families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>();
                var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var contribution in contributions)
                {
                    if (!string.Equals(displayName, contribution.DisplayName, StringComparison.Ordinal) ||
                        !Equals(documentationUri, contribution.DocumentationUri))
                        throw Error("HPDP012", $"Provider '{canonical}' has conflicting display metadata.");

                    foreach (var pair in contribution.Families)
                    {
                        if (!families.TryAdd(pair.Key, pair.Value))
                            throw Error("HPDP010", $"Provider '{canonical}' contributes client family '{pair.Key}' more than once.");
                    }

                    foreach (var alias in contribution.Aliases)
                        aliases.Add(alias);
                }

                var descriptor = new CompositeDescriptor(canonical, displayName, documentationUri, families, aliases);
                providers.Add(canonical, descriptor);
                AddKey(keys, canonical, canonical);
                foreach (var alias in aliases)
                    AddKey(keys, alias, canonical);
            }

            return new DescriptorRegistry(
                new ReadOnlyDictionary<string, IProviderDescriptor>(providers),
                new ReadOnlyDictionary<string, string>(keys));
        }

        private static void AddKey(Dictionary<string, string> keys, string key, string canonical)
        {
            if (keys.TryGetValue(key, out var existing) && !string.Equals(existing, canonical, StringComparison.OrdinalIgnoreCase))
                throw Error("HPDP011", $"Provider key or alias '{key}' is claimed by both '{existing}' and '{canonical}'.");
            keys[key] = canonical;
        }
    }

    private sealed class RuntimeRegistry : IProviderRuntimeRegistry
    {
        private readonly IProviderDescriptorRegistry _descriptors;
        private readonly IReadOnlyDictionary<(string Key, ProviderClientFamily Family), ProviderRuntimeFactoryRegistration> _factories;

        private RuntimeRegistry(IProviderDescriptorRegistry descriptors, Dictionary<(string, ProviderClientFamily), ProviderRuntimeFactoryRegistration> factories)
        {
            _descriptors = descriptors;
            _factories = new ReadOnlyDictionary<(string, ProviderClientFamily), ProviderRuntimeFactoryRegistration>(factories);
        }

        public ProviderRuntimeFactoryRegistration GetFactory(string providerKey, ProviderClientFamily family) =>
            TryGetFactory(providerKey, family, out var registration)
                ? registration!
                : throw new KeyNotFoundException($"Provider '{providerKey}' does not support client family '{family}'.");

        public bool TryGetFactory(string providerKey, ProviderClientFamily family, out ProviderRuntimeFactoryRegistration? registration)
        {
            registration = null;
            if (!_descriptors.TryGet(providerKey, out var descriptor))
                return false;
            return _factories.TryGetValue((descriptor.ProviderKey, family), out registration);
        }

        public static RuntimeRegistry Create(IReadOnlyList<ProviderManifestFragment> fragments, IProviderDescriptorRegistry descriptors)
        {
            var factories = new Dictionary<(string, ProviderClientFamily), ProviderRuntimeFactoryRegistration>(new RuntimeKeyComparer());
            foreach (var registration in fragments.SelectMany(x => x.RuntimeFactories))
            {
                var canonical = descriptors.Canonicalize(registration.ProviderKey);
                foreach (var family in registration.Families)
                {
                    if (!factories.TryAdd((canonical, family), registration))
                        throw Error("HPDP010", $"Provider '{canonical}' has more than one runtime factory for client family '{family}'.");
                }
            }
            return new RuntimeRegistry(descriptors, factories);
        }
    }

    private sealed class CompositeDescriptor : IProviderDescriptor
    {
        public CompositeDescriptor(string key, string name, Uri? uri, Dictionary<ProviderClientFamily, ProviderFamilyDescriptor> families, HashSet<string> aliases)
        {
            ProviderKey = key;
            DisplayName = name;
            DocumentationUri = uri;
            Families = new ReadOnlyDictionary<ProviderClientFamily, ProviderFamilyDescriptor>(families);
            Aliases = Array.AsReadOnly(aliases.OrderBy(x => x, StringComparer.Ordinal).ToArray());
        }
        public string ProviderKey { get; }
        public string DisplayName { get; }
        public Uri? DocumentationUri { get; }
        public IReadOnlyDictionary<ProviderClientFamily, ProviderFamilyDescriptor> Families { get; }
        public IReadOnlyList<string> Aliases { get; }
    }

    private sealed class RuntimeKeyComparer : IEqualityComparer<(string Key, ProviderClientFamily Family)>
    {
        public bool Equals((string Key, ProviderClientFamily Family) x, (string Key, ProviderClientFamily Family) y) =>
            x.Family == y.Family && StringComparer.OrdinalIgnoreCase.Equals(x.Key, y.Key);
        public int GetHashCode((string Key, ProviderClientFamily Family) obj) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Key), obj.Family);
    }

    private static ProviderCompositionException Error(string code, string message) => new(code, message);
}
