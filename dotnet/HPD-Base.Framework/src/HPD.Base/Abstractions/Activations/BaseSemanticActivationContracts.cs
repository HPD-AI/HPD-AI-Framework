using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Base;

/// <summary>Classifies one semantic activation operation carried by a module mutation.</summary>
public enum BaseSemanticActivationOperationKind
{
    /// <summary>Ensures that the logical semantic activation exists.</summary>
    Ensure = 1,
    /// <summary>Retires a terminal logical semantic activation.</summary>
    Retire = 2,
}

/// <summary>Classifies durable semantic-slot state.</summary>
public enum BaseSemanticActivationSlotState
{
    /// <summary>The slot maps to one live activation lifetime.</summary>
    Live = 1,
    /// <summary>The mapped activation was terminally retired.</summary>
    Retired = 2,
    /// <summary>Detailed retirement evidence was compacted into permanent absence authority.</summary>
    CompactedAbsent = 3,
}

/// <summary>Classifies the outcome of ensuring one semantic activation.</summary>
public enum BaseSemanticActivationEnsureDisposition
{
    /// <summary>The operation created the slot and activation.</summary>
    Created = 1,
    /// <summary>The operation resolved the existing live activation.</summary>
    Existing = 2,
    /// <summary>The identity is terminal and cannot be materialized again.</summary>
    Retired = 3,
}

/// <summary>Classifies the outcome of retiring one semantic activation.</summary>
public enum BaseSemanticActivationRetirementDisposition
{
    /// <summary>The operation retired the live slot.</summary>
    RetiredNow = 1,
    /// <summary>The slot was already retired.</summary>
    AlreadyRetired = 2,
    /// <summary>The slot already contains compacted absence authority.</summary>
    AlreadyCompacted = 3,
}

/// <summary>Classifies how the canonical activation due instant was obtained.</summary>
public enum BaseSemanticActivationDueMode
{
    /// <summary>BASE accepted the current provider time.</summary>
    AcceptedCurrentTime = 1,
    /// <summary>The installed operation supplied an explicit UTC instant.</summary>
    ExplicitUtcInstant = 2,
}

/// <summary>Identifies an installed semantic activation definition.</summary>
public sealed record BaseSemanticActivationDefinitionKey
{
    /// <summary>Gets the stable definition identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the positive definition version.</summary>
    public required int Version { get; init; }
    /// <summary>Gets the canonical 256-bit definition checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Contains installed semantic definition authority for one finalized graph.</summary>
public sealed record BaseSemanticActivationDefinitionIdentity
{
    /// <summary>Gets the stable definition identifier.</summary>
    public required string Id { get; init; }
    /// <summary>Gets the positive definition version.</summary>
    public required int Version { get; init; }
    /// <summary>Gets the canonical definition checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
    /// <summary>Gets the positive graph-owner generation.</summary>
    public required long OwnerGeneration { get; init; }
    /// <summary>Gets the exact owning module identity.</summary>
    public required string OwningModuleId { get; init; }
    /// <summary>Gets the exact installed operation authorized to retire this semantic activation.</summary>
    public required BaseSemanticActivationModuleOperationIdentity RetirementOperation { get; init; }
}

/// <summary>Owns a canonical semantic activation key digest.</summary>
[JsonConverter(typeof(BaseSemanticActivationKeyDigestJsonConverter))]
public sealed class BaseSemanticActivationKeyDigest : IEquatable<BaseSemanticActivationKeyDigest>
{
    /// <summary>Gets the exact digest length.</summary>
    public const int Length = 32;
    private readonly byte[] _bytes;
    private BaseSemanticActivationKeyDigest(byte[] bytes) => _bytes = bytes;
    /// <summary>Creates a deeply owned digest.</summary>
    public static BaseSemanticActivationKeyDigest Create(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != Length) throw new ArgumentException("A semantic activation key digest must contain exactly 32 bytes.", nameof(bytes));
        return new(bytes.ToArray());
    }
    /// <summary>Copies the digest into an exact-size or larger destination.</summary>
    public void CopyTo(Span<byte> destination)
    {
        if (destination.Length < Length) throw new ArgumentException("The destination is too small.", nameof(destination));
        _bytes.CopyTo(destination);
    }
    internal byte[] ToArray() => _bytes.ToArray();
    /// <inheritdoc />
    public bool Equals(BaseSemanticActivationKeyDigest? other) => other is not null && CryptographicOperations.FixedTimeEquals(_bytes, other._bytes);
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is BaseSemanticActivationKeyDigest other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => BitConverter.ToInt32(_bytes, 0);
}

internal sealed class BaseSemanticActivationKeyDigestJsonConverter : JsonConverter<BaseSemanticActivationKeyDigest>
{
    public override BaseSemanticActivationKeyDigest Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String)
            throw new JsonException("The semantic activation key digest must be a string.");
        string text = reader.GetString() ?? throw new JsonException("The semantic activation key digest is missing.");
        if (text.Length != 64 || text.Any(static value => !char.IsAsciiHexDigit(value) || char.IsUpper(value)))
            throw new JsonException("The semantic activation key digest is not canonical.");
        try { return BaseSemanticActivationKeyDigest.Create(Convert.FromHexString(text)); }
        catch (FormatException exception) { throw new JsonException("The semantic activation key digest is invalid.", exception); }
    }

    public override void Write(Utf8JsonWriter writer, BaseSemanticActivationKeyDigest value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer); ArgumentNullException.ThrowIfNull(value);
        Span<byte> bytes = stackalloc byte[BaseSemanticActivationKeyDigest.Length]; value.CopyTo(bytes);
        writer.WriteStringValue(Convert.ToHexStringLower(bytes));
    }
}

/// <summary>Contains the canonical due authority stored in one semantic slot.</summary>
public sealed record BaseSemanticActivationDueAuthority
{
    /// <summary>Gets how the instant was selected.</summary>
    public required BaseSemanticActivationDueMode Mode { get; init; }
    /// <summary>Gets the canonical Unix-millisecond instant.</summary>
    public required long CanonicalUnixMilliseconds { get; init; }
}

/// <summary>Contains the complete exported-subject lifetime bound to a semantic identity.</summary>
public sealed record BaseSemanticActivationSubjectLifetimeBinding
{
    /// <summary>Gets the exported-subject contract identifier.</summary>
    public required string ContractId { get; init; }
    /// <summary>Gets the positive exported-subject contract version.</summary>
    public required int ContractVersion { get; init; }
    /// <summary>Gets the exported-subject contract checksum.</summary>
    public required ImmutableArray<byte> ContractChecksum { get; init; }
    /// <summary>Gets the canonical subject identifier.</summary>
    public required BaseSubjectId SubjectId { get; init; }
    /// <summary>Gets the BASE-owned authority epoch.</summary>
    public required BaseSubjectAuthorityEpoch AuthorityEpoch { get; init; }
    /// <summary>Gets the subject incarnation within the authority epoch.</summary>
    public required BaseSubjectIncarnation Incarnation { get; init; }
    /// <summary>Gets the stable semantic scope-binding identifier.</summary>
    public required ImmutableArray<byte> ScopeBindingId { get; init; }
    /// <summary>Gets the canonical binding checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Contains one Runtime-finalized semantic activation creation.</summary>
public sealed record BaseSemanticActivationCreateIntent
{
    /// <summary>Gets the installed activation definition.</summary>
    public required BaseActivationDefinitionKey Definition { get; init; }
    /// <summary>Gets canonical activation input bytes.</summary>
    public required ImmutableArray<byte> CanonicalInput { get; init; }
    /// <summary>Gets the canonical input checksum.</summary>
    public required ImmutableArray<byte> InputChecksum { get; init; }
    /// <summary>Gets protected scope authority.</summary>
    public required BaseOwnedSubjectScopeEvidence Scope { get; init; }
    /// <summary>Gets canonical due authority.</summary>
    public required BaseSemanticActivationDueAuthority Due { get; init; }
    /// <summary>Gets the declared priority.</summary>
    public required int Priority { get; init; }
    /// <summary>Gets whether the activation is initially eligible.</summary>
    public required bool InitiallyEligible { get; init; }
    /// <summary>Gets the complete Runtime-owned semantic creation identity.</summary>
    public required BaseSemanticActivationCreationIdentity Identity { get; init; }
    /// <summary>Gets the exact installed L51 limits applied to this creation.</summary>
    public required BaseActivationLimits Limits { get; init; }
}

/// <summary>
/// Provides the canonical, provider-neutral checksum encoders for semantic activation evidence.
/// </summary>
/// <remarks>
/// Providers use these methods to author evidence returned through <see cref="IAtomicRecordSession"/>.
/// Runtime independently invokes the same canonical contract when validating hostile provider results.
/// All returned byte sequences are newly owned immutable values.
/// </remarks>
public static class BaseSemanticActivationEvidenceContract
{
    /// <summary>Creates a deeply owned, canonically checksummed scope binding.</summary>
    public static BaseSemanticActivationScopeBinding CreateScopeBinding(
        BaseSubjectScopeKind kind,
        ReadOnlySpan<byte> bindingId,
        ReadOnlySpan<byte> protectedCanonicalScope,
        ReadOnlySpan<byte> seekDigest,
        string protectionKeyId,
        int protectionKeyVersion)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(protectionKeyId);
        var value = new BaseSemanticActivationScopeBinding
        {
            Kind = kind,
            BindingId = bindingId.ToArray().ToImmutableArray(),
            ProtectedCanonicalScope = protectedCanonicalScope.ToArray().ToImmutableArray(),
            SeekDigest = seekDigest.ToArray().ToImmutableArray(),
            ProtectionKeyId = new string(protectionKeyId.AsSpan()),
            ProtectionKeyVersion = protectionKeyVersion,
            Checksum = [],
        };
        return value with { Checksum = ScopeBindingChecksum(value) };
    }

    /// <summary>Computes the canonical checksum for a scope binding.</summary>
    public static ImmutableArray<byte> ScopeBindingChecksum(BaseSemanticActivationScopeBinding value) =>
        Hash("base.semanticActivation.scopeBinding.v1\0", Int32((int)value.Kind), value.BindingId.ToArray(),
            value.ProtectedCanonicalScope.ToArray(), value.SeekDigest.ToArray(),
            System.Text.Encoding.UTF8.GetBytes(value.ProtectionKeyId), Int32(value.ProtectionKeyVersion));

    /// <summary>Computes the canonical checksum for a captured scope-directory binding.</summary>
    public static ImmutableArray<byte> ScopeDirectoryChecksum(BaseSemanticActivationScopeBinding value) =>
        System.Security.Cryptography.SHA256.HashData(value.Checksum.AsSpan()).ToImmutableArray();

    /// <summary>Creates deeply owned, canonically checksummed store authority.</summary>
    public static BaseSemanticActivationStoreAuthority CreateStoreAuthority(BaseSemanticActivationStoreAuthorityRequirement requirement)
    {
        var owned = requirement with { DefinitionSetChecksum = requirement.DefinitionSetChecksum.ToArray().ToImmutableArray() };
        return new BaseSemanticActivationStoreAuthority { Requirement = owned, Checksum = StoreAuthorityChecksum(owned) };
    }

    /// <summary>Computes the canonical checksum for captured store authority.</summary>
    public static ImmutableArray<byte> StoreAuthorityChecksum(BaseSemanticActivationStoreAuthorityRequirement value) =>
        Hash("base.semanticActivation.storeAuthority.v1\0", System.Text.Encoding.UTF8.GetBytes(value.ApplicationId),
            System.Text.Encoding.UTF8.GetBytes(value.LogicalStoreId), System.Text.Encoding.UTF8.GetBytes(value.StoreInstanceId),
            Int64(value.RestoreEpoch), Int64(value.SchemaGeneration), Int64(value.SemanticAuthorityGeneration),
            value.DefinitionSetChecksum.ToArray());

    /// <summary>Computes exact missing-slot access authority from the canonical interval bound.</summary>
    public static ImmutableArray<byte> MissingAccessPathChecksum(ReadOnlySpan<byte> canonicalSlotBound) =>
        System.Security.Cryptography.SHA256.HashData(canonicalSlotBound).ToImmutableArray();

    /// <summary>Computes the canonical checksum for one exported-subject lifetime binding.</summary>
    public static ImmutableArray<byte> SubjectLifetimeChecksum(BaseSemanticActivationSubjectLifetimeBinding value) =>
        Hash("base.semanticActivation.subjectLifetime.v1\0", System.Text.Encoding.UTF8.GetBytes(value.ContractId),
            Int32(value.ContractVersion), value.ContractChecksum.ToArray(), value.SubjectId.ToUtf8Bytes(),
            System.Text.Encoding.UTF8.GetBytes(value.AuthorityEpoch.ToBase64Url()),
            System.Text.Encoding.UTF8.GetBytes(value.Incarnation.ToBase64Url()), value.ScopeBindingId.ToArray());

    /// <summary>Computes the canonical corruption-detection checksum for retained semantic receipt authority.</summary>
    public static ImmutableArray<byte> RecoveryReceiptChecksum(string scope, string operation, string key,
        ReadOnlySpan<byte> fingerprint, ReadOnlySpan<byte> structuralDigest, ReadOnlySpan<byte> resultJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope); ArgumentException.ThrowIfNullOrWhiteSpace(operation);
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        return Hash("base.semanticActivation.recoveryReceipt.v1\0", System.Text.Encoding.UTF8.GetBytes(scope),
            System.Text.Encoding.UTF8.GetBytes(operation), System.Text.Encoding.UTF8.GetBytes(key),
            fingerprint.ToArray(), structuralDigest.ToArray(), resultJson.ToArray());
    }

    /// <summary>Computes the canonical checksum for one live semantic-slot authority.</summary>
    public static ImmutableArray<byte> LiveChecksum(BaseSemanticActivationLiveAuthority value)
    {
        Span<byte> key = stackalloc byte[BaseSemanticActivationKeyDigest.Length]; value.KeyDigest.CopyTo(key);
        return Hash("base.semanticActivation.live.v1\0",
            System.Text.Encoding.UTF8.GetBytes(value.Definition.Id), Int64(value.Definition.Version), value.Definition.Checksum.ToArray(),
            Int64(value.Definition.OwnerGeneration), System.Text.Encoding.UTF8.GetBytes(value.Definition.OwningModuleId), key.ToArray(),
            Scope(value.Scope), value.ScopeBinding.Checksum.ToArray(), Lifetime(value.SubjectLifetime),
            System.Text.Encoding.UTF8.GetBytes(value.ActivationId), System.Text.Encoding.UTF8.GetBytes(value.ActivationDefinition.Id),
            Int64(value.ActivationDefinition.Version), value.ActivationDefinition.Checksum.ToArray(), value.InputChecksum.ToArray(),
            [(byte)value.Due.Mode], Int64(value.Due.CanonicalUnixMilliseconds), Int64(value.SlotGeneration),
            value.StoreAuthority.Checksum.ToArray());
    }

    /// <summary>Computes the canonical checksum for one retired semantic-slot authority.</summary>
    public static ImmutableArray<byte> RetirementChecksum(BaseSemanticActivationRetirementAuthority value)
    {
        Span<byte> key = stackalloc byte[BaseSemanticActivationKeyDigest.Length]; value.KeyDigest.CopyTo(key);
        return Hash("base.semanticActivation.retired.v1\0", System.Text.Encoding.UTF8.GetBytes(value.Definition.Id),
            Int64(value.Definition.Version), value.Definition.Checksum.ToArray(), key.ToArray(), Lifetime(value.SubjectLifetime),
            System.Text.Encoding.UTF8.GetBytes(value.ActivationId), [(byte)value.TerminalState], Int64(value.TerminalActivationGeneration),
            value.TerminalActivationChecksum.ToArray(), value.CompletionOperationChecksum.ToArray(), value.CompletionReceiptChecksum.ToArray(),
            Int64(value.RetirementPosition), Int64(value.SlotGeneration), value.StoreAuthority.Checksum.ToArray());
    }

    /// <summary>Computes the canonical checksum for one compacted-absence authority.</summary>
    public static ImmutableArray<byte> AbsenceChecksum(BaseSemanticActivationAbsenceAuthority value)
    {
        Span<byte> key = stackalloc byte[BaseSemanticActivationKeyDigest.Length]; value.Key.CopyTo(key);
        return Hash("base.semanticActivation.absent.v1\0", key.ToArray(), System.Text.Encoding.UTF8.GetBytes(value.Definition.Id),
            Int64(value.Definition.Version), value.Definition.Checksum.ToArray(), Int64(value.Definition.OwnerGeneration),
            System.Text.Encoding.UTF8.GetBytes(value.Definition.OwningModuleId), value.ScopeBindingId.ToArray(), Lifetime(value.SubjectLifetime),
            Int64(value.FinalSlotGeneration), Int64(value.AbsenceFloorGeneration), Int64(value.RetirementPosition),
            value.StoreAuthority.Checksum.ToArray());
    }

    /// <summary>Computes the canonical checksum for captured semantic evidence.</summary>
    public static ImmutableArray<byte> CapturedChecksum(BaseAtomicSemanticActivationExtension extension, BaseCapturedSemanticActivationEvidence value)
    {
        var fields = new List<byte[]>
        {
            extension.StructuralDigest.ToArray(), new byte[] { (byte)value.State }, value.ScopeDirectory.Checksum.ToArray(),
            value.Missing?.AccessPathChecksum.ToArray() ?? [], value.Live?.Checksum.ToArray() ?? [],
            value.Retired?.Checksum.ToArray() ?? [], value.Absent?.Checksum.ToArray() ?? [],
            Int64(value.ActivationGeneration ?? 0), value.ActivationChecksum.ToArray(), value.ActivationTerminalReceiptChecksum.ToArray(),
            new byte[] { value.ActivationState is null ? (byte)0 : (byte)value.ActivationState.Value },
            value.AcceptedTime.Checksum.ToArray(),
        };
        foreach (BaseAtomicReadIntervalEvidence interval in value.ReadIntervals)
        {
            fields.Add(System.Text.Encoding.UTF8.GetBytes(interval.LogicalAccessPathId)); fields.Add(interval.CanonicalLowerBound.ToArray());
            fields.Add([interval.LowerInclusive ? (byte)1 : (byte)0]); fields.Add(interval.CanonicalUpperBound.ToArray());
            fields.Add([interval.UpperInclusive ? (byte)1 : (byte)0]);
        }
        fields.Add(Accounting(value.Accounting));
        return Hash("base.semanticActivation.captured.v1\0", [.. fields]);
    }

    /// <summary>Computes the canonical checksum for one semantic write interval.</summary>
    public static ImmutableArray<byte> WriteIntervalChecksum(BaseSemanticActivationWriteIntervalEvidence value) =>
        Hash("base.semanticActivation.writeInterval.v1\0", System.Text.Encoding.UTF8.GetBytes(value.AccessPathId),
            value.Lower.ToArray(), [value.LowerInclusive ? (byte)1 : (byte)0], value.Upper.ToArray(), [value.UpperInclusive ? (byte)1 : (byte)0]);

    /// <summary>Computes the canonical checksum for prepared semantic evidence.</summary>
    public static ImmutableArray<byte> PreparedChecksum(BaseAtomicSemanticActivationExtension extension, BasePreparedSemanticActivation value)
    {
        var fields = new List<byte[]>
        {
            extension.StructuralDigest.ToArray(), new byte[] { (byte)value.Operation }, new byte[] { (byte)value.PriorState }, new byte[] { (byte)value.ResultingState },
            Int64(value.ResultingSlotGeneration), System.Text.Encoding.UTF8.GetBytes(value.ResultingActivationId ?? string.Empty),
        };
        foreach (BaseSemanticActivationWriteIntervalEvidence interval in value.WriteIntervals) fields.Add(interval.Checksum.ToArray());
        fields.Add(Accounting(value.Accounting));
        return Hash("base.semanticActivation.prepared.v1\0", [.. fields]);
    }

    /// <summary>Computes the canonical checksum for provisional semantic evidence.</summary>
    public static ImmutableArray<byte> ProvisionalChecksum(BasePreparedSemanticActivation prepared, BaseProvisionalSemanticActivation value) =>
        Hash("base.semanticActivation.provisional.v1\0", prepared.Checksum.ToArray(), [(byte)value.Operation], [(byte)value.PriorState],
            [(byte)value.ResultingState], Int64(value.ResultingSlotGeneration), value.ResultingSlotChecksum.ToArray(), System.Text.Encoding.UTF8.GetBytes(value.ActivationId ?? string.Empty),
            Int64(value.ActivationGeneration ?? 0), value.ActivationChecksum.ToArray(), Int64(value.CommitJournalPosition), Accounting(value.Accounting));

    /// <summary>Computes canonical semantic receipt evidence, including any durable external handoff.</summary>
    public static ImmutableArray<byte> ReceiptChecksum(BaseSemanticActivationReceiptEvidence value)
    {
        Span<byte> key = stackalloc byte[BaseSemanticActivationKeyDigest.Length]; value.Key.CopyTo(key);
        return value.RecoveryPublication is null
            ? Hash("base.semanticActivation.receipt.v1\0", value.DefinitionChecksum.ToArray(), key.ToArray(),
                value.SlotChecksum.ToArray(), value.CommitEvidenceChecksum.ToArray())
            : Hash("base.semanticActivation.receipt.v1\0", value.DefinitionChecksum.ToArray(), key.ToArray(),
                value.SlotChecksum.ToArray(), value.CommitEvidenceChecksum.ToArray(), value.RecoveryPublication.Checksum.ToArray());
    }

    /// <summary>Computes the canonical checksum for read-only external recovery preflight evidence.</summary>
    public static ImmutableArray<byte> RecoveryPreflightChecksum(BaseSemanticRecoveryPreflightRequest request, BaseSemanticRecoveryPreflightEvidence value)
    {
        Span<byte> key = stackalloc byte[BaseSemanticActivationKeyDigest.Length]; value.Key.CopyTo(key);
        var fields = new List<byte[]>
        {
            request.Definition.Checksum.ToArray(), request.CanonicalKey.ToArray(), request.KeyPreimageChecksum.ToArray(), Scope(request.Scope),
            RecoveryLifetime(request.SubjectLifetime), Int32(request.MaximumCanonicalKeyBytes),
            request.StoreAuthority.DefinitionSetChecksum.ToArray(), Int64(request.Deadline.Ticks),
            value.ScopeBinding.Checksum.ToArray(), key.ToArray(), value.Live.Checksum.ToArray(),
            Int64(value.ActivationGeneration), new byte[] { (byte)value.ActivationState }, value.ActivationChecksum.ToArray(),
            value.ActivationTerminalReceiptChecksum.ToArray(), System.Text.Encoding.UTF8.GetBytes(value.TerminalReceipt.ReceiptKey),
            System.Text.Encoding.UTF8.GetBytes(value.TerminalReceipt.OperationKind), value.TerminalReceipt.Fingerprint.ToArray(),
            value.TerminalReceipt.ResultBytes.ToArray(), value.TerminalReceipt.ResultChecksum.ToArray(), value.TerminalReceipt.AuthorityChecksum.ToArray(),
        };
        foreach (BaseAtomicReadIntervalEvidence interval in value.ReadIntervals)
        {
            fields.Add(System.Text.Encoding.UTF8.GetBytes(interval.LogicalAccessPathId)); fields.Add(interval.CanonicalLowerBound.ToArray());
            fields.Add([interval.LowerInclusive ? (byte)1 : (byte)0]); fields.Add(interval.CanonicalUpperBound.ToArray());
            fields.Add([interval.UpperInclusive ? (byte)1 : (byte)0]);
        }
        fields.Add(Accounting(value.Accounting));
        return Hash("base.semanticRecovery.preflight.v1\0", [.. fields]);
    }

    /// <summary>Validates complete read-only recovery preflight authority before external influence.</summary>
    public static bool RecoveryPreflightIsValid(
        BaseSemanticRecoveryPreflightRequest request,
        BaseSemanticRecoveryPreflightEvidence value)
    {
        try
        {
            if (request.MaximumCanonicalKeyBytes <= 0 || request.CanonicalKey.IsDefaultOrEmpty
                || request.CanonicalKey.Length > request.MaximumCanonicalKeyBytes || request.KeyPreimageChecksum.Length != 32
                || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                    System.Security.Cryptography.SHA256.HashData(request.CanonicalKey.AsSpan()), request.KeyPreimageChecksum.AsSpan())
                || request.Deadline <= TimeSpan.Zero || value.ScopeBinding.BindingId.Length != 32
                || !RecoveryLifetimeValid(request.SubjectLifetime)
                || value.ScopeBinding.Kind != request.Scope.Kind || value.ScopeBinding.Checksum.Length != 32
                || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                    ScopeBindingChecksum(value.ScopeBinding).AsSpan(), value.ScopeBinding.Checksum.AsSpan())
                || value.Live.Definition.Id != request.Definition.Id || value.Live.Definition.Version != request.Definition.Version
                || value.Live.Definition.OwnerGeneration != request.Definition.OwnerGeneration
                || value.Live.Definition.OwningModuleId != request.Definition.OwningModuleId
                || !value.Live.Definition.Checksum.AsSpan().SequenceEqual(request.Definition.Checksum.AsSpan())
                || value.Live.Scope.Kind != request.Scope.Kind || value.Live.Scope.Value != request.Scope.Value
                || !ScopeBindingEqual(value.Live.ScopeBinding, value.ScopeBinding)
                || !StoreEqual(value.Live.StoreAuthority.Requirement, request.StoreAuthority)
                || value.Live.SlotGeneration <= 0 || value.Live.Checksum.Length != 32
                || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                    LiveChecksum(value.Live).AsSpan(), value.Live.Checksum.AsSpan())
                || !LifetimeMatchesPreflight(value.Live.SubjectLifetime, request.SubjectLifetime, value.ScopeBinding.BindingId)
                || value.ActivationGeneration <= 0 || !Terminal(value.ActivationState)
                || value.ActivationChecksum.Length != 32 || value.ActivationTerminalReceiptChecksum.Length != 32
                || !TerminalReceiptValid(value)
                || value.ReadIntervals.Length != 3 || value.Accounting.Operations != 1
                || value.Accounting.ScopeDirectoryReads != 1 || value.Accounting.SlotReads != 1
                || value.Accounting.ActivationReads != 1 || value.Accounting.ReadIntervals != 3
                || value.Accounting.Operations > request.Limits.MaximumOperations
                || value.Accounting.ScopeDirectoryReads > request.Limits.MaximumScopeDirectoryReads
                || value.Accounting.SlotReads > request.Limits.MaximumSlotReads
                || value.Accounting.ActivationReads > request.Limits.MaximumActivationReads
                || value.Accounting.ReadIntervals > request.Limits.MaximumReadIntervals
                || value.Accounting.IndexOperations > request.Limits.MaximumIndexOperations
                || value.Accounting.ActivationBytes > request.Limits.MaximumActivationBytes
                || value.Accounting.ScopeDirectoryBytes > request.Limits.MaximumScopeDirectoryBytes
                || value.Accounting.EvidenceBytes > request.Limits.MaximumEvidenceBytes
                || value.Accounting.ReceiptBytes > request.Limits.MaximumReceiptBytes
                || value.Accounting.TransientBytes > request.Limits.MaximumTransientBytes
                || value.Accounting != RecoveryPreflightAccounting(request, value)
                || value.Checksum.Length != 32)
                return false;
            Span<byte> key = stackalloc byte[BaseSemanticActivationKeyDigest.Length]; value.Key.CopyTo(key);
            ImmutableArray<byte> expectedKey = Hash("base.semanticActivation.key.v1\0", System.Text.Encoding.UTF8.GetBytes(request.Definition.Id),
                value.ScopeBinding.BindingId.ToArray(), request.CanonicalKey.ToArray());
            ImmutableArray<byte> expectedActivationId = Hash("base.semanticActivation.activation.v1\0",
                System.Text.Encoding.UTF8.GetBytes(request.StoreAuthority.ApplicationId),
                System.Text.Encoding.UTF8.GetBytes(request.StoreAuthority.LogicalStoreId),
                System.Text.Encoding.UTF8.GetBytes(request.Definition.OwningModuleId),
                System.Text.Encoding.UTF8.GetBytes(request.Definition.Id), value.ScopeBinding.BindingId.ToArray(), request.CanonicalKey.ToArray());
            byte[] expectedControl = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
                $"base.activation.control.v2\0{value.Live.ActivationId}\n{value.ActivationGeneration}\n{(int)value.ActivationState}"));
            byte[] scopeBound = System.Text.Encoding.UTF8.GetBytes($"{(int)request.Scope.Kind}\n{Convert.ToHexString(value.ScopeBinding.SeekDigest.AsSpan())}");
            byte[] slotBound = System.Text.Encoding.UTF8.GetBytes($"{request.Definition.Id}\n{Convert.ToHexString(value.ScopeBinding.BindingId.AsSpan())}\n{Convert.ToHexString(key)}");
            return key.SequenceEqual(expectedKey.AsSpan())
                && string.Equals(value.Live.ActivationId, Convert.ToHexStringLower(expectedActivationId.AsSpan()), StringComparison.Ordinal)
                && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(expectedControl, value.ActivationChecksum.AsSpan())
                && PreflightInterval(value.ReadIntervals[0], "base.semanticActivation.scope", scopeBound)
                && PreflightInterval(value.ReadIntervals[1], "base.semanticActivation.slot", slotBound)
                && PreflightInterval(value.ReadIntervals[2], "base.activation.byId", System.Text.Encoding.UTF8.GetBytes(value.Live.ActivationId))
                && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                    RecoveryPreflightChecksum(request, value).AsSpan(), value.Checksum.AsSpan());
        }
        catch { return false; }
    }

    private static bool PreflightInterval(BaseAtomicReadIntervalEvidence value, string path, ReadOnlySpan<byte> bound) =>
        string.Equals(value.LogicalAccessPathId, path, StringComparison.Ordinal) && value.LowerInclusive && value.UpperInclusive
        && value.CanonicalLowerBound.AsSpan().SequenceEqual(bound) && value.CanonicalUpperBound.AsSpan().SequenceEqual(bound);

    private static bool ScopeBindingEqual(BaseSemanticActivationScopeBinding left, BaseSemanticActivationScopeBinding right) =>
        left.Kind == right.Kind && left.ProtectionKeyVersion == right.ProtectionKeyVersion
        && left.ProtectionKeyId == right.ProtectionKeyId && left.BindingId.AsSpan().SequenceEqual(right.BindingId.AsSpan())
        && left.ProtectedCanonicalScope.AsSpan().SequenceEqual(right.ProtectedCanonicalScope.AsSpan())
        && left.SeekDigest.AsSpan().SequenceEqual(right.SeekDigest.AsSpan())
        && left.Checksum.AsSpan().SequenceEqual(right.Checksum.AsSpan());

    private static bool StoreEqual(BaseSemanticActivationStoreAuthorityRequirement left, BaseSemanticActivationStoreAuthorityRequirement right) =>
        left.ApplicationId == right.ApplicationId && left.LogicalStoreId == right.LogicalStoreId
        && left.StoreInstanceId == right.StoreInstanceId && left.RestoreEpoch == right.RestoreEpoch
        && left.SchemaGeneration == right.SchemaGeneration && left.SemanticAuthorityGeneration == right.SemanticAuthorityGeneration
        && left.DefinitionSetChecksum.AsSpan().SequenceEqual(right.DefinitionSetChecksum.AsSpan());

    private static bool LifetimeMatchesPreflight(BaseSemanticActivationSubjectLifetimeBinding? actual,
        BaseSemanticRecoverySubjectLifetimePreimage? requested, ImmutableArray<byte> binding)
    {
        if (actual is null || requested is null) return actual is null && requested is null;
        return actual.ContractId == requested.ContractId && actual.ContractVersion == requested.ContractVersion
            && actual.ContractChecksum.AsSpan().SequenceEqual(requested.ContractChecksum.AsSpan())
            && actual.SubjectId.Value == requested.SubjectId.Value
            && actual.AuthorityEpoch.ToBase64Url() == requested.AuthorityEpoch.ToBase64Url()
            && actual.Incarnation.ToBase64Url() == requested.Incarnation.ToBase64Url()
            && actual.ScopeBindingId.AsSpan().SequenceEqual(binding.AsSpan())
            && actual.Checksum.Length == 32 && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                SubjectLifetimeChecksum(actual).AsSpan(), actual.Checksum.AsSpan());
    }

    private static bool RecoveryLifetimeValid(BaseSemanticRecoverySubjectLifetimePreimage? value) => value is null
        || !string.IsNullOrWhiteSpace(value.ContractId) && value.ContractVersion > 0 && value.ContractChecksum.Length == 32
        && !string.IsNullOrWhiteSpace(value.SubjectId.Value)
        && value.AuthorityEpoch.ToArray().Length == 16 && value.Incarnation.ToArray().Length == 24;

    private static bool Terminal(BaseActivationState state) => state is BaseActivationState.Succeeded
        or BaseActivationState.Exhausted or BaseActivationState.Cancelled or BaseActivationState.Migrated or BaseActivationState.Disposed;

    /// <summary>Recomputes exact provider-neutral logical accounting for one recovery preflight.</summary>
    public static BaseSemanticActivationAccounting RecoveryPreflightAccounting(
        BaseSemanticRecoveryPreflightRequest request, BaseSemanticRecoveryPreflightEvidence value)
    {
        long intervalBytes = checked(value.ReadIntervals.Sum(static interval => (long)System.Text.Encoding.UTF8.GetByteCount(interval.LogicalAccessPathId)
            + interval.CanonicalLowerBound.Length + interval.CanonicalUpperBound.Length + 2));
        long scopeBytes = checked(value.ScopeBinding.BindingId.Length + value.ScopeBinding.ProtectedCanonicalScope.Length
            + value.ScopeBinding.SeekDigest.Length + System.Text.Encoding.UTF8.GetByteCount(value.ScopeBinding.ProtectionKeyId)
            + sizeof(int) + value.ScopeBinding.Checksum.Length);
        long activationBytes = checked(System.Text.Encoding.UTF8.GetByteCount(value.Live.ActivationId) + sizeof(long) + sizeof(int)
            + value.ActivationChecksum.Length + value.ActivationTerminalReceiptChecksum.Length);
        long receiptBytes = checked(System.Text.Encoding.UTF8.GetByteCount(value.TerminalReceipt.ReceiptKey)
            + System.Text.Encoding.UTF8.GetByteCount(value.TerminalReceipt.OperationKind) + value.TerminalReceipt.Fingerprint.Length
            + value.TerminalReceipt.ResultBytes.Length + value.TerminalReceipt.ResultChecksum.Length + value.TerminalReceipt.AuthorityChecksum.Length);
        long evidenceBytes = checked(intervalBytes + value.ScopeBinding.Checksum.Length + value.Live.Checksum.Length
            + value.ActivationChecksum.Length + value.ActivationTerminalReceiptChecksum.Length);
        long liveBytes = checked(value.Live.Definition.Checksum.Length + value.Live.ScopeBinding.Checksum.Length
            + (value.Live.SubjectLifetime?.Checksum.Length ?? 0) + System.Text.Encoding.UTF8.GetByteCount(value.Live.ActivationId)
            + value.Live.ActivationDefinition.Checksum.Length + value.Live.InputChecksum.Length + value.Live.StoreAuthority.Checksum.Length
            + sizeof(long) * 2 + 2);
        return new BaseSemanticActivationAccounting
        {
            Operations = 1, ScopeDirectoryReads = 1, SlotReads = 1, ActivationReads = 1, ReadIntervals = 3,
            IndexOperations = 3, KeyBytes = request.CanonicalKey.Length, ScopeDirectoryBytes = scopeBytes,
            ActivationBytes = activationBytes, EvidenceBytes = evidenceBytes, ReceiptBytes = receiptBytes,
            TransientBytes = checked(request.CanonicalKey.Length + scopeBytes + liveBytes + activationBytes + receiptBytes + evidenceBytes),
            ActivationCreation = new BaseActivationAccounting
            {
                Candidates = 0, Comparisons = 0, IndexOperations = 0, ReadIntervals = 0, EvidenceBytes = 0, TransientBytes = 0,
            },
        };
    }

    /// <summary>Validates byte-exact transaction recapture of an earlier read-only recovery preflight.</summary>
    public static bool RecoveryPreflightMatchesCapture(
        BaseSemanticRecoveryPreflightEvidence preflight,
        BaseCapturedSemanticActivationEvidence captured)
    {
        try
        {
            return captured.State == BaseSemanticActivationCapturedState.Live && captured.Live is { } live
                && ScopeBindingEqual(preflight.ScopeBinding, captured.ScopeDirectory.ResultingBinding)
                && preflight.Key.Equals(live.KeyDigest) && preflight.Key.Equals(captured.Live.KeyDigest)
                && preflight.Live.Checksum.AsSpan().SequenceEqual(live.Checksum.AsSpan())
                && preflight.ActivationGeneration == captured.ActivationGeneration
                && preflight.ActivationState == captured.ActivationState
                && preflight.ActivationChecksum.AsSpan().SequenceEqual(captured.ActivationChecksum.AsSpan())
                && preflight.ActivationTerminalReceiptChecksum.AsSpan().SequenceEqual(captured.ActivationTerminalReceiptChecksum.AsSpan())
                && preflight.ScopeBinding.Checksum.AsSpan().SequenceEqual(captured.ScopeDirectory.ResultingBinding.Checksum.AsSpan());
        }
        catch { return false; }
    }

    /// <summary>Validates transaction-bound pending authority against the exact recaptured slot.</summary>
    public static bool RecoveryPendingMatchesCapture(BaseSemanticActivationCaptureRequest request,
        BaseCapturedSemanticActivationEvidence captured)
    {
        try
        {
            BaseSemanticRecoveryPendingCommitAuthority? value = request.RecoveryPending;
            if (value is null) return request.RecoveryPreflight is null;
            if (request.RecoveryPreflight is null || request.Operation != BaseSemanticActivationOperationKind.Retire
                || captured.Live is null || !RecoveryPreflightMatchesCapture(request.RecoveryPreflight, captured)
                || value.AuthorityVersion <= 0 || string.IsNullOrWhiteSpace(value.AuthorityId) || value.AuthorityChecksum.Length != 32
                || value.Intent.Boundary.DefinitionId != request.Definition.Id
                || !value.Intent.Boundary.ScopeBindingId.AsSpan().SequenceEqual(captured.ScopeDirectory.ResultingBinding.BindingId.AsSpan())
                || !value.Intent.Boundary.Key.Equals(captured.Live.KeyDigest)
                || value.Intent.RetirementOperationFingerprint.Length != 32
                || !LifetimeAuthorityEqual(value.Intent.SubjectLifetime, captured.Live.SubjectLifetime)
                || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                    BaseSemanticRecoveryAuthorityContract.PendingIntentChecksum(value.Intent).AsSpan(), value.Intent.Checksum.AsSpan())
                || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(value.Intent.Checksum.AsSpan(), value.Pending.IntentChecksum.AsSpan())
                || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                    BaseSemanticRecoveryAuthorityContract.PendingChecksum(value.Pending).AsSpan(), value.Pending.Checksum.AsSpan())
                || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                    BaseSemanticRecoveryAuthorityContract.PendingCommitChecksum(value).AsSpan(), value.Checksum.AsSpan())) return false;
            return true;
        }
        catch { return false; }
    }

    private static bool LifetimeAuthorityEqual(BaseSemanticActivationSubjectLifetimeBinding? left,
        BaseSemanticActivationSubjectLifetimeBinding? right)
    {
        if (left is null || right is null) return left is null && right is null;
        return left.ContractId == right.ContractId && left.ContractVersion == right.ContractVersion
            && left.ContractChecksum.AsSpan().SequenceEqual(right.ContractChecksum.AsSpan())
            && left.SubjectId.Equals(right.SubjectId) && left.AuthorityEpoch.Equals(right.AuthorityEpoch)
            && left.Incarnation.Equals(right.Incarnation) && left.ScopeBindingId.AsSpan().SequenceEqual(right.ScopeBindingId.AsSpan())
            && left.Checksum.AsSpan().SequenceEqual(right.Checksum.AsSpan());
    }

    private static bool TerminalReceiptValid(BaseSemanticRecoveryPreflightEvidence value)
    {
        BaseSemanticRecoveryTerminalReceiptEvidence receipt = value.TerminalReceipt;
        if (string.IsNullOrWhiteSpace(receipt.ReceiptKey) || string.IsNullOrWhiteSpace(receipt.OperationKind)
            || receipt.Fingerprint.Length != 32 || receipt.ResultBytes.IsDefaultOrEmpty
            || receipt.ResultChecksum.Length != 32 || receipt.AuthorityChecksum.Length != 32
            || !receipt.AuthorityChecksum.AsSpan().SequenceEqual(value.ActivationTerminalReceiptChecksum.AsSpan())
            || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                System.Security.Cryptography.SHA256.HashData(receipt.ResultBytes.AsSpan()), receipt.ResultChecksum.AsSpan())) return false;
        byte[] expectedAuthority = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(receipt.OperationKind).Concat(receipt.Fingerprint).Concat(receipt.ResultBytes).ToArray());
        if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(expectedAuthority, receipt.AuthorityChecksum.AsSpan())) return false;
        BaseActivationTransitionResult? result = System.Text.Json.JsonSerializer.Deserialize(
            receipt.ResultBytes.AsSpan(), HPDBaseJsonSerializerContext.Default.BaseActivationTransitionResult);
        return result is not null && result.State == value.ActivationState && result.Generation == value.ActivationGeneration
            && result.ControlChecksum.AsSpan().SequenceEqual(value.ActivationChecksum.AsSpan())
            && receipt.OperationKind switch
            {
                "activation-completed" or "effect-completed" => value.ActivationState == BaseActivationState.Succeeded,
                "activation-failed-terminal" => value.ActivationState == BaseActivationState.Exhausted,
                "activation-cancelled" => value.ActivationState == BaseActivationState.Cancelled,
                "activation-migrated" => value.ActivationState == BaseActivationState.Migrated,
                "activation-disposed" => value.ActivationState == BaseActivationState.Disposed,
                "effect-reconciled" => value.ActivationState is BaseActivationState.Succeeded or BaseActivationState.Exhausted,
                _ => false,
            };
    }

    private static byte[] RecoveryLifetime(BaseSemanticRecoverySubjectLifetimePreimage? value) => value is null ? [] :
        Hash("base.semanticRecovery.subjectLifetimePreimage.v1\0", System.Text.Encoding.UTF8.GetBytes(value.ContractId),
            Int32(value.ContractVersion), value.ContractChecksum.ToArray(), value.SubjectId.ToUtf8Bytes(),
            System.Text.Encoding.UTF8.GetBytes(value.AuthorityEpoch.ToBase64Url()),
            System.Text.Encoding.UTF8.GetBytes(value.Incarnation.ToBase64Url())).ToArray();

    private static byte[] Accounting(BaseSemanticActivationAccounting value)
    {
        long[] values = [value.Operations, value.ScopeDirectoryReads, value.SlotReads, value.ActivationReads, value.ReadIntervals,
            value.IndexOperations, value.KeyBytes, value.ScopeDirectoryBytes, value.ActivationBytes, value.EvidenceBytes,
            value.ReceiptBytes, value.TransientBytes, value.ActivationCreation.Candidates, value.ActivationCreation.Comparisons,
            value.ActivationCreation.IndexOperations, value.ActivationCreation.ReadIntervals, value.ActivationCreation.EvidenceBytes,
            value.ActivationCreation.TransientBytes];
        return values.SelectMany(Int64).ToArray();
    }

    private static byte[] Scope(BaseOwnedSubjectScopeEvidence value) =>
        [(byte)value.Kind, .. System.Text.Encoding.UTF8.GetBytes(value.Value ?? string.Empty)];

    private static byte[] Lifetime(BaseSemanticActivationSubjectLifetimeBinding? value)
    {
        if (value is null) return [];
        return Hash("base.semanticActivation.subjectLifetimeAuthority.v1\0", System.Text.Encoding.UTF8.GetBytes(value.ContractId),
            Int64(value.ContractVersion), value.ContractChecksum.ToArray(), value.SubjectId.ToUtf8Bytes(),
            System.Text.Encoding.UTF8.GetBytes(value.AuthorityEpoch.ToBase64Url()),
            System.Text.Encoding.UTF8.GetBytes(value.Incarnation.ToBase64Url()), value.ScopeBindingId.ToArray(), value.Checksum.ToArray()).ToArray();
    }

    private static byte[] Int64(long value) { byte[] bytes = new byte[8]; System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(bytes, value); return bytes; }
    private static byte[] Int32(int value) { byte[] bytes = new byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(bytes, value); return bytes; }
    private static ImmutableArray<byte> Hash(string purpose, params byte[][] fields)
    {
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        hash.AppendData(System.Text.Encoding.UTF8.GetBytes(purpose));
        byte[] length = new byte[4];
        foreach (byte[] field in fields) { System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, field.Length); hash.AppendData(length); hash.AppendData(field); }
        return hash.GetHashAndReset().ToImmutableArray();
    }
}

/// <summary>Contains Runtime-owned identity for one semantic activation creation.</summary>
public sealed record BaseSemanticActivationCreationIdentity
{
    /// <summary>Gets installed semantic definition authority.</summary>
    public required BaseSemanticActivationDefinitionIdentity SemanticDefinition { get; init; }
    /// <summary>Gets the canonical semantic key.</summary>
    public required BaseSemanticActivationKeyDigest Key { get; init; }
    /// <summary>Gets the stable scope-binding identifier.</summary>
    public required ImmutableArray<byte> ScopeBindingId { get; init; }
    /// <summary>Gets the derived activation identifier bytes.</summary>
    public required ImmutableArray<byte> DerivedActivationIdBytes { get; init; }
    /// <summary>Gets the canonical creation-identity checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Identifies the installed module completion operation permitted to retire a slot.</summary>
public sealed record BaseSemanticActivationModuleOperationIdentity
{
    /// <summary>Gets the stable operation identifier.</summary>
    public required string OperationId { get; init; }
    /// <summary>Gets the positive operation version.</summary>
    public required int OperationVersion { get; init; }
    /// <summary>Gets the canonical operation checksum.</summary>
    public required string OperationChecksum { get; init; }
}

/// <summary>Base type for the closed semantic activation operation union.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(BaseSemanticActivationEnsureIntent), "ensure")]
[JsonDerivedType(typeof(BaseSemanticActivationRetireIntent), "retire")]
public abstract record BaseSemanticActivationOperation;

/// <summary>Requests one bounded transaction-local semantic-slot capture.</summary>
public sealed record BaseSemanticActivationCaptureRequest
{
    /// <summary>Gets installed semantic definition authority.</summary>
    public required BaseSemanticActivationDefinitionIdentity Definition { get; init; }
    /// <summary>Gets the unbound canonical key bytes.</summary>
    public required ImmutableArray<byte> CanonicalKey { get; init; }
    /// <summary>Gets the checksum of the unbound canonical key preimage.</summary>
    public required ImmutableArray<byte> KeyPreimageChecksum { get; init; }
    /// <summary>Gets protected logical scope authority.</summary>
    public required BaseOwnedSubjectScopeEvidence Scope { get; init; }
    /// <summary>Gets the Runtime-proposed binding for an absent scope-directory entry.</summary>
    public required ImmutableArray<byte> ProposedScopeBindingId { get; init; }
    /// <summary>Gets the requested semantic transition.</summary>
    public required BaseSemanticActivationOperationKind Operation { get; init; }
    /// <summary>Gets exact store authority required by capture.</summary>
    public required BaseSemanticActivationStoreAuthorityRequirement StoreAuthority { get; init; }
    /// <summary>Gets effective semantic execution limits.</summary>
    public required BaseSemanticActivationExecutionLimits Limits { get; init; }
    /// <summary>Gets the Runtime-issued accepted-time receipt validated inside the provider transaction.</summary>
    public required BaseAcceptedTimeReceipt AcceptedTime { get; init; }
    /// <summary>Gets optional read-only external-recovery preflight authority that transaction capture must exactly recapture.</summary>
    public BaseSemanticRecoveryPreflightEvidence? RecoveryPreflight { get; init; }
    /// <summary>Gets the certified external pending ticket bound to this local transaction.</summary>
    public BaseSemanticRecoveryPendingCommitAuthority? RecoveryPending { get; init; }
}

/// <summary>Ensures one semantic activation.</summary>
public sealed record BaseSemanticActivationEnsureIntent : BaseSemanticActivationOperation
{
    /// <summary>Gets installed definition authority.</summary>
    public required BaseSemanticActivationDefinitionIdentity Definition { get; init; }
    /// <summary>Gets the bound semantic key digest.</summary>
    public required BaseSemanticActivationKeyDigest Key { get; init; }
    /// <summary>Gets canonical key bytes.</summary>
    public required ImmutableArray<byte> CanonicalKey { get; init; }
    /// <summary>Gets protected scope evidence.</summary>
    public required BaseOwnedSubjectScopeEvidence Scope { get; init; }
    /// <summary>Gets optional subject lifetime authority.</summary>
    public BaseSemanticActivationSubjectLifetimeBinding? SubjectLifetime { get; init; }
    /// <summary>Gets the activation creation.</summary>
    public required BaseSemanticActivationCreateIntent Activation { get; init; }
    /// <summary>Gets canonical due authority.</summary>
    public required BaseSemanticActivationDueAuthority Due { get; init; }
}

/// <summary>Retires one terminal semantic activation.</summary>
public sealed record BaseSemanticActivationRetireIntent : BaseSemanticActivationOperation
{
    /// <summary>Gets installed definition authority.</summary>
    public required BaseSemanticActivationDefinitionIdentity Definition { get; init; }
    /// <summary>Gets the bound semantic key digest.</summary>
    public required BaseSemanticActivationKeyDigest Key { get; init; }
    /// <summary>Gets canonical key bytes.</summary>
    public required ImmutableArray<byte> CanonicalKey { get; init; }
    /// <summary>Gets protected scope evidence.</summary>
    public required BaseOwnedSubjectScopeEvidence Scope { get; init; }
    /// <summary>Gets optional subject lifetime authority.</summary>
    public BaseSemanticActivationSubjectLifetimeBinding? SubjectLifetime { get; init; }
    /// <summary>Gets the only installed completion operation permitted to retire.</summary>
    public required BaseSemanticActivationModuleOperationIdentity CompletionOperation { get; init; }
}

/// <summary>Contains one semantic operation in the shared atomic request.</summary>
public sealed record BaseAtomicSemanticActivationExtension
{
    /// <summary>Gets the exact read-only capture request.</summary>
    public required BaseSemanticActivationCaptureRequest Capture { get; init; }
    /// <summary>Gets the closed semantic operation.</summary>
    public required BaseSemanticActivationOperation Operation { get; init; }
    /// <summary>Gets the canonical structural digest.</summary>
    public required ImmutableArray<byte> StructuralDigest { get; init; }
}

/// <summary>Classifies the exact semantic state captured from storage.</summary>
public enum BaseSemanticActivationCapturedState
{
    /// <summary>The slot key is authoritatively absent.</summary>
    Missing = 1,
    /// <summary>The slot is live.</summary>
    Live = 2,
    /// <summary>The slot is retired.</summary>
    Retired = 3,
    /// <summary>The slot is represented by permanent compacted absence.</summary>
    CompactedAbsent = 4,
}

/// <summary>Contains the required store authority for semantic execution.</summary>
public sealed record BaseSemanticActivationStoreAuthorityRequirement
{
    /// <summary>Gets application identity.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets logical store identity.</summary>
    public required string LogicalStoreId { get; init; }
    /// <summary>Gets physical store-instance identity.</summary>
    public required string StoreInstanceId { get; init; }
    /// <summary>Gets restore epoch.</summary>
    public required long RestoreEpoch { get; init; }
    /// <summary>Gets schema generation.</summary>
    public required long SchemaGeneration { get; init; }
    /// <summary>Gets semantic authority generation.</summary>
    public required long SemanticAuthorityGeneration { get; init; }
    /// <summary>Gets installed definition-set checksum.</summary>
    public required ImmutableArray<byte> DefinitionSetChecksum { get; init; }
}

/// <summary>Contains captured semantic store authority.</summary>
public sealed record BaseSemanticActivationStoreAuthority
{
    /// <summary>Gets required authority.</summary>
    public required BaseSemanticActivationStoreAuthorityRequirement Requirement { get; init; }
    /// <summary>Gets provider evidence checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Contains the stable and rotatable scope-directory binding.</summary>
public sealed record BaseSemanticActivationScopeBinding
{
    /// <summary>Gets scope kind.</summary>
    public required BaseSubjectScopeKind Kind { get; init; }
    /// <summary>Gets stable BASE-owned binding ID.</summary>
    public required ImmutableArray<byte> BindingId { get; init; }
    /// <summary>Gets protected canonical scope.</summary>
    public required ImmutableArray<byte> ProtectedCanonicalScope { get; init; }
    /// <summary>Gets current protected seek digest.</summary>
    public required ImmutableArray<byte> SeekDigest { get; init; }
    /// <summary>Gets protection key ID.</summary>
    public required string ProtectionKeyId { get; init; }
    /// <summary>Gets protection key version.</summary>
    public required int ProtectionKeyVersion { get; init; }
    /// <summary>Gets binding checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Classifies a scope-directory capture.</summary>
public enum BaseSemanticActivationScopeDirectoryState
{
    /// <summary>The directory binding exists.</summary>
    Existing = 1,
    /// <summary>The directory key is absent and the proposed binding may be inserted.</summary>
    Missing = 2,
}

/// <summary>Contains one read-only scope-directory capture.</summary>
public sealed record BaseSemanticActivationScopeDirectoryCapture
{
    /// <summary>Gets captured directory state.</summary>
    public required BaseSemanticActivationScopeDirectoryState State { get; init; }
    /// <summary>Gets existing or proposed resulting binding.</summary>
    public required BaseSemanticActivationScopeBinding ResultingBinding { get; init; }
    /// <summary>Gets exact scope-directory read intervals.</summary>
    public required ImmutableArray<BaseAtomicReadIntervalEvidence> ReadIntervals { get; init; }
    /// <summary>Gets canonical retained bytes.</summary>
    public required long CanonicalBytes { get; init; }
    /// <summary>Gets capture checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Contains authoritative missing-slot evidence.</summary>
public sealed record BaseSemanticActivationMissingAuthority
{
    /// <summary>Gets semantic key.</summary>
    public required BaseSemanticActivationKeyDigest Key { get; init; }
    /// <summary>Gets captured store authority.</summary>
    public required BaseSemanticActivationStoreAuthority StoreAuthority { get; init; }
    /// <summary>Gets exact absent-key access-path checksum.</summary>
    public required ImmutableArray<byte> AccessPathChecksum { get; init; }
}

/// <summary>Contains current live-slot authority.</summary>
public sealed record BaseSemanticActivationLiveAuthority
{
    /// <summary>Gets installed semantic definition.</summary>
    public required BaseSemanticActivationDefinitionIdentity Definition { get; init; }
    /// <summary>Gets semantic key.</summary>
    public required BaseSemanticActivationKeyDigest KeyDigest { get; init; }
    /// <summary>Gets protected logical scope evidence.</summary>
    public required BaseOwnedSubjectScopeEvidence Scope { get; init; }
    /// <summary>Gets stable scope binding.</summary>
    public required BaseSemanticActivationScopeBinding ScopeBinding { get; init; }
    /// <summary>Gets optional subject lifetime.</summary>
    public BaseSemanticActivationSubjectLifetimeBinding? SubjectLifetime { get; init; }
    /// <summary>Gets mapped activation ID.</summary>
    public required string ActivationId { get; init; }
    /// <summary>Gets activation definition.</summary>
    public required BaseActivationDefinitionKey ActivationDefinition { get; init; }
    /// <summary>Gets activation input checksum.</summary>
    public required ImmutableArray<byte> InputChecksum { get; init; }
    /// <summary>Gets canonical due authority.</summary>
    public required BaseSemanticActivationDueAuthority Due { get; init; }
    /// <summary>Gets positive slot generation.</summary>
    public required long SlotGeneration { get; init; }
    /// <summary>Gets store authority.</summary>
    public required BaseSemanticActivationStoreAuthority StoreAuthority { get; init; }
    /// <summary>Gets live authority checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Contains current retired-slot authority.</summary>
public sealed record BaseSemanticActivationRetirementAuthority
{
    /// <summary>Gets exact definition authority.</summary>
    public required BaseSemanticActivationDefinitionKey Definition { get; init; }
    /// <summary>Gets semantic key.</summary>
    public required BaseSemanticActivationKeyDigest KeyDigest { get; init; }
    /// <summary>Gets optional subject lifetime.</summary>
    public BaseSemanticActivationSubjectLifetimeBinding? SubjectLifetime { get; init; }
    /// <summary>Gets terminal activation ID.</summary>
    public required string ActivationId { get; init; }
    /// <summary>Gets terminal activation state.</summary>
    public required BaseActivationState TerminalState { get; init; }
    /// <summary>Gets terminal activation generation.</summary>
    public required long TerminalActivationGeneration { get; init; }
    /// <summary>Gets terminal activation checksum.</summary>
    public required ImmutableArray<byte> TerminalActivationChecksum { get; init; }
    /// <summary>Gets the installed completion operation checksum.</summary>
    public required ImmutableArray<byte> CompletionOperationChecksum { get; init; }
    /// <summary>Gets the outer completion receipt checksum.</summary>
    public required ImmutableArray<byte> CompletionReceiptChecksum { get; init; }
    /// <summary>Gets retirement journal position.</summary>
    public required long RetirementPosition { get; init; }
    /// <summary>Gets final slot generation.</summary>
    public required long SlotGeneration { get; init; }
    /// <summary>Gets store authority.</summary>
    public required BaseSemanticActivationStoreAuthority StoreAuthority { get; init; }
    /// <summary>Gets retirement checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Contains permanent compacted-absence authority.</summary>
public sealed record BaseSemanticActivationAbsenceAuthority
{
    /// <summary>Gets semantic key.</summary>
    public required BaseSemanticActivationKeyDigest Key { get; init; }
    /// <summary>Gets definition authority.</summary>
    public required BaseSemanticActivationDefinitionIdentity Definition { get; init; }
    /// <summary>Gets stable scope binding ID.</summary>
    public required ImmutableArray<byte> ScopeBindingId { get; init; }
    /// <summary>Gets optional subject lifetime.</summary>
    public BaseSemanticActivationSubjectLifetimeBinding? SubjectLifetime { get; init; }
    /// <summary>Gets final slot generation.</summary>
    public required long FinalSlotGeneration { get; init; }
    /// <summary>Gets absence floor generation.</summary>
    public required long AbsenceFloorGeneration { get; init; }
    /// <summary>Gets retirement position.</summary>
    public required long RetirementPosition { get; init; }
    /// <summary>Gets store authority.</summary>
    public required BaseSemanticActivationStoreAuthority StoreAuthority { get; init; }
    /// <summary>Gets absence checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Contains exact semantic accounting.</summary>
public sealed record BaseSemanticActivationAccounting
{
    /// <summary>Gets semantic operations.</summary>
    public required int Operations { get; init; }
    /// <summary>Gets scope-directory reads.</summary>
    public required int ScopeDirectoryReads { get; init; }
    /// <summary>Gets slot reads.</summary>
    public required int SlotReads { get; init; }
    /// <summary>Gets activation reads.</summary>
    public required int ActivationReads { get; init; }
    /// <summary>Gets read intervals.</summary>
    public required int ReadIntervals { get; init; }
    /// <summary>Gets index operations.</summary>
    public required int IndexOperations { get; init; }
    /// <summary>Gets canonical key bytes.</summary>
    public required long KeyBytes { get; init; }
    /// <summary>Gets scope-directory bytes.</summary>
    public required long ScopeDirectoryBytes { get; init; }
    /// <summary>Gets activation bytes.</summary>
    public required long ActivationBytes { get; init; }
    /// <summary>Gets canonical evidence bytes.</summary>
    public required long EvidenceBytes { get; init; }
    /// <summary>Gets canonical receipt bytes.</summary>
    public required long ReceiptBytes { get; init; }
    /// <summary>Gets retained transient bytes.</summary>
    public required long TransientBytes { get; init; }
    /// <summary>Gets the exact nested L51 activation-creation accounting.</summary>
    public required BaseActivationAccounting ActivationCreation { get; init; }
}

/// <summary>Contains provider-captured semantic slot authority.</summary>
public sealed record BaseCapturedSemanticActivationEvidence
{
    /// <summary>Gets captured state.</summary>
    public required BaseSemanticActivationCapturedState State { get; init; }
    /// <summary>Gets scope-directory capture.</summary>
    public required BaseSemanticActivationScopeDirectoryCapture ScopeDirectory { get; init; }
    /// <summary>Gets missing authority only for Missing.</summary>
    public BaseSemanticActivationMissingAuthority? Missing { get; init; }
    /// <summary>Gets live authority only for Live.</summary>
    public BaseSemanticActivationLiveAuthority? Live { get; init; }
    /// <summary>Gets retirement authority only for Retired.</summary>
    public BaseSemanticActivationRetirementAuthority? Retired { get; init; }
    /// <summary>Gets absence authority only for CompactedAbsent.</summary>
    public BaseSemanticActivationAbsenceAuthority? Absent { get; init; }
    /// <summary>Gets the mapped activation generation only while the slot is live.</summary>
    public long? ActivationGeneration { get; init; }
    /// <summary>Gets the mapped activation state only while the slot is live.</summary>
    public BaseActivationState? ActivationState { get; init; }
    /// <summary>Gets the mapped activation control checksum only while the slot is live.</summary>
    public ImmutableArray<byte> ActivationChecksum { get; init; }
    /// <summary>Gets the mapped activation terminal receipt checksum when the live mapping is terminal.</summary>
    public ImmutableArray<byte> ActivationTerminalReceiptChecksum { get; init; }
    /// <summary>Gets normalized nonempty read intervals.</summary>
    public required ImmutableArray<BaseAtomicReadIntervalEvidence> ReadIntervals { get; init; }
    /// <summary>Gets exact capture accounting.</summary>
    public required BaseSemanticActivationAccounting Accounting { get; init; }
    /// <summary>Gets the exact accepted-time receipt admitted by the provider transaction.</summary>
    public required BaseAcceptedTimeReceipt AcceptedTime { get; init; }
    /// <summary>Gets evidence checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Opaque single-use provider plan for a semantic transition.</summary>
public abstract class BaseSemanticActivationPreparedPlan
{
    /// <summary>Initializes a provider-owned plan.</summary>
    protected BaseSemanticActivationPreparedPlan() { }
}

/// <summary>Contains one semantic write interval.</summary>
public sealed record BaseSemanticActivationWriteIntervalEvidence
{
    /// <summary>Gets access-path ID.</summary>
    public required string AccessPathId { get; init; }
    /// <summary>Gets lower bound.</summary>
    public required ImmutableArray<byte> Lower { get; init; }
    /// <summary>Gets lower inclusivity.</summary>
    public required bool LowerInclusive { get; init; }
    /// <summary>Gets upper bound.</summary>
    public required ImmutableArray<byte> Upper { get; init; }
    /// <summary>Gets upper inclusivity.</summary>
    public required bool UpperInclusive { get; init; }
    /// <summary>Gets interval checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Contains provider-prepared semantic transition evidence.</summary>
public sealed record BasePreparedSemanticActivation
{
    /// <summary>Gets session-owned single-use plan.</summary>
    public required BaseSemanticActivationPreparedPlan SessionPlan { get; init; }
    /// <summary>Gets operation kind.</summary>
    public required BaseSemanticActivationOperationKind Operation { get; init; }
    /// <summary>Gets prior state.</summary>
    public required BaseSemanticActivationCapturedState PriorState { get; init; }
    /// <summary>Gets resulting state.</summary>
    public required BaseSemanticActivationSlotState ResultingState { get; init; }
    /// <summary>Gets resulting slot generation.</summary>
    public required long ResultingSlotGeneration { get; init; }
    /// <summary>Gets resulting activation ID when live.</summary>
    public string? ResultingActivationId { get; init; }
    /// <summary>Gets normalized read intervals.</summary>
    public required ImmutableArray<BaseAtomicReadIntervalEvidence> ReadIntervals { get; init; }
    /// <summary>Gets normalized write intervals.</summary>
    public required ImmutableArray<BaseSemanticActivationWriteIntervalEvidence> WriteIntervals { get; init; }
    /// <summary>Gets exact accounting.</summary>
    public required BaseSemanticActivationAccounting Accounting { get; init; }
    /// <summary>Gets prepared checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Contains applied semantic evidence before commit publication.</summary>
public sealed record BaseProvisionalSemanticActivation
{
    /// <summary>Gets operation kind.</summary>
    public required BaseSemanticActivationOperationKind Operation { get; init; }
    /// <summary>Gets prior state.</summary>
    public required BaseSemanticActivationCapturedState PriorState { get; init; }
    /// <summary>Gets resulting state.</summary>
    public required BaseSemanticActivationSlotState ResultingState { get; init; }
    /// <summary>Gets resulting slot generation.</summary>
    public required long ResultingSlotGeneration { get; init; }
    /// <summary>Gets the canonical checksum of the resulting durable slot authority.</summary>
    public required ImmutableArray<byte> ResultingSlotChecksum { get; init; }
    /// <summary>Gets activation ID when live.</summary>
    public string? ActivationId { get; init; }
    /// <summary>Gets activation generation when live.</summary>
    public long? ActivationGeneration { get; init; }
    /// <summary>Gets activation checksum when live.</summary>
    public ImmutableArray<byte> ActivationChecksum { get; init; }
    /// <summary>Gets commit journal position.</summary>
    public required long CommitJournalPosition { get; init; }
    /// <summary>Gets exact accounting.</summary>
    public required BaseSemanticActivationAccounting Accounting { get; init; }
    /// <summary>Gets provisional checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Stores semantic activation evidence in one module-mutation receipt.</summary>
public sealed record BaseSemanticActivationReceiptEvidence
{
    /// <summary>Gets the operation kind.</summary>
    public required BaseSemanticActivationOperationKind Operation { get; init; }
    /// <summary>Gets the installed definition ID.</summary>
    public required string DefinitionId { get; init; }
    /// <summary>Gets the installed definition version.</summary>
    public required int DefinitionVersion { get; init; }
    /// <summary>Gets the definition checksum.</summary>
    public required ImmutableArray<byte> DefinitionChecksum { get; init; }
    /// <summary>Gets the exact semantic key digest.</summary>
    public required BaseSemanticActivationKeyDigest Key { get; init; }
    /// <summary>Gets the resulting state.</summary>
    public required BaseSemanticActivationSlotState State { get; init; }
    /// <summary>Gets the resulting slot generation.</summary>
    public required long SlotGeneration { get; init; }
    /// <summary>Gets ensure disposition when applicable.</summary>
    public BaseSemanticActivationEnsureDisposition? EnsureDisposition { get; init; }
    /// <summary>Gets retirement disposition when applicable.</summary>
    public BaseSemanticActivationRetirementDisposition? RetirementDisposition { get; init; }
    /// <summary>Gets the live activation identifier when disclosed.</summary>
    public string? ActivationId { get; init; }
    /// <summary>Gets the resulting slot checksum.</summary>
    public required ImmutableArray<byte> SlotChecksum { get; init; }
    /// <summary>Gets the commit journal position.</summary>
    public required long JournalPosition { get; init; }
    /// <summary>Gets commit-evidence checksum.</summary>
    public required ImmutableArray<byte> CommitEvidenceChecksum { get; init; }
    /// <summary>Gets the durable external recovery handoff for a newly retired slot.</summary>
    public BaseSemanticRecoveryLocalReceiptAuthority? RecoveryPublication { get; init; }
    /// <summary>Gets the canonical evidence checksum.</summary>
    public required ImmutableArray<byte> Checksum { get; init; }
}

/// <summary>Provides the source-generated wire shape for semantic receipt evidence.</summary>
public sealed record BaseSemanticActivationReceiptEvidenceWire
{
    /// <summary>Gets the operation discriminator.</summary>
    public required int Operation { get; init; }
    /// <summary>Gets the definition ID.</summary>
    public required string DefinitionId { get; init; }
    /// <summary>Gets the definition version.</summary>
    public required int DefinitionVersion { get; init; }
    /// <summary>Gets the definition checksum.</summary>
    public required byte[] DefinitionChecksum { get; init; }
    /// <summary>Gets semantic key digest bytes.</summary>
    public required byte[] KeyDigest { get; init; }
    /// <summary>Gets the resulting state.</summary>
    public required int State { get; init; }
    /// <summary>Gets the canonical positive slot generation.</summary>
    public required string SlotGeneration { get; init; }
    /// <summary>Gets ensure disposition when applicable.</summary>
    public int? EnsureDisposition { get; init; }
    /// <summary>Gets retirement disposition when applicable.</summary>
    public int? RetirementDisposition { get; init; }
    /// <summary>Gets the live activation identifier when disclosed.</summary>
    public string? ActivationId { get; init; }
    /// <summary>Gets resulting slot checksum.</summary>
    public required byte[] SlotChecksum { get; init; }
    /// <summary>Gets canonical positive journal position.</summary>
    public required string JournalPosition { get; init; }
    /// <summary>Gets commit-evidence checksum.</summary>
    public required byte[] CommitEvidenceChecksum { get; init; }
    /// <summary>Gets the durable external recovery handoff.</summary>
    public BaseSemanticRecoveryLocalReceiptAuthority? RecoveryPublication { get; init; }
    /// <summary>Gets the canonical evidence checksum.</summary>
    public required byte[] Checksum { get; init; }
}
