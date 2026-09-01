using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using HPD.Base;
using HPD.Payments.Persistence.AtomicDomains;
using HPD.Payments.Persistence.Ports;
using HPD.Payments.Persistence.Receipts;
using HPD.Payments.Primitives.Identity;
using HPD.Payments.Supporting.Ownership;

namespace HPD.Payments.Runtime.Base;

/// <summary>Provides one closed AOT-safe fact representation for the Base persistence bridge.</summary>
public sealed class PaymentsFactJsonCodec<TFact> where TFact : notnull
{
    private readonly Func<TFact, string> _encode;
    private readonly Func<string, TFact> _decode;
    /// <summary>Gets the stable semantic codec identifier.</summary>
    public string TypeId { get; }

    /// <summary>Creates an exact source-generated JSON codec.</summary>
    public PaymentsFactJsonCodec(string typeId, JsonTypeInfo<TFact> typeInfo)
    {
        if (string.IsNullOrWhiteSpace(typeId) || Encoding.UTF8.GetByteCount(typeId) > 128 || typeId.Any(char.IsControl))
            throw new ArgumentException("A bounded stable fact type identifier is required.", nameof(typeId));
        TypeId = typeId.Normalize(NormalizationForm.FormC);
        ArgumentNullException.ThrowIfNull(typeInfo);
        _encode = value => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(value, typeInfo));
        _decode = payload => JsonSerializer.Deserialize(Convert.FromBase64String(payload), typeInfo)
            ?? throw new InvalidOperationException("The persisted fact payload decoded to null.");
    }

    internal PaymentsFactJsonCodec(string typeId, Func<TFact, string> encode, Func<string, TFact> decode)
    {
        if (string.IsNullOrWhiteSpace(typeId) || Encoding.UTF8.GetByteCount(typeId) > 128 || typeId.Any(char.IsControl))
            throw new ArgumentException("A bounded stable fact type identifier is required.", nameof(typeId));
        TypeId = typeId.Normalize(NormalizationForm.FormC); _encode = encode; _decode = decode;
    }

    internal string Encode(TFact fact) => _encode(fact);
    internal TFact Decode(string payload) => _decode(payload);
}

/// <summary>Creates exact fact codecs through closed source-generated payload graphs.</summary>
public static class PaymentsFactJsonCodec
{
    /// <summary>Creates a codec with explicit semantic projection and reconstruction.</summary>
    public static PaymentsFactJsonCodec<TFact> Create<TFact, TPayload>(string typeId, JsonTypeInfo<TPayload> typeInfo,
        Func<TFact, TPayload> encode, Func<TPayload, TFact> decode) where TFact : notnull where TPayload : notnull
    {
        ArgumentNullException.ThrowIfNull(typeInfo); ArgumentNullException.ThrowIfNull(encode); ArgumentNullException.ThrowIfNull(decode);
        return new(typeId,
            fact => Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(encode(fact), typeInfo)),
            payload => decode(JsonSerializer.Deserialize(Convert.FromBase64String(payload), typeInfo)
                ?? throw new InvalidOperationException("The persisted fact payload decoded to null.")));
    }
}

/// <summary>Implements the frozen Payments owner port solely through HPD.Base registered operations and queries.</summary>
public sealed class BaseOwnerPersistencePort<TFact> : IOwnerPersistencePort<TFact> where TFact : notnull
{
    private readonly BaseSession _session;
    private readonly PaymentsFactJsonCodec<TFact> _codec;

    /// <summary>Creates a provider-neutral bridge over one principal-bound Base session.</summary>
    public BaseOwnerPersistencePort(BaseSession session, PaymentsFactJsonCodec<TFact> codec)
        => (_session, _codec) = (session ?? throw new ArgumentNullException(nameof(session)), codec ?? throw new ArgumentNullException(nameof(codec)));

    /// <inheritdoc />
    public async ValueTask<OwnerAppendReceipt<TFact>> CompareBindAppendAsync(OwnerAppendRequest<TFact> request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); cancellationToken.ThrowIfCancellationRequested();
        if (request.Domain.Kind != AtomicDomainKind.Local)
            return new(request.ExpectedOwner, OwnerAppendDisposition.Unsupported, request.ExpectedOwner.Generation, default, "e-local-only");
        if (request.ExpectedOwner.Generation.Value >= long.MaxValue)
            return new(request.ExpectedOwner, OwnerAppendDisposition.Rejected, request.ExpectedOwner.Generation, default, "generation-exhausted");

        string ownerKey = OwnerKey(request.ExpectedOwner);
        string digest = request.SemanticDigest.ToCanonicalText();
        string payload = _codec.Encode(request.Fact);
        long resultGeneration = checked((long)request.ExpectedOwner.Generation.Value + 1);
        var command = new AppendOwnerFactRequest
        {
            OwnerKey = ownerKey,
            EventId = EventId(ownerKey, resultGeneration),
            SemanticDigest = digest,
            FactType = _codec.TypeId,
            Payload = payload,
            ExpectedGeneration = request.ExpectedOwner.Generation.Value == 1 ? null : ParseBaseGeneration(request.ExpectedOwner.Generation.Value - 1),
            ResultGeneration = GenerationText((ulong)resultGeneration),
        };
        BaseMutationRequestIdentity identity = BaseMutationRequestIdentity.Create(
            "hpd.payments", "owner-fact-append", IdempotencyKey(ownerKey, digest),
            BaseMutationRequestFingerprint.Create(Fingerprint(ownerKey, digest, _codec.TypeId, payload, request.ExpectedOwner.Generation.Value)));
        BaseResult<BaseModuleMutationExecutionResult<AppendOwnerFactResult>> result = await _session.ModuleMutations
            .Get(PaymentsOwnerFactMutation.Identity).ExecuteAsync(command, identity, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result is BaseSuccess<BaseModuleMutationExecutionResult<AppendOwnerFactResult>> success)
        {
            ulong baseGeneration = ulong.Parse(success.Value.Result.Generation.ToCanonicalString(), NumberStyles.None, CultureInfo.InvariantCulture);
            if (baseGeneration + 1 != (ulong)resultGeneration) throw new InvalidOperationException("Base and Payments owner generations diverged.");
            OwnerGeneration observed = OwnerGeneration.Create((ulong)resultGeneration);
            OwnerReference owner = new(request.ExpectedOwner.Authority, request.ExpectedOwner.SubjectId, observed);
            OwnerAppendDisposition disposition = success.Value.Disposition == BaseMutationRequestDisposition.Duplicate
                ? OwnerAppendDisposition.Replay : OwnerAppendDisposition.Appended;
            return new(owner, disposition, observed, request.Fact, disposition == OwnerAppendDisposition.Replay ? "replay" : "appended");
        }

        var failure = (BaseFailure<BaseModuleMutationExecutionResult<AppendOwnerFactResult>>)result;
        OwnerGeneration observedGeneration = await ReadObservedGenerationAsync(ownerKey, request.ExpectedOwner.Generation, cancellationToken).ConfigureAwait(false);
        OwnerReference observedOwner = new(request.ExpectedOwner.Authority, request.ExpectedOwner.SubjectId, observedGeneration);
        OwnerAppendDisposition failedDisposition = failure.Error.Category == ErrorCategory.Conflict
            || string.Equals(failure.Error.Code, BaseMutationRequestErrorCodes.FingerprintConflict, StringComparison.Ordinal)
            || string.Equals(failure.Error.Code, "base.subject.providerContractInvalid", StringComparison.Ordinal)
            ? OwnerAppendDisposition.Conflict : failure.Status switch
        {
            OperationStatus.Conflict => OwnerAppendDisposition.Conflict,
            OperationStatus.Unsupported or OperationStatus.CapabilityUnavailable => OwnerAppendDisposition.Unsupported,
            OperationStatus.StoreError => OwnerAppendDisposition.Indeterminate,
            _ => OwnerAppendDisposition.Rejected,
        };
        return new(observedOwner, failedDisposition, observedGeneration, default, FailureCode(failure));
    }

    /// <inheritdoc />
    public async ValueTask<OwnerHistoryPage<TFact>> ReadHistoryAsync(OwnerHistoryRequest request, ReadOnlyMemory<byte> continuation = default, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request); cancellationToken.ThrowIfCancellationRequested();
        string ownerKey = OwnerKey(request.Owner);
        ulong cutValue = request.Frame.OwnerCuts.Single(x => x.Owner == request.Owner).Owner.Generation.Value;
        if (cutValue > long.MaxValue) throw new ArgumentOutOfRangeException(nameof(request), "The Base bridge supports generations through Int64.MaxValue.");
        BaseQuery<PaymentsOwnerFactEvent> query = _session.Collection(PaymentsOwnerFactEvent.Collection).Query()
            .Where(PaymentsOwnerFactEvent.Fields.OwnerKey, ownerKey).Take(4096);
        BaseRecord<PaymentsOwnerFactEvent>[] all = (await query.ToArrayAsync(4096, cancellationToken).ConfigureAwait(false)).RequireValue()
            .Where(item => ParseGeneration(item.Value.Generation) <= cutValue)
            .OrderBy(item => ParseGeneration(item.Value.Generation)).ThenBy(item => item.Id.ToString(), StringComparer.Ordinal).ToArray();
        ulong continuationShape = ContinuationShape(ownerKey, cutValue, request.MaximumFacts);
        int offset = DecodeOffset(continuation.Span, continuationShape);
        if (offset < 0 || offset >= all.Length) throw new KeyNotFoundException("No owner facts exist at the requested historical cut or continuation.");
        BaseRecord<PaymentsOwnerFactEvent>[] items = all.Skip(offset).Take(request.MaximumFacts).ToArray();
        var facts = new TFact[items.Length];
        for (int index = 0; index < facts.Length; index++)
        {
            PaymentsOwnerFactEvent item = items[index].Value;
            if (!string.Equals(item.FactType, _codec.TypeId, StringComparison.Ordinal)) throw new InvalidOperationException("Persisted fact codec identity does not match the requested port.");
            facts[index] = _codec.Decode(item.Payload);
        }
        int nextOffset = offset + items.Length;
        return new(facts, OwnerGeneration.Create(ParseGeneration(items[^1].Value.Generation)),
            nextOffset < all.Length ? EncodeOffset(nextOffset, continuationShape) : []);
    }

    private async ValueTask<OwnerGeneration> ReadObservedGenerationAsync(string ownerKey, OwnerGeneration fallback, CancellationToken cancellationToken)
    {
        BaseResult<BaseRecord<PaymentsOwnerFactHead>> result = await _session.Collection(PaymentsOwnerFactHead.Collection)
            .GetAsync(RecordId.Create(ownerKey), cancellationToken).ConfigureAwait(false);
        return result is BaseSuccess<BaseRecord<PaymentsOwnerFactHead>> success
            ? OwnerGeneration.Create(ParseGeneration(success.Value.Value.Generation)) : fallback;
    }

    private static BaseModuleGeneration ParseBaseGeneration(ulong value)
    {
        byte[] json = Encoding.UTF8.GetBytes("\"" + value.ToString(CultureInfo.InvariantCulture) + "\"");
        var reader = new Utf8JsonReader(json); reader.Read();
        return new BaseModuleGenerationJsonConverter().Read(ref reader, typeof(BaseModuleGeneration), new JsonSerializerOptions());
    }

    private static string OwnerKey(OwnerReference owner)
    {
        byte[] authority = Encoding.UTF8.GetBytes(((int)owner.Authority).ToString(CultureInfo.InvariantCulture) + ":");
        byte[] subject = owner.SubjectId.GetCanonicalBytes();
        byte[] input = new byte[authority.Length + subject.Length]; authority.CopyTo(input, 0); subject.CopyTo(input, authority.Length);
        return "o-" + Convert.ToHexString(SHA256.HashData(input));
    }

    private static string EventId(string ownerKey, long generation) => ownerKey + "-" + generation.ToString("D20", CultureInfo.InvariantCulture);
    private static string GenerationText(ulong generation) => generation.ToString("D20", CultureInfo.InvariantCulture);
    private static ulong ParseGeneration(string value) => ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ulong generation)
        && string.Equals(value, GenerationText(generation), StringComparison.Ordinal) && generation > 0
            ? generation : throw new InvalidOperationException("Persisted Payments generation is not canonical.");
    private static ulong ContinuationShape(string ownerKey, ulong cut, int maximumFacts) => BinaryPrimitives.ReadUInt64BigEndian(
        SHA256.HashData(Encoding.UTF8.GetBytes(ownerKey + "\n" + cut.ToString(CultureInfo.InvariantCulture) + "\n"
            + maximumFacts.ToString(CultureInfo.InvariantCulture))));
    private static byte[] EncodeOffset(int value, ulong shape)
    {
        byte[] token = new byte[12];
        BinaryPrimitives.WriteInt32BigEndian(token, value);
        BinaryPrimitives.WriteUInt64BigEndian(token.AsSpan(4), shape);
        return token;
    }
    private static int DecodeOffset(ReadOnlySpan<byte> value, ulong shape)
    {
        if (value.IsEmpty) return 0;
        if (value.Length != 12 || BinaryPrimitives.ReadUInt64BigEndian(value[4..]) != shape)
            throw new ArgumentException("History continuation does not belong to this exact request.", nameof(value));
        int offset = BinaryPrimitives.ReadInt32BigEndian(value);
        return offset >= 0 ? offset : throw new ArgumentException("History continuation offset is invalid.", nameof(value));
    }
    private static string IdempotencyKey(string ownerKey, string digest) => "i-" + Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(ownerKey + "\n" + digest)));
    private static byte[] Fingerprint(string ownerKey, string digest, string type, string payload, ulong expected) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', ownerKey, digest, type, payload, expected.ToString(CultureInfo.InvariantCulture))));
    private static string FailureCode(BaseFailure<BaseModuleMutationExecutionResult<AppendOwnerFactResult>> failure) =>
        failure.Error.Category == ErrorCategory.Conflict || failure.Status == OperationStatus.Conflict
            || string.Equals(failure.Error.Code, BaseMutationRequestErrorCodes.FingerprintConflict, StringComparison.Ordinal)
            || string.Equals(failure.Error.Code, "base.subject.providerContractInvalid", StringComparison.Ordinal) ? "generation-conflict" : failure.Status switch
        {
            OperationStatus.Unsupported or OperationStatus.CapabilityUnavailable => "base-unsupported",
            OperationStatus.StoreError => "base-indeterminate",
            _ => "base-rejected",
        };
}
