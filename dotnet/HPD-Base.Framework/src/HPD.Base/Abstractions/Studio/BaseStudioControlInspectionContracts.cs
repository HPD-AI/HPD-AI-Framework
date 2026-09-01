using System.Collections.Immutable;

namespace HPD.Base;

/// <summary>Identifies one closed durable control-plane fact family available to Studio inspection.</summary>
public enum BaseStudioControlFactKind : byte
{
    /// <summary>An L30, L43, L50, or atomic activation-creation receipt.</summary>
    AtomicReceipt = 1,
    /// <summary>An L51 identified transition receipt.</summary>
    ActivationReceipt,
    /// <summary>Current activation control authority.</summary>
    Activation,
    /// <summary>Current durable schedule authority.</summary>
    Schedule,
    /// <summary>Immutable schedule occurrence authority.</summary>
    Occurrence,
    /// <summary>Current executor-incarnation authority.</summary>
    Executor,
    /// <summary>Current at-most-once effect authority.</summary>
    Effect,
    /// <summary>Current retained non-cooperative activation work.</summary>
    Quarantine,
    /// <summary>Current exported-subject contract publication authority.</summary>
    SubjectContract,
    /// <summary>Current exported-subject lifetime authority.</summary>
    Subject,
    /// <summary>Current installed lifecycle-consumer projection authority.</summary>
    LifecycleConsumer,
    /// <summary>Current independent lifecycle checkpoint authority.</summary>
    LifecycleCheckpoint,
    /// <summary>Current coordinated-retirement barrier authority.</summary>
    RetirementBarrier,
}

/// <summary>Contains independent provider-neutral limits for one control inspection.</summary>
public sealed record BaseStudioControlInspectionLimits
{
    /// <summary>Gets the maximum facts returned.</summary>
    public required int MaximumItems { get; init; }
    /// <summary>Gets the maximum provider rows examined.</summary>
    public required long MaximumRowsRead { get; init; }
    /// <summary>Gets the maximum canonical evidence bytes.</summary>
    public required long MaximumEvidenceBytes { get; init; }
    /// <summary>Gets the maximum transient bytes.</summary>
    public required long MaximumTransientBytes { get; init; }
    /// <summary>Gets the complete operation deadline.</summary>
    public required TimeSpan Deadline { get; init; }
}

/// <summary>Requests an exact identity or canonical finite page of one closed fact family.</summary>
public sealed record BaseStudioControlInspectionRequest
{
    /// <summary>Gets the installed application identity.</summary>
    public required string ApplicationId { get; init; }
    /// <summary>Gets the requested closed fact kind.</summary>
    public required BaseStudioControlFactKind Kind { get; init; }
    /// <summary>Gets an exact identity, or <see langword="null"/> for a bounded inventory page.</summary>
    public string? Identity { get; init; }
    /// <summary>Gets the exclusive ordinal identity boundary.</summary>
    public string? AfterIdentity { get; init; }
    /// <summary>Gets the exact safe receipt-subject kind filter, when reading activation receipts.</summary>
    public string? SubjectKind { get; init; }
    /// <summary>Gets the exact safe receipt-subject identity filter, when reading activation receipts.</summary>
    public string? SubjectIdentity { get; init; }
    /// <summary>Gets the requested maximum page size.</summary>
    public required int Take { get; init; }
    /// <summary>Gets the purpose-protected scope checksum.</summary>
    public required ImmutableArray<byte> ProtectedScopeChecksum { get; init; }
    /// <summary>Gets exact intersected limits.</summary>
    public required BaseStudioControlInspectionLimits Limits { get; init; }
}

/// <summary>Base type for one deeply owned, value-safe control-plane inspection fact.</summary>
public abstract record BaseStudioControlFact
{
    private protected BaseStudioControlFact() { }
    /// <summary>Gets the closed fact kind.</summary>
    public abstract BaseStudioControlFactKind Kind { get; }
    /// <summary>Gets the canonical identity used for exact lookup and paging.</summary>
    public required string Identity { get; init; }
    /// <summary>Gets the purpose-bound fact checksum.</summary>
    public required ImmutableArray<byte> FactChecksum { get; init; }
}

/// <summary>Projects one retained atomic receipt without protected request or result values.</summary>
public sealed record BaseStudioAtomicReceiptFact : BaseStudioControlFact
{
    /// <inheritdoc />
    public override BaseStudioControlFactKind Kind => BaseStudioControlFactKind.AtomicReceipt;
    /// <summary>Gets the closed committed result family.</summary>
    public required BaseAtomicReceiptResultKind ResultKind { get; init; }
    /// <summary>Gets receipt expiry.</summary>
    public required DateTimeOffset ExpiresAtUtc { get; init; }
    /// <summary>Gets the request fingerprint.</summary>
    public required ImmutableArray<byte> RequestFingerprint { get; init; }
    /// <summary>Gets the structural result digest.</summary>
    public required ImmutableArray<byte> StructuralDigest { get; init; }
}

/// <summary>Projects one retained L51 receipt without protected result bytes.</summary>
public sealed record BaseStudioActivationReceiptFact : BaseStudioControlFact
{
    /// <inheritdoc />
    public override BaseStudioControlFactKind Kind => BaseStudioControlFactKind.ActivationReceipt;
    /// <summary>Gets the closed transition kind.</summary>
    public required string TransitionKind { get; init; }
    /// <summary>Gets the request fingerprint.</summary>
    public required ImmutableArray<byte> RequestFingerprint { get; init; }
    /// <summary>Gets the canonical result digest.</summary>
    public required ImmutableArray<byte> ResultDigest { get; init; }
    /// <summary>Gets the activation identity when the retained receipt is activation-bound.</summary>
    public string? ActivationId { get; init; }
}

/// <summary>Projects current activation and attempt authority without input/result values or fences.</summary>
public sealed record BaseStudioActivationFact : BaseStudioControlFact
{
    /// <inheritdoc />
    public override BaseStudioControlFactKind Kind => BaseStudioControlFactKind.Activation;
    /// <summary>Gets the definition identity.</summary>
    public required string DefinitionId { get; init; }
    /// <summary>Gets the definition version.</summary>
    public required int DefinitionVersion { get; init; }
    /// <summary>Gets the current state.</summary>
    public required BaseActivationState State { get; init; }
    /// <summary>Gets current control generation.</summary>
    public required long Generation { get; init; }
    /// <summary>Gets current attempt number.</summary>
    public required int AttemptNumber { get; init; }
    /// <summary>Gets current claim epoch.</summary>
    public required long ClaimEpoch { get; init; }
    /// <summary>Gets effective due time in canonical accepted-time ticks.</summary>
    public required long EffectiveDueAt { get; init; }
    /// <summary>Gets optional occurrence identity.</summary>
    public string? OccurrenceId { get; init; }
    /// <summary>Gets whether an effect authority exists.</summary>
    public required bool HasEffect { get; init; }
}

/// <summary>Projects current schedule authority.</summary>
public sealed record BaseStudioScheduleFact : BaseStudioControlFact
{
    /// <inheritdoc />
    public override BaseStudioControlFactKind Kind => BaseStudioControlFactKind.Schedule;
    /// <summary>Gets installed schedule version.</summary>
    public required int Version { get; init; }
    /// <summary>Gets definition generation.</summary>
    public required long DefinitionGeneration { get; init; }
    /// <summary>Gets whether future occurrence materialization is enabled.</summary>
    public required bool Enabled { get; init; }
    /// <summary>Gets semantic schedule epoch.</summary>
    public required long ScheduleEpoch { get; init; }
    /// <summary>Gets next nominal accepted-time tick, when any.</summary>
    public long? NextNominal { get; init; }
}

/// <summary>Projects one immutable occurrence disposition.</summary>
public sealed record BaseStudioOccurrenceFact : BaseStudioControlFact
{
    /// <inheritdoc />
    public override BaseStudioControlFactKind Kind => BaseStudioControlFactKind.Occurrence;
    /// <summary>Gets owning schedule identity.</summary>
    public required string ScheduleId { get; init; }
    /// <summary>Gets owning schedule epoch.</summary>
    public required long ScheduleEpoch { get; init; }
    /// <summary>Gets nominal accepted-time tick.</summary>
    public required long NominalAt { get; init; }
    /// <summary>Gets effective accepted-time tick.</summary>
    public required long EffectiveAt { get; init; }
    /// <summary>Gets the closed disposition name.</summary>
    public required string Disposition { get; init; }
    /// <summary>Gets linked activation identity when materialized.</summary>
    public string? ActivationId { get; init; }
}

/// <summary>Projects one executor incarnation and its latest heartbeat without worker-secret authority.</summary>
public sealed record BaseStudioExecutorFact : BaseStudioControlFact
{
    /// <inheritdoc />
    public override BaseStudioControlFactKind Kind => BaseStudioControlFactKind.Executor;
    /// <summary>Gets host identity.</summary>
    public required string HostId { get; init; }
    /// <summary>Gets process-incarnation identity.</summary>
    public required string ProcessIncarnationId { get; init; }
    /// <summary>Gets executor generation.</summary>
    public required long ExecutorGeneration { get; init; }
    /// <summary>Gets current heartbeat revision.</summary>
    public required long HeartbeatRevision { get; init; }
    /// <summary>Gets heartbeat expiry.</summary>
    public required long HeartbeatExpiresAt { get; init; }
    /// <summary>Gets whether the incarnation is retired.</summary>
    public required bool Retired { get; init; }
}

/// <summary>Projects current at-most-once effect ownership without fencing tokens.</summary>
public sealed record BaseStudioEffectFact : BaseStudioControlFact
{
    /// <inheritdoc />
    public override BaseStudioControlFactKind Kind => BaseStudioControlFactKind.Effect;
    /// <summary>Gets owning activation identity.</summary>
    public required string ActivationId { get; init; }
    /// <summary>Gets the positive activation attempt that owns the effect.</summary>
    public required int AttemptNumber { get; init; }
    /// <summary>Gets effect-start generation.</summary>
    public required long EffectStartGeneration { get; init; }
    /// <summary>Gets executor generation.</summary>
    public required long ExecutorGeneration { get; init; }
    /// <summary>Gets current heartbeat revision.</summary>
    public required long HeartbeatRevision { get; init; }
    /// <summary>Gets heartbeat expiry.</summary>
    public required long HeartbeatExpiresAt { get; init; }
}

/// <summary>Projects one current durable quarantine fact.</summary>
public sealed record BaseStudioQuarantineFact : BaseStudioControlFact
{
    /// <inheritdoc />
    public override BaseStudioControlFactKind Kind => BaseStudioControlFactKind.Quarantine;
    /// <summary>Gets the exact durable quarantine evidence.</summary>
    public required BaseActivationQuarantineItem Quarantine { get; init; }
}

/// <summary>Projects one current exported-subject contract publication without its private validation plan.</summary>
public sealed record BaseStudioSubjectContractFact : BaseStudioControlFact
{
    /// <inheritdoc />
    public override BaseStudioControlFactKind Kind => BaseStudioControlFactKind.SubjectContract;
    /// <summary>Gets the contract identity.</summary>
    public required string ContractId { get; init; }
    /// <summary>Gets the positive contract version.</summary>
    public required int ContractVersion { get; init; }
    /// <summary>Gets the installed contract checksum.</summary>
    public required string ContractChecksum { get; init; }
    /// <summary>Gets the current authority epoch bytes.</summary>
    public required ImmutableArray<byte> AuthorityEpoch { get; init; }
    /// <summary>Gets the restore epoch.</summary>
    public required long RestoreEpoch { get; init; }
    /// <summary>Gets the current state generation.</summary>
    public required long StateGeneration { get; init; }
    /// <summary>Gets the last publication kind.</summary>
    public required BaseSubjectAuthorityPublicationKind PublicationKind { get; init; }
    /// <summary>Gets the last publication journal position.</summary>
    public required long PublicationPosition { get; init; }
}

/// <summary>Projects one current exported-subject lifetime without its private backing identity.</summary>
public sealed record BaseStudioSubjectFact : BaseStudioControlFact
{
    /// <inheritdoc />
    public override BaseStudioControlFactKind Kind => BaseStudioControlFactKind.Subject;
    /// <summary>Gets the contract identity.</summary>
    public required string ContractId { get; init; }
    /// <summary>Gets the positive contract version.</summary>
    public required int ContractVersion { get; init; }
    /// <summary>Gets the protected canonical subject identity.</summary>
    public required string SubjectId { get; init; }
    /// <summary>Gets the current incarnation bytes.</summary>
    public required ImmutableArray<byte> Incarnation { get; init; }
    /// <summary>Gets the creation journal position.</summary>
    public required long CreatedJournalPosition { get; init; }
}

/// <summary>Projects one installed lifecycle-consumer authority.</summary>
public sealed record BaseStudioLifecycleConsumerFact : BaseStudioControlFact
{
    /// <inheritdoc />
    public override BaseStudioControlFactKind Kind => BaseStudioControlFactKind.LifecycleConsumer;
    /// <summary>Gets the consumer identity.</summary>
    public required string ConsumerId { get; init; }
    /// <summary>Gets the consumer version.</summary>
    public required int ConsumerVersion { get; init; }
    /// <summary>Gets the consumer checksum.</summary>
    public required string ConsumerChecksum { get; init; }
    /// <summary>Gets the owning contract identity.</summary>
    public required string ContractId { get; init; }
    /// <summary>Gets the contract version.</summary>
    public required int ContractVersion { get; init; }
    /// <summary>Gets the projection generation.</summary>
    public required long ProjectionGeneration { get; init; }
    /// <summary>Gets the published graph generation.</summary>
    public required long PublishedGraphGeneration { get; init; }
    /// <summary>Gets the delivery epoch.</summary>
    public required long DeliveryEpoch { get; init; }
}

/// <summary>Projects one independent lifecycle checkpoint authority.</summary>
public sealed record BaseStudioLifecycleCheckpointFact : BaseStudioControlFact
{
    /// <inheritdoc />
    public override BaseStudioControlFactKind Kind => BaseStudioControlFactKind.LifecycleCheckpoint;
    /// <summary>Gets the consumer identity.</summary>
    public required string ConsumerId { get; init; }
    /// <summary>Gets the consumer version.</summary>
    public required int ConsumerVersion { get; init; }
    /// <summary>Gets the contract identity.</summary>
    public required string ContractId { get; init; }
    /// <summary>Gets the contract version.</summary>
    public required int ContractVersion { get; init; }
    /// <summary>Gets the protected scope identity.</summary>
    public required string ProtectedScopeIdentity { get; init; }
    /// <summary>Gets the projection generation.</summary>
    public required long ProjectionGeneration { get; init; }
    /// <summary>Gets the checkpoint generation.</summary>
    public required long CheckpointGeneration { get; init; }
    /// <summary>Gets the canonical through boundary.</summary>
    public required string ThroughBoundary { get; init; }
    /// <summary>Gets whether retained history overtook the checkpoint.</summary>
    public required bool Overtaken { get; init; }
}

/// <summary>Projects one current coordinated-retirement barrier authority.</summary>
public sealed record BaseStudioRetirementBarrierFact : BaseStudioControlFact
{
    /// <inheritdoc />
    public override BaseStudioControlFactKind Kind => BaseStudioControlFactKind.RetirementBarrier;
    /// <summary>Gets the contract identity.</summary>
    public required string ContractId { get; init; }
    /// <summary>Gets the contract version.</summary>
    public required int ContractVersion { get; init; }
    /// <summary>Gets the protected subject identity.</summary>
    public required string ProtectedSubjectIdentity { get; init; }
    /// <summary>Gets the authority epoch.</summary>
    public required string AuthorityEpoch { get; init; }
    /// <summary>Gets the incarnation.</summary>
    public required string Incarnation { get; init; }
    /// <summary>Gets the tombstone sequence.</summary>
    public required long TombstoneSequence { get; init; }
    /// <summary>Gets the required-consumer checksum.</summary>
    public required string RequiredConsumerSetChecksum { get; init; }
    /// <summary>Gets the deadline.</summary>
    public required DateTimeOffset DeadlineUtc { get; init; }
    /// <summary>Gets the barrier state.</summary>
    public required BaseSubjectRetirementBarrierState State { get; init; }
    /// <summary>Gets the barrier generation.</summary>
    public required long Generation { get; init; }
    /// <summary>Gets the barrier checksum.</summary>
    public required string BarrierChecksum { get; init; }
}

/// <summary>Contains one finite canonical provider-neutral control fact page.</summary>
public sealed record BaseStudioControlInspectionPage
{
    /// <summary>Gets facts in ordinal identity order.</summary>
    public required ImmutableArray<BaseStudioControlFact> Items { get; init; }
    /// <summary>Gets the next exclusive identity, when more facts exist.</summary>
    public string? NextIdentity { get; init; }
    /// <summary>Gets rows examined.</summary>
    public required long RowsRead { get; init; }
    /// <summary>Gets canonical evidence bytes.</summary>
    public required long EvidenceBytes { get; init; }
    /// <summary>Gets transient bytes.</summary>
    public required long TransientBytes { get; init; }
    /// <summary>Gets the complete page checksum.</summary>
    public required ImmutableArray<byte> PageChecksum { get; init; }
}

/// <summary>Provides bounded provider-neutral control inspection without exposing provider internals.</summary>
public interface IBaseStudioControlInspectionStore
{
    /// <summary>Reads one exact or finite page of durable control facts.</summary>
    ValueTask<OperationResult<BaseStudioControlInspectionPage>> ReadStudioControlFactsAsync(
        BaseStudioControlInspectionRequest request, CancellationToken cancellationToken = default);
}

/// <summary>Defines canonical validation, measurement, and checksum rules for control inspection.</summary>
public static class BaseStudioControlInspectionContract
{
    /// <summary>Encodes one atomic receipt's logical identity.</summary>
    public static string AtomicIdentity(string scope, string operation, string key) => EncodeIdentity("atomic", scope, operation, key);
    /// <summary>Decodes one canonical atomic receipt identity.</summary>
    public static bool TryDecodeAtomicIdentity(string value, out string scope, out string operation, out string key)
    { if (TryDecodeIdentity(value, "atomic", 4, out object[] parts) && parts[1] is string s && parts[2] is string o && parts[3] is string k)
      { scope = s; operation = o; key = k; return true; } scope = operation = key = ""; return false; }
    /// <summary>Encodes one schedule's logical identity.</summary>
    public static string ScheduleIdentity(string id, int version) => EncodeIdentity("schedule", id, version);
    /// <summary>Decodes one canonical schedule identity.</summary>
    public static bool TryDecodeScheduleIdentity(string value, out string id, out int version)
    { if (TryDecodeIdentity(value, "schedule", 3, out object[] parts) && parts[1] is string i && parts[2] is int v && v > 0)
      { id = i; version = v; return true; } id = ""; version = 0; return false; }
    /// <summary>Encodes one executor's logical identity; generation remains fact evidence.</summary>
    public static string ExecutorIdentity(string application, string host, string process) => EncodeIdentity("executor", application, host, process);
    /// <summary>Decodes one canonical executor logical identity.</summary>
    public static bool TryDecodeExecutorIdentity(string value, out string application, out string host, out string process)
    { if (TryDecodeIdentity(value, "executor", 4, out object[] parts) && parts[1] is string a && parts[2] is string h && parts[3] is string p)
      { application = a; host = h; process = p; return true; } application = host = process = ""; return false; }
    /// <summary>Encodes one exported subject contract identity.</summary>
    public static string SubjectContractIdentity(string contractId, int version) => EncodeIdentity("subjectContract", contractId, version);
    /// <summary>Encodes one current exported subject identity.</summary>
    public static string SubjectIdentity(string contractId, int version, string subjectId) => EncodeIdentity("subject", contractId, version, subjectId);
    /// <summary>Encodes one lifecycle-consumer logical identity.</summary>
    public static string LifecycleConsumerIdentity(string consumerId, int version) => EncodeIdentity("lifecycleConsumer", consumerId, version);
    /// <summary>Encodes one lifecycle-checkpoint logical identity.</summary>
    public static string LifecycleCheckpointIdentity(string consumerId, int version, string scopeDigest) => EncodeIdentity("lifecycleCheckpoint", consumerId, version, scopeDigest);
    /// <summary>Encodes one retirement-barrier logical identity.</summary>
    public static string RetirementBarrierIdentity(string contractId, int version, string subjectId, string authorityEpoch, string incarnation)
        => EncodeIdentity("retirementBarrier", contractId, version, subjectId, authorityEpoch, incarnation);

    /// <summary>Returns whether a request is closed and independently bounded.</summary>
    public static bool IsValid(BaseStudioControlInspectionRequest? value) => value is not null &&
        !string.IsNullOrWhiteSpace(value.ApplicationId) && Enum.IsDefined(value.Kind) && value.Take is >= 1 and <= 1_024 &&
        value.ProtectedScopeChecksum.Length == 32 && !(value.Identity is not null && value.AfterIdentity is not null) &&
        ((value.SubjectKind is null && value.SubjectIdentity is null) || value.Kind == BaseStudioControlFactKind.ActivationReceipt &&
            value.SubjectKind is not null && value.SubjectIdentity is not null && ValidId(value.SubjectKind) && ValidId(value.SubjectIdentity)) &&
        (value.Identity is null || value.Take == 1 && value.AfterIdentity is null && ValidIdentity(value.Kind, value.Identity, value.ApplicationId)) &&
        (value.AfterIdentity is null || ValidIdentity(value.Kind, value.AfterIdentity, value.ApplicationId)) && value.Limits is { MaximumItems: >= 1 and <= 1_024, MaximumRowsRead: >= 1 and <= 1_000_000,
            MaximumEvidenceBytes: >= 1 and <= 67_108_864, MaximumTransientBytes: >= 1 and <= 67_108_864 } &&
        value.Take <= value.Limits.MaximumItems && value.Limits.Deadline > TimeSpan.Zero && value.Limits.Deadline <= TimeSpan.FromMinutes(1);

    /// <summary>Computes the purpose-bound checksum for one exact fact.</summary>
    public static ImmutableArray<byte> FactChecksum(BaseStudioControlFact fact)
    {
        ArgumentNullException.ThrowIfNull(fact);
        return Hash("fact", ((byte)fact.Kind).ToString(System.Globalization.CultureInfo.InvariantCulture), fact.Identity,
            fact switch
            {
                BaseStudioAtomicReceiptFact x => $"{(int)x.ResultKind}|{Utc(x.ExpiresAtUtc)}|{Hex(x.RequestFingerprint)}|{Hex(x.StructuralDigest)}",
                BaseStudioActivationReceiptFact x => $"{x.TransitionKind}|{Hex(x.RequestFingerprint)}|{Hex(x.ResultDigest)}|{x.ActivationId}",
                BaseStudioActivationFact x => $"{x.DefinitionId}|{x.DefinitionVersion}|{(int)x.State}|{x.Generation}|{x.AttemptNumber}|{x.ClaimEpoch}|{x.EffectiveDueAt}|{x.OccurrenceId}|{x.HasEffect}",
                BaseStudioScheduleFact x => $"{x.Version}|{x.DefinitionGeneration}|{x.Enabled}|{x.ScheduleEpoch}|{x.NextNominal}",
                BaseStudioOccurrenceFact x => $"{x.ScheduleId}|{x.ScheduleEpoch}|{x.NominalAt}|{x.EffectiveAt}|{x.Disposition}|{x.ActivationId}",
                BaseStudioExecutorFact x => $"{x.HostId}|{x.ProcessIncarnationId}|{x.ExecutorGeneration}|{x.HeartbeatRevision}|{x.HeartbeatExpiresAt}|{x.Retired}",
                BaseStudioEffectFact x => $"{x.ActivationId}|{x.AttemptNumber}|{x.EffectStartGeneration}|{x.ExecutorGeneration}|{x.HeartbeatRevision}|{x.HeartbeatExpiresAt}",
                BaseStudioQuarantineFact x => $"{x.Quarantine.Sequence}|{x.Quarantine.Operation}|{Utc(x.Quarantine.RetainedAt)}",
                BaseStudioSubjectContractFact x => $"{x.ContractId}|{x.ContractVersion}|{x.ContractChecksum}|{Hex(x.AuthorityEpoch)}|{x.RestoreEpoch}|{x.StateGeneration}|{(int)x.PublicationKind}|{x.PublicationPosition}",
                BaseStudioSubjectFact x => $"{x.ContractId}|{x.ContractVersion}|{x.SubjectId}|{Hex(x.Incarnation)}|{x.CreatedJournalPosition}",
                BaseStudioLifecycleConsumerFact x => $"{x.ConsumerId}|{x.ConsumerVersion}|{x.ConsumerChecksum}|{x.ContractId}|{x.ContractVersion}|{x.ProjectionGeneration}|{x.PublishedGraphGeneration}|{x.DeliveryEpoch}",
                BaseStudioLifecycleCheckpointFact x => $"{x.ConsumerId}|{x.ConsumerVersion}|{x.ContractId}|{x.ContractVersion}|{x.ProtectedScopeIdentity}|{x.ProjectionGeneration}|{x.CheckpointGeneration}|{x.ThroughBoundary}|{x.Overtaken}",
                BaseStudioRetirementBarrierFact x => $"{x.ContractId}|{x.ContractVersion}|{x.ProtectedSubjectIdentity}|{x.AuthorityEpoch}|{x.Incarnation}|{x.TombstoneSequence}|{x.RequiredConsumerSetChecksum}|{Utc(x.DeadlineUtc)}|{(int)x.State}|{x.Generation}|{x.BarrierChecksum}",
                _ => throw new ArgumentException("Unknown Studio control fact.", nameof(fact)),
            });
    }

    /// <summary>Measures one fact's canonical provider evidence bytes.</summary>
    public static long Measure(BaseStudioControlFact fact) => checked(64L + System.Text.Encoding.UTF8.GetByteCount(fact.Identity) +
        fact switch
        {
            BaseStudioAtomicReceiptFact x => x.RequestFingerprint.Length + x.StructuralDigest.Length + 24,
            BaseStudioActivationReceiptFact x => x.RequestFingerprint.Length + x.ResultDigest.Length +
                System.Text.Encoding.UTF8.GetByteCount(x.TransitionKind) + System.Text.Encoding.UTF8.GetByteCount(x.ActivationId ?? "") + 8,
            BaseStudioActivationFact x => System.Text.Encoding.UTF8.GetByteCount(x.DefinitionId) + System.Text.Encoding.UTF8.GetByteCount(x.OccurrenceId ?? "") + 64,
            BaseStudioScheduleFact => 48,
            BaseStudioOccurrenceFact x => System.Text.Encoding.UTF8.GetByteCount(x.ScheduleId) + System.Text.Encoding.UTF8.GetByteCount(x.Disposition) + System.Text.Encoding.UTF8.GetByteCount(x.ActivationId ?? "") + 40,
            BaseStudioExecutorFact x => System.Text.Encoding.UTF8.GetByteCount(x.HostId) + System.Text.Encoding.UTF8.GetByteCount(x.ProcessIncarnationId) + 40,
            BaseStudioEffectFact x => System.Text.Encoding.UTF8.GetByteCount(x.ActivationId) + 40,
            BaseStudioQuarantineFact x => System.Text.Encoding.UTF8.GetByteCount(x.Quarantine.Operation) + 24,
            BaseStudioSubjectContractFact x => System.Text.Encoding.UTF8.GetByteCount(x.ContractId) + System.Text.Encoding.UTF8.GetByteCount(x.ContractChecksum) + x.AuthorityEpoch.Length + 40,
            BaseStudioSubjectFact x => System.Text.Encoding.UTF8.GetByteCount(x.ContractId) + System.Text.Encoding.UTF8.GetByteCount(x.SubjectId) + x.Incarnation.Length + 24,
            BaseStudioLifecycleConsumerFact x => System.Text.Encoding.UTF8.GetByteCount(x.ConsumerId + x.ConsumerChecksum + x.ContractId) + 48,
            BaseStudioLifecycleCheckpointFact x => System.Text.Encoding.UTF8.GetByteCount(x.ConsumerId + x.ContractId + x.ProtectedScopeIdentity + x.ThroughBoundary) + 48,
            BaseStudioRetirementBarrierFact x => System.Text.Encoding.UTF8.GetByteCount(x.ContractId + x.ProtectedSubjectIdentity + x.AuthorityEpoch + x.Incarnation + x.RequiredConsumerSetChecksum + x.BarrierChecksum) + 64,
            _ => throw new ArgumentException("Unknown Studio control fact.", nameof(fact)),
        });

    /// <summary>Computes the checksum over a complete finite page and its accounting.</summary>
    public static ImmutableArray<byte> PageChecksum(IEnumerable<BaseStudioControlFact> facts, string? next, long rows, long evidence, long transient)
        => Hash("page", string.Join(',', facts.Select(static value => Hex(value.FactChecksum))), next,
            rows.ToString(System.Globalization.CultureInfo.InvariantCulture), evidence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            transient.ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>Returns whether a provider page exactly matches the request and canonical checksums.</summary>
    public static bool IsValidResult(BaseStudioControlInspectionRequest request, BaseStudioControlInspectionPage? page)
    {
        if (!IsValid(request) || page is null || page.Items.Length > request.Take || page.RowsRead < page.Items.Length ||
            page.RowsRead > request.Limits.MaximumRowsRead || page.EvidenceBytes > request.Limits.MaximumEvidenceBytes ||
            page.TransientBytes > request.Limits.MaximumTransientBytes || page.Items.Any(item => item.Kind != request.Kind ||
                item is BaseStudioActivationReceiptFact receipt && !ValidId(receipt.ActivationId, optional: true) ||
                item is BaseStudioSubjectContractFact contract && (contract.ContractVersion < 1 || contract.AuthorityEpoch.Length != 16 ||
                    contract.RestoreEpoch < 0 || contract.StateGeneration < 1 || contract.PublicationPosition < 1 ||
                    contract.ContractChecksum.Length != 64 || !Enum.IsDefined(contract.PublicationKind)) ||
                item is BaseStudioSubjectFact subject && (subject.ContractVersion < 1 || subject.Incarnation.Length != 16 || subject.CreatedJournalPosition < 0) ||
                item is BaseStudioLifecycleConsumerFact consumer && (consumer.ConsumerVersion < 1 || consumer.ContractVersion < 1 || consumer.ProjectionGeneration < 1 || consumer.PublishedGraphGeneration < 1 || consumer.DeliveryEpoch < 0) ||
                item is BaseStudioLifecycleCheckpointFact checkpoint && (checkpoint.ConsumerVersion < 1 || checkpoint.ContractVersion < 1 || checkpoint.ProjectionGeneration < 1 || checkpoint.CheckpointGeneration < 1) ||
                item is BaseStudioRetirementBarrierFact barrier && (barrier.ContractVersion < 1 || barrier.TombstoneSequence < 1 || barrier.Generation < 1 || !Enum.IsDefined(barrier.State)) ||
                item.FactChecksum.Length != 32 || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                    item.FactChecksum.AsSpan(), FactChecksum(item).AsSpan())) ||
            !page.Items.Select(static value => value.Identity).SequenceEqual(page.Items.Select(static value => value.Identity).Order(StringComparer.Ordinal)) ||
            page.Items.Select(static value => value.Identity).Distinct(StringComparer.Ordinal).Count() != page.Items.Length ||
            page.EvidenceBytes != page.Items.Sum(Measure) || request.Identity is not null &&
                (page.Items.Length > 1 || page.Items.Any(value => !StringComparer.Ordinal.Equals(value.Identity, request.Identity))) ||
            request.AfterIdentity is not null && page.Items.Any(value => StringComparer.Ordinal.Compare(value.Identity, request.AfterIdentity) <= 0) ||
            request.SubjectKind is not null && page.Items.Any(value => value is not BaseStudioActivationReceiptFact receipt ||
                !StringComparer.Ordinal.Equals(request.SubjectKind, "activation") ||
                !StringComparer.Ordinal.Equals(receipt.ActivationId, request.SubjectIdentity)) ||
            page.NextIdentity is not null && (page.Items.IsEmpty || !StringComparer.Ordinal.Equals(page.NextIdentity, page.Items[^1].Identity))) return false;
        return page.PageChecksum.Length == 32 && System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(page.PageChecksum.AsSpan(),
            PageChecksum(page.Items, page.NextIdentity, page.RowsRead, page.EvidenceBytes, page.TransientBytes).AsSpan());
    }

    private static bool ValidId(string? value, bool optional = false) => value is null ? optional :
        value.Length is >= 1 and <= 512 && !value.Any(char.IsControl) && value.IsNormalized();
    private static bool ValidIdentity(BaseStudioControlFactKind kind, string value, string application) => kind switch
    {
        BaseStudioControlFactKind.AtomicReceipt => TryDecodeAtomicIdentity(value, out _, out _, out _),
        BaseStudioControlFactKind.Schedule => TryDecodeScheduleIdentity(value, out _, out _),
        BaseStudioControlFactKind.Executor => TryDecodeExecutorIdentity(value, out string app, out _, out _) && StringComparer.Ordinal.Equals(app, application),
        _ => ValidId(value),
    };
    private static string EncodeIdentity(string tag, params object[] parts)
    {
        if (parts.Any(static part => part is string text ? !ValidComponent(text) : part is not int number || number <= 0))
            throw new ArgumentException("Studio control identity components are invalid.", nameof(parts));
        using var stream = new System.IO.MemoryStream(); using (var writer = new System.Text.Json.Utf8JsonWriter(stream))
        { writer.WriteStartArray(); writer.WriteStringValue(tag); foreach (object part in parts) { if (part is string text) writer.WriteStringValue(text); else writer.WriteNumberValue((int)part); } writer.WriteEndArray(); }
        return Convert.ToBase64String(stream.ToArray()).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
    private static bool TryDecodeIdentity(string value, string tag, int count, out object[] parts)
    {
        parts = []; if (value.Length is < 1 or > 512 || value.Any(char.IsControl)) return false;
        try
        {
            string padded = value.Replace('-', '+').Replace('_', '/'); padded += new string('=', (4 - padded.Length % 4) % 4);
            using System.Text.Json.JsonDocument document = System.Text.Json.JsonDocument.Parse(Convert.FromBase64String(padded));
            if (document.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array || document.RootElement.GetArrayLength() != count) return false;
            System.Text.Json.JsonElement[] elements = [.. document.RootElement.EnumerateArray()];
            if (elements[0].ValueKind != System.Text.Json.JsonValueKind.String || !StringComparer.Ordinal.Equals(elements[0].GetString(), tag)) return false;
            var decoded = new object[count]; decoded[0] = tag;
            for (int i = 1; i < count; i++)
            {
                if (elements[i].ValueKind == System.Text.Json.JsonValueKind.String)
                { string? text = elements[i].GetString(); if (!ValidComponent(text)) return false; decoded[i] = text!; }
                else if (elements[i].ValueKind == System.Text.Json.JsonValueKind.Number && elements[i].TryGetInt32(out int number)) decoded[i] = number;
                else return false;
            }
            string canonical = EncodeIdentity(tag, decoded[1..]); if (!StringComparer.Ordinal.Equals(canonical, value)) return false;
            parts = decoded; return true;
        }
        catch (Exception exception) when (exception is FormatException or System.Text.Json.JsonException or InvalidCastException) { return false; }
    }
    private static bool ValidComponent(string? value) => value is { Length: >= 1 and <= 256 } && value.IsNormalized() && !value.Any(char.IsControl);
    private static string Utc(DateTimeOffset value) => value.ToUniversalTime().ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", System.Globalization.CultureInfo.InvariantCulture);
    private static string Hex(IEnumerable<byte> value) => Convert.ToHexString(value.ToArray());
    private static ImmutableArray<byte> Hash(params string?[] values)
    {
        using var hash = System.Security.Cryptography.IncrementalHash.CreateHash(System.Security.Cryptography.HashAlgorithmName.SHA256);
        Span<byte> length = stackalloc byte[sizeof(int)];
        foreach (string value in new[] { "base-studio-control-inspection-v1" }.Concat(values.Select(static value => value ?? "")))
        {
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(value);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
            hash.AppendData(length); hash.AppendData(bytes);
        }
        return [.. hash.GetHashAndReset()];
    }
}
