using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HPD.Base;
using HPD.Payments.Persistence.AtomicDomains;
using HPD.Payments.Persistence.Ports;
using HPD.Payments.Persistence.Receipts;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Primitives.Time;
using HPD.Payments.Supporting.Custody;
using HPD.Payments.Supporting.Ownership;
using HPD.Payments.Supporting.Relations;

namespace HPD.Payments.Runtime.Base;

/// <summary>Implements all supporting Payments persistence ports through Base registered operations and bounded queries.</summary>
public sealed class BaseSupportingPersistencePort : IRelationPersistencePort, IContinuationPersistencePort, ICustodyPersistencePort
{
    private readonly BaseSession _session;

    /// <summary>Creates a supporting bridge over one principal-bound Base session.</summary>
    public BaseSupportingPersistencePort(BaseSession session) => _session = session ?? throw new ArgumentNullException(nameof(session));

    /// <inheritdoc />
    public async ValueTask<PersistenceReceipt> GuardedRelateAsync(SupportingRelation relation, AtomicDomain domain, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(relation); cancellationToken.ThrowIfCancellationRequested();
        if (!IsLocal(domain) || relation.RelationId.Scope != domain.DomainId.Scope)
            return Receipt(relation.RelationId, domain, "guarded-relate", PersistenceObservation.Unsupported, "e-local-only");
        if (relation.Source.Generation.Value <= 1 || relation.Target.Generation.Value <= 1)
            return Receipt(relation.RelationId, domain, "guarded-relate", PersistenceObservation.Failed, "endpoint-generation-conflict");
        string recordId = Key("r", relation.RelationId.GetCanonicalBytes());
        string payload = Encode(PaymentsRelationPayload.From(relation), PaymentsSupportingPayloadJsonContext.Default.PaymentsRelationPayload);
        var request = new PersistRelationRequest
        {
            RecordId = recordId, Scope = ScopeKey(relation.RelationId.Scope), Payload = payload,
            SourceOwnerKey = OwnerKey(relation.Source), SourceGeneration = BaseGeneration(relation.Source.Generation),
            TargetOwnerKey = OwnerKey(relation.Target), TargetGeneration = BaseGeneration(relation.Target.Generation),
        };
        BaseResult<BaseModuleMutationExecutionResult<PaymentsPersistenceResult>> result = await _session.ModuleMutations.Get(PaymentsRelationMutation.Identity)
            .ExecuteAsync(request, Identity("relation", recordId, payload), cancellationToken: cancellationToken).ConfigureAwait(false);
        return Translate(result, relation.RelationId, domain, "guarded-relate", "endpoint-generation-conflict");
    }

    /// <inheritdoc />
    public async ValueTask<PersistenceReceipt> CommitDiscoverableAsync(ContinuationDeclaration continuation, AtomicDomain domain, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(continuation); cancellationToken.ThrowIfCancellationRequested();
        if (!IsLocal(domain) || continuation.ContinuationId.Scope != domain.DomainId.Scope)
            return Receipt(continuation.ContinuationId, domain, "commit-continuation", PersistenceObservation.Unsupported, "e-local-only");
        if (continuation.Owner.Generation.Value <= 1)
            return Receipt(continuation.ContinuationId, domain, "commit-continuation", PersistenceObservation.Failed, "owner-generation-conflict");
        string recordId = Key("n", continuation.ContinuationId.GetCanonicalBytes());
        string payload = Encode(PaymentsContinuationPayload.From(continuation), PaymentsSupportingPayloadJsonContext.Default.PaymentsContinuationPayload);
        var request = new PersistContinuationRequest
        {
            RecordId = recordId, Scope = ScopeKey(continuation.ContinuationId.Scope), OwnerKey = OwnerKey(continuation.Owner),
            OwnerGeneration = BaseGeneration(continuation.Owner.Generation), Payload = payload,
        };
        BaseResult<BaseModuleMutationExecutionResult<PaymentsPersistenceResult>> result = await _session.ModuleMutations.Get(PaymentsContinuationMutation.Identity)
            .ExecuteAsync(request, Identity("continuation", recordId, payload), cancellationToken: cancellationToken).ConfigureAwait(false);
        return Translate(result, continuation.ContinuationId, domain, "commit-continuation", "owner-generation-conflict");
    }

    /// <inheritdoc />
    public async ValueTask<ContinuationDiscoveryPage> DiscoverAsync(AtomicDomain domain, int maximumItems, ReadOnlyMemory<byte> continuation = default, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (maximumItems is < 1 or > 1024) throw new ArgumentOutOfRangeException(nameof(maximumItems));
        if (!IsLocal(domain)) throw new NotSupportedException("The Base InMemory bridge supports only E-LOCAL.");
        string scope = ScopeKey(domain.DomainId.Scope);
        BaseRecord<PaymentsContinuationRecord>[] all = (await _session.Collection(PaymentsContinuationRecord.Collection).Query()
            .Where(PaymentsContinuationRecord.Fields.Scope, scope).Take(4096).ToArrayAsync(4096, cancellationToken).ConfigureAwait(false)).RequireValue()
            .OrderBy(item => item.Id.ToString(), StringComparer.Ordinal).ToArray();
        int offset = DecodeToken(continuation.Span, maximumItems, Shape(scope));
        if (offset > all.Length) throw new ArgumentException("Continuation is outside the current discovery result set.", nameof(continuation));
        BaseRecord<PaymentsContinuationRecord>[] selected = all.Skip(offset).Take(maximumItems).ToArray();
        ContinuationDeclaration[] items = selected.Select(item => Decode(item.Value.Payload, PaymentsSupportingPayloadJsonContext.Default.PaymentsContinuationPayload).ToValue()).ToArray();
        int next = offset + items.Length;
        return new(items, next < all.Length ? EncodeToken(next, maximumItems, Shape(scope)) : []);
    }

    /// <inheritdoc />
    public async ValueTask<PersistenceReceipt> RecordCustodyAsync(CustodyInstance custody, AtomicDomain domain, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(custody); cancellationToken.ThrowIfCancellationRequested();
        if (!IsLocal(domain) || custody.InstanceId.Scope != domain.DomainId.Scope)
            return Receipt(custody.InstanceId, domain, "record-custody", PersistenceObservation.Unsupported, "e-local-only");
        if (custody.Subject.Generation.Value <= 1)
            return Receipt(custody.InstanceId, domain, "record-custody", PersistenceObservation.Failed, "owner-generation-conflict");
        string instanceKey = Key("c", custody.InstanceId.GetCanonicalBytes());
        BaseRecord<PaymentsCustodyRecord>[] existing = (await _session.Collection(PaymentsCustodyRecord.Collection).Query()
            .Where(PaymentsCustodyRecord.Fields.InstanceKey, instanceKey).Take(4096)
            .ToArrayAsync(4096, cancellationToken).ConfigureAwait(false)).RequireValue();
        CustodyInstance[] prior = existing
            .Select(item => Decode(item.Value.Payload, PaymentsSupportingPayloadJsonContext.Default.PaymentsCustodyPayload).ToValue())
            .OrderBy(item => item.InventoryGeneration.Value).ToArray();
        if (prior.Any(item => item.InventoryGeneration.Value > custody.InventoryGeneration.Value))
            return Receipt(custody.InstanceId, domain, "record-custody", PersistenceObservation.Failed, "custody-generation-conflict");
        int precedingCount = prior.Count(item => item.InventoryGeneration.Value < custody.InventoryGeneration.Value);
        int expectedOrdinal = prior.Any(item => item.InventoryGeneration == custody.InventoryGeneration)
            ? precedingCount
            : prior.Length;
        string recordId = instanceKey + "-" + GenerationText(custody.InventoryGeneration.Value);
        string payload = Encode(PaymentsCustodyPayload.From(custody), PaymentsSupportingPayloadJsonContext.Default.PaymentsCustodyPayload);
        var request = new PersistCustodyRequest
        {
            RecordId = recordId, OwnerKey = OwnerKey(custody.Subject), OwnerGeneration = BaseGeneration(custody.Subject.Generation),
            InstanceKey = instanceKey, ExpectedInstanceGeneration = expectedOrdinal == 0 ? null : ParseBaseGeneration((ulong)expectedOrdinal),
            InventoryGeneration = GenerationText(custody.InventoryGeneration.Value), Payload = payload,
        };
        BaseResult<BaseModuleMutationExecutionResult<PaymentsPersistenceResult>> result = await _session.ModuleMutations.Get(PaymentsCustodyMutation.Identity)
            .ExecuteAsync(request, Identity("custody", recordId, payload), cancellationToken: cancellationToken).ConfigureAwait(false);
        string success = custody.State == CustodyState.Residual ? "residue-retained" : "none";
        return Translate(result, custody.InstanceId, domain, "record-custody", "custody-generation-conflict", success);
    }

    /// <inheritdoc />
    public async ValueTask<CustodyPage> ReadCustodyAsync(OwnerReference owner, OwnerGeneration throughGeneration, int maximumItems, ReadOnlyMemory<byte> continuation = default, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!owner.IsValid || !throughGeneration.IsValid || maximumItems is < 1 or > 1024)
            throw new ArgumentException("A valid owner, generation, and bound are required.");
        string ownerKey = OwnerKey(owner);
        BaseRecord<PaymentsCustodyRecord>[] records = (await _session.Collection(PaymentsCustodyRecord.Collection).Query()
            .Where(PaymentsCustodyRecord.Fields.OwnerKey, ownerKey).Take(4096).ToArrayAsync(4096, cancellationToken).ConfigureAwait(false)).RequireValue();
        CustodyInstance[] all = records.Select(item => Decode(item.Value.Payload, PaymentsSupportingPayloadJsonContext.Default.PaymentsCustodyPayload).ToValue())
            .Where(item => item.InventoryGeneration.Value <= throughGeneration.Value)
            .GroupBy(item => item.InstanceId).Select(group => group.OrderByDescending(item => item.InventoryGeneration.Value).First())
            .OrderBy(item => Convert.ToHexString(item.InstanceId.GetCanonicalBytes()), StringComparer.Ordinal).ToArray();
        ulong shape = Shape(ownerKey + "\n" + throughGeneration.Value.ToString(CultureInfo.InvariantCulture));
        int offset = DecodeToken(continuation.Span, maximumItems, shape);
        if (offset > all.Length) throw new ArgumentException("Continuation is outside the current custody result set.", nameof(continuation));
        CustodyInstance[] items = all.Skip(offset).Take(maximumItems).ToArray();
        int next = offset + items.Length;
        return new(items, next < all.Length ? EncodeToken(next, maximumItems, shape) : []);
    }

    /// <summary>Administratively purges exact verified-absence custody events through Base's host-only boundary.</summary>
    public async ValueTask<int> SweepVerifiedAbsentAsync(IHPDBaseAdministration administration, PrincipalContext principal,
        OwnerGeneration throughGeneration, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(administration); ArgumentNullException.ThrowIfNull(principal);
        if (!throughGeneration.IsValid) throw new ArgumentException("A valid sweep generation is required.", nameof(throughGeneration));
        BaseRecord<PaymentsCustodyRecord>[] records = (await _session.Collection(PaymentsCustodyRecord.Collection).Query().Take(4096)
            .ToArrayAsync(4096, cancellationToken).ConfigureAwait(false)).RequireValue();
        RecordId[] ids = records.Where(item =>
            {
                CustodyInstance value = Decode(item.Value.Payload, PaymentsSupportingPayloadJsonContext.Default.PaymentsCustodyPayload).ToValue();
                return value.State == CustodyState.VerifiedAbsent && value.InventoryGeneration.Value <= throughGeneration.Value;
            })
            .Select(item => item.Id).Distinct().OrderBy(id => id.Value, StringComparer.Ordinal).ToArray();
        if (ids.Length == 0) return 0;
        BaseResult<BasePurgeResult> result = await administration.PurgeAsync(new BasePurgeRequest
        {
            CollectionId = PaymentsCustodyRecord.Collection.Id, RecordIds = ids, Principal = principal,
            ReasonCode = "verified-absence-sweep", AuditReference = "hpd-payments-custody", EvaluatedAt = DateTimeOffset.UtcNow,
        }, cancellationToken).ConfigureAwait(false);
        return result.RequireValue().PurgedCount;
    }

    private static PersistenceReceipt Translate(BaseResult<BaseModuleMutationExecutionResult<PaymentsPersistenceResult>> result, SemanticId id,
        AtomicDomain domain, string operation, string conflictCode, string successCode = "none")
    {
        if (result is BaseSuccess<BaseModuleMutationExecutionResult<PaymentsPersistenceResult>>)
            return Receipt(id, domain, operation, PersistenceObservation.Observed, successCode);
        var failure = (BaseFailure<BaseModuleMutationExecutionResult<PaymentsPersistenceResult>>)result;
        PersistenceObservation observation = string.Equals(failure.Error.Code, "base.moduleMutation.requirementFailed", StringComparison.Ordinal)
            ? PersistenceObservation.Failed : failure.Status switch
        {
            OperationStatus.Unsupported or OperationStatus.CapabilityUnavailable => PersistenceObservation.Unsupported,
            OperationStatus.StoreError => PersistenceObservation.Indeterminate,
            _ => PersistenceObservation.Failed,
        };
        string limitation = observation == PersistenceObservation.Failed ? conflictCode : observation == PersistenceObservation.Unsupported ? "base-unsupported" : "base-indeterminate";
        return Receipt(id, domain, operation, observation, limitation);
    }

    private static PersistenceReceipt Receipt(SemanticId id, AtomicDomain domain, string operation, PersistenceObservation observation, string limitation) =>
        new(id, observation, new(domain, operation, observation, NamedTime.Create(TimeKind.Observed, DateTimeOffset.UnixEpoch), Evidence(), limitation));
    private static CanonicalDigest Evidence() => CanonicalDigest.Sha256(new("base-inmemory", ContractVersion.Create(1, 0), "registered-mutation", "ordinal", "utc", "canonical", "builtin"), "hpd-payments-base-inmemory-bridge"u8);
    private static bool IsLocal(AtomicDomain domain) => domain.IsValid && domain.Kind == AtomicDomainKind.Local;
    private static string ScopeKey(ScopeId scope) => Key("s", Encoding.UTF8.GetBytes(scope.ToString()));
    private static string OwnerKey(OwnerReference owner)
    {
        byte[] authority = Encoding.UTF8.GetBytes(((int)owner.Authority).ToString(CultureInfo.InvariantCulture) + ":");
        byte[] subject = owner.SubjectId.GetCanonicalBytes();
        byte[] input = new byte[authority.Length + subject.Length]; authority.CopyTo(input, 0); subject.CopyTo(input, authority.Length);
        return Key("o", input);
    }
    private static BaseModuleGeneration BaseGeneration(OwnerGeneration generation)
    {
        if (generation.Value <= 1) throw new ArgumentException("A supporting declaration cannot target an owner before its first committed fact.", nameof(generation));
        return ParseBaseGeneration(generation.Value - 1);
    }
    private static BaseModuleGeneration ParseBaseGeneration(ulong value)
    {
        byte[] json = Encoding.UTF8.GetBytes("\"" + value.ToString(CultureInfo.InvariantCulture) + "\"");
        var reader = new Utf8JsonReader(json); reader.Read();
        return new BaseModuleGenerationJsonConverter().Read(ref reader, typeof(BaseModuleGeneration), new JsonSerializerOptions());
    }
    private static string GenerationText(ulong generation) => generation.ToString("D20", CultureInfo.InvariantCulture);
    private static string Key(string prefix, ReadOnlySpan<byte> bytes) => prefix + "-" + Convert.ToHexString(SHA256.HashData(bytes));
    private static string Encode<T>(T value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo) => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(value, typeInfo));
    private static T Decode<T>(string value, System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo) => JsonSerializer.Deserialize(Convert.FromBase64String(value), typeInfo) ?? throw new InvalidOperationException("Persisted supporting payload decoded to null.");
    private static BaseMutationRequestIdentity Identity(string operation, string recordId, string payload) => BaseMutationRequestIdentity.Create("hpd.payments", operation,
        Key("i", Encoding.UTF8.GetBytes(recordId + "\n" + payload)), BaseMutationRequestFingerprint.Create(SHA256.HashData(Encoding.UTF8.GetBytes(recordId + "\n" + payload))));
    private static ulong Shape(string value) => BinaryPrimitives.ReadUInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static byte[] EncodeToken(int offset, int size, ulong shape)
    {
        byte[] token = new byte[16]; BinaryPrimitives.WriteInt32BigEndian(token, offset); BinaryPrimitives.WriteInt32BigEndian(token.AsSpan(4), size); BinaryPrimitives.WriteUInt64BigEndian(token.AsSpan(8), shape); return token;
    }
    private static int DecodeToken(ReadOnlySpan<byte> token, int size, ulong shape)
    {
        if (token.IsEmpty) return 0;
        if (token.Length != 16 || BinaryPrimitives.ReadInt32BigEndian(token[4..]) != size || BinaryPrimitives.ReadUInt64BigEndian(token[8..]) != shape)
            throw new ArgumentException("Continuation does not belong to this exact request shape.", nameof(token));
        int offset = BinaryPrimitives.ReadInt32BigEndian(token); return offset >= 0 ? offset : throw new ArgumentException("Continuation offset is invalid.", nameof(token));
    }
}
