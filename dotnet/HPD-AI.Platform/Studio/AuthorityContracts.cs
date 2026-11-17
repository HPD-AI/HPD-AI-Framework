using System.Collections.Immutable;
using System.Globalization;

namespace HPD.AI.Platform.Studio;

/// <summary>Captures one store authority pinned by a protected Studio response.</summary>
public sealed class StudioStoreAuthority
{
    private StudioStoreAuthority(string store, long provider, long restore, long schema,
        StudioSha256 capability, StudioSha256 checksum)
    { StoreIdentity = store; ProviderGeneration = provider; RestoreEpoch = restore; SchemaGeneration = schema;
      CapabilityChecksum = capability; Checksum = checksum; }
    /// <summary>Gets the logical store identity.</summary>
    public string StoreIdentity { get; }
    /// <summary>Gets the provider generation.</summary>
    public long ProviderGeneration { get; }
    /// <summary>Gets the restore epoch.</summary>
    public long RestoreEpoch { get; }
    /// <summary>Gets the schema generation.</summary>
    public long SchemaGeneration { get; }
    /// <summary>Gets the provider capability checksum.</summary>
    public StudioSha256 CapabilityChecksum { get; }
    /// <summary>Gets the canonical store-authority checksum.</summary>
    public StudioSha256 Checksum { get; }

    public static StudioStoreAuthority Create(string store, long provider, long restore, long schema, StudioSha256 capability)
    {
        StudioContractValidation.Id(store); ArgumentNullException.ThrowIfNull(capability);
        if (provider < 1 || restore < 0 || schema < 0) throw new ArgumentOutOfRangeException(nameof(provider));
        StudioSha256 owned = StudioSha256.FromBytes(capability.ToArray());
        StudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.store-authority.v1", writer =>
        { writer.String(store); writer.Int64(provider); writer.Int64(restore); writer.Int64(schema); writer.Checksum(owned); });
        return new(store, provider, restore, schema, owned, checksum);
    }
}

/// <summary>Provides the common current authorization envelope on every protected Studio response.</summary>
public sealed class StudioResponseAuthority
{
    private StudioResponseAuthority(long principal, StudioSha256 session, StudioSha256 scope,
        long application, StudioSha256 applicationChecksum, long studio, StudioSha256 studioChecksum,
        long policy, StudioSha256 policyChecksum, ImmutableArray<StudioStoreAuthority> stores,
        DateTimeOffset authorizedThrough, StudioSha256 checksum)
    { PrincipalGeneration = principal; AuthenticatedSessionChecksum = session; ProtectedScopeChecksum = scope;
      ApplicationGraphGeneration = application; ApplicationGraphChecksum = applicationChecksum;
      StudioOwnerGeneration = studio; StudioOwnerChecksum = studioChecksum; PolicyOwnerGeneration = policy;
      PolicyOwnerChecksum = policyChecksum; Stores = stores; AuthorizedThroughUtc = authorizedThrough; Checksum = checksum; }
    /// <summary>Gets the authenticated principal generation.</summary>
    public long PrincipalGeneration { get; }
    /// <summary>Gets the authenticated session checksum.</summary>
    public StudioSha256 AuthenticatedSessionChecksum { get; }
    /// <summary>Gets the protected scope checksum.</summary>
    public StudioSha256 ProtectedScopeChecksum { get; }
    /// <summary>Gets the application graph generation.</summary>
    public long ApplicationGraphGeneration { get; }
    /// <summary>Gets the application graph checksum.</summary>
    public StudioSha256 ApplicationGraphChecksum { get; }
    /// <summary>Gets the Studio owner generation.</summary>
    public long StudioOwnerGeneration { get; }
    /// <summary>Gets the Studio owner checksum.</summary>
    public StudioSha256 StudioOwnerChecksum { get; }
    /// <summary>Gets the policy owner generation.</summary>
    public long PolicyOwnerGeneration { get; }
    /// <summary>Gets the policy owner checksum.</summary>
    public StudioSha256 PolicyOwnerChecksum { get; }
    /// <summary>Gets required store authorities in ordinal store order.</summary>
    public ImmutableArray<StudioStoreAuthority> Stores { get; }
    /// <summary>Gets the nonrenewable authorization-lease expiry.</summary>
    public DateTimeOffset AuthorizedThroughUtc { get; }
    /// <summary>Gets the canonical response-authority checksum.</summary>
    public StudioSha256 Checksum { get; }

    public static StudioResponseAuthority Create(long principal, StudioSha256 session, StudioSha256 scope,
        long application, StudioSha256 applicationChecksum, long studio, StudioSha256 studioChecksum,
        long policy, StudioSha256 policyChecksum, IEnumerable<StudioStoreAuthority> stores,
        DateTimeOffset issuedAtUtc, DateTimeOffset sessionExpiresAtUtc,
        IEnumerable<DateTimeOffset> admittedAuthorityExpiries)
    {
        if (principal < 1 || application < 1 || studio < 1 || policy < 1) throw new ArgumentOutOfRangeException(nameof(principal));
        ArgumentNullException.ThrowIfNull(session); ArgumentNullException.ThrowIfNull(scope); ArgumentNullException.ThrowIfNull(applicationChecksum);
        ArgumentNullException.ThrowIfNull(studioChecksum); ArgumentNullException.ThrowIfNull(policyChecksum);
        if (issuedAtUtc.Offset != TimeSpan.Zero || sessionExpiresAtUtc.Offset != TimeSpan.Zero ||
            issuedAtUtc == default || sessionExpiresAtUtc <= issuedAtUtc)
            throw new ArgumentException("Studio authorization times must be canonical UTC.", nameof(issuedAtUtc));
        DateTimeOffset[] expiries = admittedAuthorityExpiries?.Take(129).ToArray()
            ?? throw new ArgumentNullException(nameof(admittedAuthorityExpiries));
        if (expiries.Length > 128 || expiries.Any(value => value.Offset != TimeSpan.Zero || value <= issuedAtUtc))
            throw new ArgumentException("Studio admitted-authority expiries are invalid.", nameof(admittedAuthorityExpiries));
        DateTimeOffset authorizedThroughUtc = new[] { issuedAtUtc.AddSeconds(30), sessionExpiresAtUtc }
            .Concat(expiries).Min();
        ImmutableArray<StudioStoreAuthority> ownedStores = StudioContractValidation.Materialize(stores, 32, true, nameof(stores));
        if (!ownedStores.Select(static value => value.StoreIdentity).SequenceEqual(ownedStores.Select(static value => value.StoreIdentity).Order(StringComparer.Ordinal)) ||
            ownedStores.Select(static value => value.StoreIdentity).Distinct(StringComparer.Ordinal).Count() != ownedStores.Length)
            throw new ArgumentException("Studio store authorities are not canonical.", nameof(stores));
        StudioSha256 ownedSession = StudioSha256.FromBytes(session.ToArray()); StudioSha256 ownedScope = StudioSha256.FromBytes(scope.ToArray());
        StudioSha256 ownedApplication = StudioSha256.FromBytes(applicationChecksum.ToArray());
        StudioSha256 ownedStudio = StudioSha256.FromBytes(studioChecksum.ToArray()); StudioSha256 ownedPolicy = StudioSha256.FromBytes(policyChecksum.ToArray());
        StudioSha256 checksum = StudioCanonicalEncoding.Hash("base.studio.response-authority.v1", writer =>
        {
            writer.Int64(principal); writer.Checksum(ownedSession); writer.Checksum(ownedScope); writer.Int64(application); writer.Checksum(ownedApplication);
            writer.Int64(studio); writer.Checksum(ownedStudio); writer.Int64(policy); writer.Checksum(ownedPolicy);
            StudioGraphValidation.Encode(writer, ownedStores, static value => value.Checksum); writer.String(CanonicalUtc(authorizedThroughUtc));
        });
        return new(principal, ownedSession, ownedScope, application, ownedApplication, studio, ownedStudio,
            policy, ownedPolicy, ownedStores, authorizedThroughUtc, checksum);
    }

    internal static string CanonicalUtc(DateTimeOffset value)
        => value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture);
}
