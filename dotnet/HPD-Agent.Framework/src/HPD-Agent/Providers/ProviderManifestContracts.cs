using System;
using System.Collections.Generic;
using System.Text.Json.Serialization.Metadata;

namespace HPD.Agent.Providers;

/// <summary>
/// Declares the canonical identity and display metadata for a provider implementation.
/// </summary>
/// <remarks>
/// The provider source generator converts this declaration into an immutable provider
/// manifest fragment. It does not perform runtime registration or mutate global state.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class HpdProviderAttribute : Attribute
{
    /// <summary>Initializes a provider declaration.</summary>
    /// <param name="providerKey">The lowercase, URL-safe canonical provider key.</param>
    /// <param name="displayName">The provider name shown to users.</param>
    public HpdProviderAttribute(string providerKey, string displayName)
    {
        ProviderKey = providerKey;
        DisplayName = displayName;
    }

    /// <summary>Gets the canonical provider key.</summary>
    public string ProviderKey { get; }

    /// <summary>Gets the provider display name.</summary>
    public string DisplayName { get; }

    /// <summary>Gets or sets the provider documentation URL.</summary>
    public string? DocumentationUrl { get; set; }
}

/// <summary>Declares one client-family contribution made by a provider implementation.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class HpdProviderFamilyAttribute : Attribute
{
    /// <summary>Initializes a provider-family declaration.</summary>
    /// <param name="family">The contributed client family.</param>
    public HpdProviderFamilyAttribute(ProviderClientFamily family) => Family = family;

    /// <summary>Gets the contributed family.</summary>
    public ProviderClientFamily Family { get; }

    /// <summary>Gets or sets the component lifetime.</summary>
    public ProviderFamilyLifetime Lifetime { get; set; } = ProviderFamilyLifetime.ReusableClient;

    /// <summary>Gets or sets the built-in model used only as the final host fallback.</summary>
    public string? DefaultModelName { get; set; }
}

/// <summary>Declares an alternate key that canonicalizes to the provider key.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class HpdProviderAliasAttribute : Attribute
{
    /// <summary>Initializes a provider alias declaration.</summary>
    /// <param name="alias">The alternate provider key.</param>
    public HpdProviderAliasAttribute(string alias) => Alias = alias;

    /// <summary>Gets the declared alias.</summary>
    public string Alias { get; }
}

/// <summary>Declares a source-generated JSON contract owned by a provider family.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
public sealed class HpdProviderPayloadAttribute : Attribute
{
    /// <summary>Initializes a provider payload declaration.</summary>
    public HpdProviderPayloadAttribute(
        ProviderClientFamily family,
        ProviderPayloadKind kind,
        Type payloadType,
        Type jsonContextType)
    {
        Family = family;
        Kind = kind;
        PayloadType = payloadType;
        JsonContextType = jsonContextType;
    }

    /// <summary>Gets the owning client family.</summary>
    public ProviderClientFamily Family { get; }

    /// <summary>Gets the payload's role.</summary>
    public ProviderPayloadKind Kind { get; }

    /// <summary>Gets the concrete payload type.</summary>
    public Type PayloadType { get; }

    /// <summary>Gets the source-generated JSON context type.</summary>
    public Type JsonContextType { get; }
}

/// <summary>Identifies the role of a provider-specific payload.</summary>
public enum ProviderPayloadKind
{
    /// <summary>Client-construction configuration.</summary>
    Configuration = 0,

    /// <summary>Options compiled for one provider operation.</summary>
    OperationOptions = 1
}

/// <summary>Describes one closed, AOT-safe provider JSON payload contract.</summary>
public sealed class ProviderPayloadJsonContract
{
    /// <summary>Initializes a payload contract.</summary>
    public ProviderPayloadJsonContract(
        string providerKey,
        ProviderClientFamily family,
        ProviderPayloadKind kind,
        Type runtimeType,
        JsonTypeInfo jsonTypeInfo)
    {
        ProviderKey = providerKey;
        Family = family;
        Kind = kind;
        RuntimeType = runtimeType;
        JsonTypeInfo = jsonTypeInfo;
    }

    /// <summary>Gets the canonical provider key.</summary>
    public string ProviderKey { get; }

    /// <summary>Gets the owning client family.</summary>
    public ProviderClientFamily Family { get; }

    /// <summary>Gets the payload role.</summary>
    public ProviderPayloadKind Kind { get; }

    /// <summary>Gets the concrete runtime type used for diagnostics.</summary>
    public Type RuntimeType { get; }

    /// <summary>Gets the source-generated JSON metadata.</summary>
    public JsonTypeInfo JsonTypeInfo { get; }
}

/// <summary>Looks up generated provider payload JSON contracts.</summary>
public interface IProviderSerializationRegistry
{
    /// <summary>Attempts to find the contract for a provider, family, and payload role.</summary>
    bool TryGet(
        string providerKey,
        ProviderClientFamily family,
        ProviderPayloadKind kind,
        out ProviderPayloadJsonContract? contract);
}

/// <summary>Identifies a generated provider manifest exposed by a provider assembly.</summary>
/// <remarks>
/// Consuming-host source generation reads this assembly metadata and emits direct
/// references to each manifest. Runtime assembly scanning is not used.
/// </remarks>
[AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
public sealed class HpdProviderManifestAttribute : Attribute
{
    /// <summary>Initializes a manifest marker.</summary>
    /// <param name="manifestType">The generated public static manifest type.</param>
    /// <param name="providerKey">The canonical provider key contributed by the manifest.</param>
    /// <param name="families">The client families contributed by the manifest.</param>
    public HpdProviderManifestAttribute(
        Type manifestType,
        string providerKey,
        params ProviderClientFamily[] families)
    {
        ManifestType = manifestType;
        ProviderKey = providerKey;
        Families = Array.AsReadOnly((ProviderClientFamily[])families.Clone());
    }

    /// <summary>Gets the generated manifest type.</summary>
    public Type ManifestType { get; }

    /// <summary>Gets the canonical provider key contributed by the manifest.</summary>
    public string ProviderKey { get; }

    /// <summary>Gets the client families contributed by the manifest.</summary>
    public IReadOnlyList<ProviderClientFamily> Families { get; }

    /// <summary>Gets or sets aliases contributed by the manifest.</summary>
    public string[] Aliases { get; set; } = Array.Empty<string>();
}

/// <summary>Describes one immutable provider contribution before same-key composition.</summary>
public interface IProviderDescriptor
{
    /// <summary>Gets the canonical provider key.</summary>
    string ProviderKey { get; }

    /// <summary>Gets the provider display name.</summary>
    string DisplayName { get; }

    /// <summary>Gets the optional provider documentation URL.</summary>
    Uri? DocumentationUri { get; }

    /// <summary>Gets the client-family contributions in this manifest.</summary>
    IReadOnlyDictionary<ProviderClientFamily, ProviderFamilyDescriptor> Families { get; }

    /// <summary>Gets aliases that canonicalize to <see cref="ProviderKey"/>.</summary>
    IReadOnlyList<string> Aliases { get; }
}

/// <summary>Provides a closed runtime factory contributed by a provider manifest.</summary>
public sealed class ProviderRuntimeFactoryRegistration
{
    /// <summary>Initializes a closed provider factory registration.</summary>
    public ProviderRuntimeFactoryRegistration(
        string providerKey,
        IReadOnlyList<ProviderClientFamily> families,
        Func<IProvider> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerKey);
        ArgumentNullException.ThrowIfNull(families);
        ArgumentNullException.ThrowIfNull(factory);
        ProviderKey = providerKey;
        Families = new List<ProviderClientFamily>(families).AsReadOnly();
        Factory = factory;
    }

    /// <summary>Gets the canonical provider key.</summary>
    public string ProviderKey { get; }

    /// <summary>Gets the client families created by this factory.</summary>
    public IReadOnlyList<ProviderClientFamily> Families { get; }

    /// <summary>Gets the statically reachable provider factory.</summary>
    public Func<IProvider> Factory { get; }
}

/// <summary>
/// Contains the immutable descriptor and runtime-factory contributions emitted by one
/// provider assembly.
/// </summary>
/// <param name="Descriptors">Provider descriptor contributions.</param>
/// <param name="RuntimeFactories">Closed runtime provider factories.</param>
public sealed class ProviderManifestFragment
{
    /// <summary>Initializes an immutable provider manifest fragment.</summary>
    /// <param name="descriptors">Provider descriptor contributions.</param>
    /// <param name="runtimeFactories">Closed runtime provider factories.</param>
    public ProviderManifestFragment(
        IReadOnlyList<IProviderDescriptor> descriptors,
        IReadOnlyList<ProviderRuntimeFactoryRegistration> runtimeFactories,
        IReadOnlyList<ProviderPayloadJsonContract> serializationContracts)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(runtimeFactories);
        ArgumentNullException.ThrowIfNull(serializationContracts);
        Descriptors = new List<IProviderDescriptor>(descriptors).AsReadOnly();
        RuntimeFactories = new List<ProviderRuntimeFactoryRegistration>(runtimeFactories).AsReadOnly();
        SerializationContracts = new List<ProviderPayloadJsonContract>(serializationContracts).AsReadOnly();
    }

    /// <summary>Gets immutable provider descriptor contributions.</summary>
    public IReadOnlyList<IProviderDescriptor> Descriptors { get; }

    /// <summary>Gets immutable closed runtime provider factories.</summary>
    public IReadOnlyList<ProviderRuntimeFactoryRegistration> RuntimeFactories { get; }

    /// <summary>Gets immutable source-generated provider payload contracts.</summary>
    public IReadOnlyList<ProviderPayloadJsonContract> SerializationContracts { get; }
}
