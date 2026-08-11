using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using HPD.Agent.Authority;

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
    /// <summary>Gets every generated runtime registration.</summary>
    IReadOnlyCollection<ProviderRuntimeFactoryRegistration> Registrations { get; }

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
        IProviderRuntimeRegistry runtime,
        IProviderSerializationRegistry serialization,
        IProviderSecretAliasRegistry secretAliases,
        ProviderCatalogV1? authorityCatalog)
    {
        Fragments = fragments;
        Descriptors = descriptors;
        Runtime = runtime;
        Serialization = serialization;
        SecretAliases = secretAliases;
        AuthorityCatalog = authorityCatalog;
    }

    /// <summary>Gets the manifest fragments used to construct this composition.</summary>
    public IReadOnlyList<ProviderManifestFragment> Fragments { get; }

    /// <summary>Gets the composed descriptor registry.</summary>
    public IProviderDescriptorRegistry Descriptors { get; }

    /// <summary>Gets the composed runtime registry.</summary>
    public IProviderRuntimeRegistry Runtime { get; }

    /// <summary>Gets the generated provider payload serialization registry.</summary>
    public IProviderSerializationRegistry Serialization { get; }

    /// <summary>Gets provider-owned environment-variable aliases.</summary>
    public IProviderSecretAliasRegistry SecretAliases { get; }

    /// <summary>Gets the canonical application provider catalog when every fragment is source-generated.</summary>
    /// <remarks>Legacy hand-authored fragments remain supported for finite tests but cannot create authority catalog membership.</remarks>
    public ProviderCatalogV1? AuthorityCatalog { get; }

    /// <summary>Validates an explicitly supplied provider-bound payload without resolving credentials.</summary>
    public void ValidatePayload(
        string? providerKey,
        ProviderClientFamily family,
        ProviderPayloadKind kind,
        object? value,
        string path)
    {
        if (value is null)
            return;
        if (string.IsNullOrWhiteSpace(providerKey))
            throw new AgentRunConfigurationException(
                "ProviderKeyRequired",
                path,
                $"A provider key is required when '{path}' is supplied.");

        var canonical = Descriptors.Canonicalize(providerKey);
        if (!Serialization.TryGet(canonical, family, kind, out var contract) || contract is null ||
            !contract.RuntimeType.IsInstanceOfType(value))
        {
            var code = kind == ProviderPayloadKind.Configuration
                ? "ProviderConfigTypeMismatch"
                : "ProviderOptionsTypeMismatch";
            throw new AgentRunConfigurationException(
                code,
                path,
                $"The value at '{path}' is not compatible with provider '{canonical}' and family '{family}'.",
                canonical,
                contract?.RuntimeType,
                value.GetType());
        }
    }

    /// <summary>Creates an isolated immutable composition from manifest fragments.</summary>
    public static ProviderComposition Create(IReadOnlyList<ProviderManifestFragment> fragments)
    {
        ArgumentNullException.ThrowIfNull(fragments);
        var fragmentCopy = new List<ProviderManifestFragment>(fragments).AsReadOnly();
        var registry = DescriptorRegistry.Create(fragmentCopy);
        return new ProviderComposition(
            fragmentCopy,
            registry,
            RuntimeRegistry.Create(fragmentCopy, registry),
            SerializationRegistry.Create(fragmentCopy, registry),
            SecretAliasRegistry.Create(fragmentCopy),
            ProviderAuthorityCatalogFactoryV1.TryCreate(fragmentCopy));
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
            Registrations = Array.AsReadOnly(factories.Values.Distinct().OrderBy(x => x.ProviderKey, StringComparer.Ordinal).ToArray());
        }

        public IReadOnlyCollection<ProviderRuntimeFactoryRegistration> Registrations { get; }

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

    private sealed class SerializationRegistry : IProviderSerializationRegistry
    {
        private readonly IProviderDescriptorRegistry _descriptors;
        private readonly IReadOnlyDictionary<(string Key, ProviderClientFamily Family, ProviderPayloadKind Kind), ProviderPayloadJsonContract> _contracts;

        private SerializationRegistry(
            IProviderDescriptorRegistry descriptors,
            Dictionary<(string, ProviderClientFamily, ProviderPayloadKind), ProviderPayloadJsonContract> contracts)
        {
            _descriptors = descriptors;
            _contracts = new ReadOnlyDictionary<(string, ProviderClientFamily, ProviderPayloadKind), ProviderPayloadJsonContract>(contracts);
        }

        public bool TryGet(string providerKey, ProviderClientFamily family, ProviderPayloadKind kind, out ProviderPayloadJsonContract? contract)
        {
            contract = null;
            return _descriptors.TryGet(providerKey, out var descriptor) &&
                _contracts.TryGetValue((descriptor.ProviderKey, family, kind), out contract);
        }

        public static SerializationRegistry Create(IReadOnlyList<ProviderManifestFragment> fragments, IProviderDescriptorRegistry descriptors)
        {
            var contracts = new Dictionary<(string, ProviderClientFamily, ProviderPayloadKind), ProviderPayloadJsonContract>(new SerializationKeyComparer());
            foreach (var contract in fragments.SelectMany(x => x.SerializationContracts))
            {
                var canonical = descriptors.Canonicalize(contract.ProviderKey);
                if (!contracts.TryAdd((canonical, contract.Family, contract.Kind), contract))
                    throw Error("HPDP013", $"Provider '{canonical}' has more than one '{contract.Kind}' JSON contract for client family '{contract.Family}'.");
            }
            return new SerializationRegistry(descriptors, contracts);
        }
    }

    private sealed class SecretAliasRegistry : IProviderSecretAliasRegistry
    {
        private readonly IReadOnlyDictionary<string, IReadOnlyList<string>> _aliases;

        private SecretAliasRegistry(IReadOnlyDictionary<string, IReadOnlyList<string>> aliases) => _aliases = aliases;

        public IReadOnlyList<string>? GetEnvironmentVariables(string secretKey) =>
            _aliases.TryGetValue(secretKey, out var aliases) ? aliases : null;

        public static SecretAliasRegistry Create(IReadOnlyList<ProviderManifestFragment> fragments)
        {
            var aliases = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (var registration in fragments.SelectMany(x => x.SecretAliases))
            {
                if (aliases.TryGetValue(registration.SecretKey, out var existing) &&
                    !existing.SequenceEqual(registration.EnvironmentVariables, StringComparer.Ordinal))
                    throw Error("HPDP014", $"Provider secret key '{registration.SecretKey}' has conflicting environment aliases.");
                aliases[registration.SecretKey] = Array.AsReadOnly(registration.EnvironmentVariables.ToArray());
            }

            return new SecretAliasRegistry(new ReadOnlyDictionary<string, IReadOnlyList<string>>(aliases));
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

    private sealed class SerializationKeyComparer : IEqualityComparer<(string Key, ProviderClientFamily Family, ProviderPayloadKind Kind)>
    {
        public bool Equals((string Key, ProviderClientFamily Family, ProviderPayloadKind Kind) x, (string Key, ProviderClientFamily Family, ProviderPayloadKind Kind) y) =>
            x.Family == y.Family && x.Kind == y.Kind && StringComparer.OrdinalIgnoreCase.Equals(x.Key, y.Key);
        public int GetHashCode((string Key, ProviderClientFamily Family, ProviderPayloadKind Kind) obj) =>
            HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(obj.Key), obj.Family, obj.Kind);
    }

    private static ProviderCompositionException Error(string code, string message) => new(code, message);
}
