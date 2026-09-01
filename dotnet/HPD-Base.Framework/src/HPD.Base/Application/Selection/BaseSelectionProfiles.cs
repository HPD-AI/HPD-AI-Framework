using System.Collections.Immutable;

namespace HPD.Base;

/// <summary>Identifies one source-generated application module installation.</summary>
public sealed class BaseGeneratedModuleRegistration
{
    internal BaseGeneratedModuleRegistration(string applicationId, ImmutableHashSet<string> collectionIds)
    {
        ApplicationId = applicationId;
        CollectionIds = collectionIds;
    }
    internal string ApplicationId { get; }
    internal ImmutableHashSet<string> CollectionIds { get; }
}

/// <summary>Describes one generator-owned selection profile identity.</summary>
internal sealed record BaseGeneratedSelectionProfileDescriptor
{
    /// <summary>Gets the owning application identifier.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the owning collection identifier.</summary>
    public required string CollectionId { get; init; }
    /// <summary>Gets the stable profile identifier.</summary>
    public required string ProfileId { get; init; }
    /// <summary>Gets the semantic profile version.</summary>
    public required int Version { get; init; }
    /// <summary>Gets the fixed mutation kind.</summary>
    public required BaseSelectionMutationKind Kind { get; init; }
    /// <summary>Gets the expected finalized semantic checksum.</summary>
    public required string Checksum { get; init; }
}

/// <summary>Provides the generator-only module registration boundary.</summary>
public static class BaseGeneratedModules
{
    /// <summary>Creates one opaque generated module registration.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static BaseGeneratedModuleRegistration RegisterCollectionModule(string applicationId, string collectionId)
    {
        BaseApplicationId.Validate(applicationId, nameof(applicationId));
        BaseApplicationId.Validate(collectionId, nameof(collectionId));
        return new BaseGeneratedModuleRegistration(
            new string(applicationId.AsSpan()),
            ImmutableHashSet.Create(StringComparer.Ordinal, new string(collectionId.AsSpan())));
    }
}

internal sealed class BaseSelectionProfileRegistry
{
    private readonly Dictionary<(string Application, string Collection, string Id, int Version), BaseSelectionOperationProfile> _profiles;

    internal BaseSelectionProfileRegistry(IEnumerable<BaseSelectionOperationProfile> profiles) =>
        _profiles = profiles.ToDictionary(
            static profile => (profile.ApplicationId, profile.CollectionId, profile.Id, profile.Version));

    internal BaseSelectionOperationProfile? Find(string application, string collection, string id, int version) =>
        _profiles.GetValueOrDefault((application, collection, id, version));

    internal IReadOnlyCollection<BaseSelectionOperationProfile> All => _profiles.Values;
}

internal static class BaseSelectionProfileChecksum
{
    internal static string Compute(BaseSelectionOperationProfile profile)
    {
        byte[] bytes = System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(
            profile, HPDBaseJsonSerializerContext.Default.BaseSelectionOperationProfile);
        return Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(bytes));
    }
}

/// <summary>Identifies one generated, module-registered selection profile without exposing raw lookup.</summary>
public sealed class BaseGeneratedSelectionProfileIdentity
{
    internal BaseGeneratedSelectionProfileIdentity(
        string applicationId,
        string collectionId,
        string profileId,
        int version,
        BaseSelectionMutationKind kind,
        string checksum,
        BaseGeneratedModuleRegistration module)
    {
        ApplicationId = applicationId;
        CollectionId = collectionId;
        ProfileId = profileId;
        Version = version;
        Kind = kind;
        Checksum = checksum;
        Module = module;
    }

    internal string ApplicationId { get; }
    internal string CollectionId { get; }
    internal string ProfileId { get; }
    internal int Version { get; }
    internal BaseSelectionMutationKind Kind { get; }
    internal string Checksum { get; }
    internal BaseGeneratedModuleRegistration Module { get; }
}

/// <summary>Provides infrastructure-only registration for source-generated profile identities.</summary>
public static class BaseGeneratedSelectionProfiles
{
    /// <summary>
    /// Creates an opaque generated profile identity while deriving its checksum from the complete profile.
    /// </summary>
    /// <param name="module">The generated module authority for the profile collection.</param>
    /// <param name="profile">The complete immutable profile installed by the host.</param>
    /// <returns>An opaque profile identity bound to the exact profile checksum.</returns>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static BaseGeneratedSelectionProfileIdentity RegisterSelectionProfile(
        BaseGeneratedModuleRegistration module,
        BaseSelectionOperationProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);
        return RegisterSelectionProfile(module, new BaseGeneratedSelectionProfileDescriptor
        {
            ApplicationId = profile.ApplicationId,
            CollectionId = profile.CollectionId,
            ProfileId = profile.Id,
            Version = profile.Version,
            Kind = profile.MutationKind,
            Checksum = BaseSelectionProfileChecksum.Compute(profile),
        });
    }

    /// <summary>Registers an opaque generated profile identity during generated module installation.</summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    internal static BaseGeneratedSelectionProfileIdentity RegisterSelectionProfile(
        BaseGeneratedModuleRegistration module,
        BaseGeneratedSelectionProfileDescriptor descriptor)
    {
        ArgumentNullException.ThrowIfNull(module);
        ArgumentNullException.ThrowIfNull(descriptor);
        BaseApplicationId.Validate(descriptor.ApplicationId, nameof(descriptor));
        BaseApplicationId.Validate(descriptor.CollectionId, nameof(descriptor));
        BaseApplicationId.Validate(descriptor.ProfileId, nameof(descriptor));
        ArgumentOutOfRangeException.ThrowIfLessThan(descriptor.Version, 1);
        ArgumentException.ThrowIfNullOrWhiteSpace(descriptor.Checksum);
        if (!Enum.IsDefined(descriptor.Kind)
            || !string.Equals(module.ApplicationId, descriptor.ApplicationId, StringComparison.Ordinal)
            || !module.CollectionIds.Contains(descriptor.CollectionId))
            throw new InvalidOperationException(BaseSelectionErrorCodes.ProfileInvalid);
        return new BaseGeneratedSelectionProfileIdentity(
            new string(descriptor.ApplicationId.AsSpan()), new string(descriptor.CollectionId.AsSpan()),
            new string(descriptor.ProfileId.AsSpan()), descriptor.Version, descriptor.Kind,
            new string(descriptor.Checksum.AsSpan()), module);
    }
}

/// <summary>Binds an installed merge-patch selection profile to one typed collection.</summary>
public sealed class BaseMergePatchSelectionProfile<T>
{
    internal BaseMergePatchSelectionProfile(BaseSelectionOperationProfile profile) => Profile = profile;
    internal BaseSelectionOperationProfile Profile { get; }
    /// <summary>Gets the stable profile identifier.</summary>
    public string Id => Profile.Id;
    /// <summary>Gets the semantic profile version.</summary>
    public int Version => Profile.Version;
    /// <summary>Gets the maximum selected records.</summary>
    public int MaximumSelectedRecords => Profile.Limits.MaximumSelectedRecords;
}

/// <summary>Binds an installed delete selection profile to one typed collection.</summary>
public sealed class BaseDeleteSelectionProfile<T>
{
    internal BaseDeleteSelectionProfile(BaseSelectionOperationProfile profile) => Profile = profile;
    internal BaseSelectionOperationProfile Profile { get; }
    /// <summary>Gets the stable profile identifier.</summary>
    public string Id => Profile.Id;
    /// <summary>Gets the semantic profile version.</summary>
    public int Version => Profile.Version;
    /// <summary>Gets the maximum selected records.</summary>
    public int MaximumSelectedRecords => Profile.Limits.MaximumSelectedRecords;
}
