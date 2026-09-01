using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

/// <summary>Identifies one closed installed operation or automation definition family.</summary>
public enum HPDBaseStudioDefinitionKind : byte
{
    /// <summary>An L30 registered read.</summary>
    RegisteredRead = 1,
    /// <summary>An L43 selection mutation.</summary>
    SelectionMutation,
    /// <summary>An L50 module mutation.</summary>
    ModuleMutation,
    /// <summary>An L51 activation definition.</summary>
    Activation,
    /// <summary>An L51 schedule definition.</summary>
    Schedule,
    /// <summary>An L53 semantic activation definition.</summary>
    SemanticActivation,
}

/// <summary>Projects one exact immutable installed definition authority without executable objects.</summary>
public sealed record HPDBaseStudioDefinitionAuthority
{
    /// <summary>Gets the closed definition family.</summary>
    public required HPDBaseStudioDefinitionKind Kind { get; init; }
    /// <summary>Gets the stable definition identity.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the positive semantic version.</summary>
    public required int Version { get; init; }
    /// <summary>Gets the exact owning module.</summary>
    public required string OwningModuleId { get; init; }
    /// <summary>Gets the full semantic definition checksum.</summary>
    public required ImmutableArray<byte> DefinitionChecksum { get; init; }
}

/// <summary>Projects one exact immutable installed policy registration without exposing its evaluator.</summary>
public sealed record HPDBaseStudioPolicyAuthority
{
    /// <summary>Gets the stable policy identity.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the positive policy version.</summary>
    public required int Version { get; init; }
    /// <summary>Gets the owning module identity.</summary>
    public required string OwningModuleId { get; init; }
    /// <summary>Gets the stable evaluator contract identity.</summary>
    public required string EvaluatorContractId { get; init; }
    /// <summary>Gets the positive evaluator contract version.</summary>
    public required int EvaluatorContractVersion { get; init; }
    /// <summary>Gets the deterministic composition order.</summary>
    public required int CompositionOrder { get; init; }
    /// <summary>Gets the exact semantic registration checksum.</summary>
    public required ImmutableArray<byte> RegistrationChecksum { get; init; }
}

/// <summary>Projects one exact frozen L38 grant registration without exposing its source owner.</summary>
public sealed class HPDBaseStudioGrantAuthority
{
    private readonly byte[] _checksum;
    private readonly AccessGrant? _staticGrant;
    internal HPDBaseStudioGrantAuthority(BaseGrantRegistration registration)
    {
        Id = new string(registration.Registration.Id.AsSpan());
        Version = registration.Registration.Version;
        OwningModuleId = new string(registration.Definition.OwningModuleId.AsSpan());
        SourceContractId = new string(registration.Definition.SourceContractId.AsSpan());
        SourceContractVersion = registration.Definition.SourceContractVersion;
        _checksum = registration.Registration.Checksum.ToArray();
        _staticGrant = registration.StaticGrant is null ? null : BasePolicyAuthorityCanonicalizer.CloneGrant(registration.StaticGrant);
    }
    /// <summary>Gets the registered grant identity.</summary>
    public string Id { get; }
    /// <summary>Gets the registered grant version.</summary>
    public int Version { get; }
    /// <summary>Gets the exact module that owns the L38 grant authority.</summary>
    public string OwningModuleId { get; }
    /// <summary>Gets the exact installed grant-source contract identity.</summary>
    public string SourceContractId { get; }
    /// <summary>Gets the installed grant-source contract version.</summary>
    public int SourceContractVersion { get; }
    /// <summary>Gets whether this registration has immutable grant semantics.</summary>
    public bool HasStaticSemantics => _staticGrant is not null;
    /// <summary>Returns a defensive copy of immutable grant semantics, when installed.</summary>
    public AccessGrant? GetStaticGrant() => _staticGrant is null ? null : BasePolicyAuthorityCanonicalizer.CloneGrant(_staticGrant);
    /// <summary>Returns a defensive copy of the exact registration checksum.</summary>
    public byte[] GetChecksum() => _checksum.ToArray();
}

/// <summary>Provides the immutable BASE graph authority consumed by Studio registration.</summary>
public sealed class HPDBaseStudioAuthoritySnapshot
{
    private readonly byte[] _checksum;
    internal HPDBaseStudioAuthoritySnapshot(string applicationId, long policyOwnerGeneration,
        ReadOnlySpan<byte> policyOwnerChecksum, BaseLogicalSchema logicalSchema, IEnumerable<string> operationIds,
        IEnumerable<HPDBaseStudioPolicyAuthority> policies, IEnumerable<HPDBaseStudioGrantAuthority> grants, IEnumerable<HPDBaseStudioDefinitionAuthority> definitions,
        HPDBaseStoreProvider provider, HPDBaseStoreRegistrationReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(logicalSchema); ApplicationId = new string(applicationId.AsSpan());
        if (!StringComparer.Ordinal.Equals(logicalSchema.ApplicationId, applicationId)) throw new InvalidOperationException("base.studio.authorityInvalid");
        if (policyOwnerGeneration < 1 || policyOwnerChecksum.Length != 32)
            throw new InvalidOperationException("base.studio.authorityInvalid");
        PolicyOwnerGeneration = policyOwnerGeneration;
        _policyOwnerChecksum = policyOwnerChecksum.ToArray();
        RecordStoreRegistrationId = new string(receipt.RecordStoreRegistrationId.AsSpan()); ProviderId = new string(provider.Kind.AsSpan());
        ProviderVersion = provider.ProtocolVersion; ProviderGeneration = 1; SchemaDigest = new string(receipt.SchemaDigest.AsSpan());
        _providerCapabilityChecksum = provider.ProviderChecksum.ToArray();
        OperationIds = [.. operationIds.Select(static value => new string(value.AsSpan())).Order(StringComparer.Ordinal)];
        Definitions = [.. definitions.Select(static value => value with
            { Id = new(value.Id.AsSpan()), OwningModuleId = new(value.OwningModuleId.AsSpan()), DefinitionChecksum = [.. value.DefinitionChecksum] })
            .OrderBy(static value => (byte)value.Kind).ThenBy(static value => value.Id, StringComparer.Ordinal).ThenBy(static value => value.Version)];
        Grants = [.. grants.OrderBy(static value => value.Id, StringComparer.Ordinal).ThenBy(static value => value.Version)];
        Policies = [.. policies.Select(static value => value with
            { Id = new(value.Id.AsSpan()), OwningModuleId = new(value.OwningModuleId.AsSpan()), EvaluatorContractId = new(value.EvaluatorContractId.AsSpan()), RegistrationChecksum = [.. value.RegistrationChecksum] })
            .OrderBy(static value => value.CompositionOrder).ThenBy(static value => value.Id, StringComparer.Ordinal).ThenBy(static value => value.Version)];
        _collectionChecksums = logicalSchema.Collections.ToImmutableSortedDictionary(static value => value.Id,
            value => BaseLogicalSchemaFactory.InstalledCollectionChecksum(logicalSchema, value.Id), StringComparer.Ordinal);
        if (OperationIds.Any(static value => string.IsNullOrWhiteSpace(value))
            || OperationIds.Distinct(StringComparer.Ordinal).Count() != OperationIds.Length
            || Grants.Select(static value => (value.Id, value.Version)).Distinct().Count() != Grants.Length
            || Policies.Any(static value => string.IsNullOrWhiteSpace(value.Id) || value.Version < 1 || string.IsNullOrWhiteSpace(value.OwningModuleId) ||
                string.IsNullOrWhiteSpace(value.EvaluatorContractId) || value.EvaluatorContractVersion < 1 || value.CompositionOrder < 0 || value.RegistrationChecksum.Length != 32)
            || Policies.Select(static value => (value.Id, value.Version)).Distinct().Count() != Policies.Length
            || Definitions.Any(static value => !Enum.IsDefined(value.Kind) || string.IsNullOrWhiteSpace(value.Id) || value.Version < 1 ||
                string.IsNullOrWhiteSpace(value.OwningModuleId) || value.DefinitionChecksum.Length != 32)
            || Definitions.Select(static value => (value.Kind, value.Id, value.Version)).Distinct().Count() != Definitions.Length)
            throw new InvalidOperationException("base.studio.authorityInvalid");
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Add(hash, "base.studio.base-authority.v4"); Add(hash, ApplicationId); Add(hash, PolicyOwnerGeneration); Add(hash, _policyOwnerChecksum); Add(hash, OperationIds.Length);
        foreach (string operationId in OperationIds) Add(hash, operationId);
        Add(hash, Definitions.Length); foreach (HPDBaseStudioDefinitionAuthority definition in Definitions)
        { Add(hash, (int)definition.Kind); Add(hash, definition.Id); Add(hash, definition.Version); Add(hash, definition.OwningModuleId); Add(hash, definition.DefinitionChecksum.AsSpan()); }
        Add(hash, RecordStoreRegistrationId); Add(hash, receipt.Identity.ToByteArray()); Add(hash, ProviderId); Add(hash, ProviderVersion); Add(hash, ProviderGeneration); Add(hash, SchemaDigest);
        Add(hash, _providerCapabilityChecksum); Add(hash, receipt.RequiredRoles.Count); foreach (string role in receipt.RequiredRoles) Add(hash, role);
        Add(hash, receipt.ContributorIds.Count); foreach (string contributor in receipt.ContributorIds) Add(hash, contributor);
        Add(hash, _collectionChecksums.Count); foreach (var collection in _collectionChecksums) { Add(hash, collection.Key); Add(hash, collection.Value); }
        Add(hash, Grants.Length); foreach (HPDBaseStudioGrantAuthority grant in Grants)
        { Add(hash, grant.Id); Add(hash, grant.Version); Add(hash, grant.OwningModuleId); Add(hash, grant.SourceContractId);
          Add(hash, grant.SourceContractVersion); Add(hash, grant.GetChecksum()); }
        Add(hash, Policies.Length); foreach (HPDBaseStudioPolicyAuthority policy in Policies)
        { Add(hash, policy.Id); Add(hash, policy.Version); Add(hash, policy.OwningModuleId); Add(hash, policy.EvaluatorContractId);
          Add(hash, policy.EvaluatorContractVersion); Add(hash, policy.CompositionOrder); Add(hash, policy.RegistrationChecksum.AsSpan()); }
        _checksum = hash.GetHashAndReset();
    }
    /// <summary>Gets the owning BASE application identity.</summary>
    public string ApplicationId { get; }
    private readonly byte[] _policyOwnerChecksum;
    private readonly byte[] _providerCapabilityChecksum;
    private readonly ImmutableSortedDictionary<string, byte[]> _collectionChecksums;
    /// <summary>Gets the positive immutable L38 policy-owner generation.</summary>
    public long PolicyOwnerGeneration { get; }
    /// <summary>Gets the graph-owned record-store registration identity.</summary>
    public string RecordStoreRegistrationId { get; }
    /// <summary>Gets the frozen provider identity.</summary>
    public string ProviderId { get; }
    /// <summary>Gets the frozen provider version.</summary>
    public int ProviderVersion { get; }
    /// <summary>Gets the provider installation generation scoped to this immutable application graph.</summary>
    public long ProviderGeneration { get; }
    /// <summary>Gets the accepted store-registration schema digest.</summary>
    public string SchemaDigest { get; }
    /// <summary>Returns the full frozen provider-capability checksum.</summary>
    public byte[] GetProviderCapabilityChecksum() => _providerCapabilityChecksum.ToArray();
    /// <summary>Returns the exact immutable L38 policy-owner checksum.</summary>
    public byte[] GetPolicyOwnerChecksum() => _policyOwnerChecksum.ToArray();
    /// <summary>Gets exact installed Studio command identities in canonical ordinal order.</summary>
    public ImmutableArray<string> OperationIds { get; }
    /// <summary>Gets every exact installed operation and automation definition in canonical order.</summary>
    public ImmutableArray<HPDBaseStudioDefinitionAuthority> Definitions { get; }
    /// <summary>Gets installed grant registrations in canonical identity/version order.</summary>
    public ImmutableArray<HPDBaseStudioGrantAuthority> Grants { get; }
    /// <summary>Gets installed policy registrations in exact composition order.</summary>
    public ImmutableArray<HPDBaseStudioPolicyAuthority> Policies { get; }
    /// <summary>Returns a defensive copy of the snapshot checksum.</summary>
    public byte[] GetChecksum() => _checksum.ToArray();
    /// <summary>Returns whether one exact Studio command identity is installed.</summary>
    public bool HasOperation(string operationId) => OperationIds.Contains(operationId, StringComparer.Ordinal);

    /// <summary>Returns the exact Studio-owned frozen installed collection checksum, or <see langword="null"/> when absent.</summary>
    public byte[]? GetInstalledCollectionChecksum(string collectionId)
        => _collectionChecksums.TryGetValue(collectionId, out byte[]? value) ? value.ToArray() : null;

    private static void Add(IncrementalHash hash, string value) => Add(hash, Encoding.UTF8.GetBytes(value));
    private static void Add(IncrementalHash hash, int value)
    { Span<byte> bytes = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes, value); Add(hash, bytes); }
    private static void Add(IncrementalHash hash, long value)
    { Span<byte> bytes = stackalloc byte[8]; System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(bytes, value); Add(hash, bytes); }
    private static void Add(IncrementalHash hash, ReadOnlySpan<byte> value)
    { Span<byte> length = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, value.Length); hash.AppendData(length); hash.AppendData(value); }
}
