using System;
using System.Collections.Generic;

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
    public HpdProviderManifestAttribute(Type manifestType) => ManifestType = manifestType;

    /// <summary>Gets the generated manifest type.</summary>
    public Type ManifestType { get; }
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
/// <param name="ProviderKey">The canonical provider key.</param>
/// <param name="Factory">The statically reachable provider factory.</param>
public sealed record ProviderRuntimeFactoryRegistration(
    string ProviderKey,
    Func<IProvider> Factory);

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
        IReadOnlyList<ProviderRuntimeFactoryRegistration> runtimeFactories)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(runtimeFactories);
        Descriptors = new List<IProviderDescriptor>(descriptors).AsReadOnly();
        RuntimeFactories = new List<ProviderRuntimeFactoryRegistration>(runtimeFactories).AsReadOnly();
    }

    /// <summary>Gets immutable provider descriptor contributions.</summary>
    public IReadOnlyList<IProviderDescriptor> Descriptors { get; }

    /// <summary>Gets immutable closed runtime provider factories.</summary>
    public IReadOnlyList<ProviderRuntimeFactoryRegistration> RuntimeFactories { get; }
}
