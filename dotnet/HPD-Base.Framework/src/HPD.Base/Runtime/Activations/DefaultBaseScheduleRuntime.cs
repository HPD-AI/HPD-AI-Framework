using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

internal sealed class DefaultBaseScheduleRuntime(
    IRecordStoreRegistry stores,
    IBasePolicyOrchestrator policy,
    BaseActivationAcceptedTimeAuthority acceptedTime,
    BaseActivationRegistry activations,
    BaseTimeZoneRegistry timeZones) : IBaseScheduleRuntime
{
    public async ValueTask<OperationResult<BaseScheduleAuthority>> ReadAsync(
        BaseSession session, BaseScheduleDefinition definition, CancellationToken cancellationToken)
    {
        OperationResult<IBaseActivationProvider> provider = await AuthorizeAsync(
            session, definition, definition.ManageGrantId, BaseOperationKind.ScheduleMutation, cancellationToken).ConfigureAwait(false);
        if (!provider.IsSuccess() || provider.Value is null)
            return CopyFailure<BaseScheduleAuthority, IBaseActivationProvider>(provider);
        return await provider.Value.ReadScheduleAsync(definition.Id, definition.Version, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<OperationResult<BaseScheduleMutationResult>> MutateAsync(
        BaseSession session, BaseScheduleDefinition definition, BaseScheduleMutationKind kind,
        long? expectedGeneration, BaseMutationRequestIdentity identity, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        OperationResult<IBaseActivationProvider> provider = await AuthorizeAsync(
            session, definition, definition.ManageGrantId, BaseOperationKind.ScheduleMutation, cancellationToken).ConfigureAwait(false);
        if (!provider.IsSuccess() || provider.Value is null)
            return CopyFailure<BaseScheduleMutationResult, IBaseActivationProvider>(provider);
        BaseActivationDefinition target = Target(definition);
        return await provider.Value.MutateScheduleAsync(new BaseScheduleMutationRequest
        {
            Kind = kind,
            Definition = BaseScheduleDefinitionBuilder.Create(definition),
            ExpectedDefinitionGeneration = expectedGeneration,
            InitialNextNominal = kind is BaseScheduleMutationKind.Create or BaseScheduleMutationKind.Update
                ? Next(definition, null)
                : null,
            AcceptedTime = acceptedTime.Capture(session.ApplicationId),
            Identity = identity,
            Limits = target.Limits.Provider,
        }, cancellationToken).ConfigureAwait(false);
    }

    public async ValueTask<OperationResult<BaseScheduleMaintenancePage>> AdvanceAsync(
        BaseSession session, BaseScheduleDefinition definition, BaseMutationRequestIdentity identity,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(identity);
        OperationResult<IBaseActivationProvider> provider = await AuthorizeAsync(
            session, definition, definition.MaterializeGrantId, BaseOperationKind.ScheduleMaterialization, cancellationToken).ConfigureAwait(false);
        if (!provider.IsSuccess() || provider.Value is null)
            return CopyFailure<BaseScheduleMaintenancePage, IBaseActivationProvider>(provider);
        OperationResult<BaseScheduleAuthority> current = await provider.Value
            .ReadScheduleAsync(definition.Id, definition.Version, cancellationToken).ConfigureAwait(false);
        if (!current.IsSuccess() || current.Value is null)
            return CopyFailure<BaseScheduleMaintenancePage, BaseScheduleAuthority>(current);
        if (!CryptographicOperations.FixedTimeEquals(current.Value.Definition.Checksum.AsSpan(), definition.Checksum.AsSpan()))
            return Failure<BaseScheduleMaintenancePage>(OperationStatus.Conflict, "base.activation.scheduleChanged", ErrorCategory.Conflict);

        BaseActivationDefinition target = Target(definition);
        BaseAcceptedTimeReceipt time = acceptedTime.Capture(session.ApplicationId);
        if (!current.Value.Enabled || current.Value.NextNominal is null || current.Value.NextNominal > time.CapturedUtc)
            return OperationResults.Ok(new BaseScheduleMaintenancePage
            {
                Authority = current.Value,
                Occurrences = [],
                Accounting = EmptyAccounting(),
                Disposition = BaseMutationRequestDisposition.Duplicate,
            });

        int maximum = Math.Min(256, target.Limits.Provider.MaximumCandidates);
        var nominal = new List<long>(maximum);
        long? cursor = current.Value.LastConsideredNominal;
        while (nominal.Count < maximum)
        {
            long? next = Next(definition, cursor);
            if (next is null || next > time.CapturedUtc) break;
            nominal.Add(next.Value);
            cursor = next;
        }
        if (nominal.Count == 0)
            return Failure<BaseScheduleMaintenancePage>(OperationStatus.Conflict, "base.activation.scheduleConflict", ErrorCategory.Conflict);

        long? following = Next(definition, nominal[^1]);
        var proposals = ImmutableArray.CreateBuilder<BaseScheduleOccurrenceProposal>(nominal.Count);
        for (int index = 0; index < nominal.Count; index++)
        {
            bool materialize = definition.MisfirePolicy switch
            {
                BaseScheduleMisfirePolicy.Skip => false,
                BaseScheduleMisfirePolicy.RunLatest => index == nominal.Count - 1 && (following is null || following > time.CapturedUtc),
                BaseScheduleMisfirePolicy.RunAll => true,
                _ => false,
            };
            int overlapOrdinal = BaseScheduleDefinitionBuilder.OverlapOrdinal(definition.Expression, nominal[index], timeZones,
                definition.GapPolicy, definition.TimeOverlapPolicy);
            proposals.Add(Proposal(session, definition, target, current.Value.ScheduleEpoch, nominal[index], overlapOrdinal, materialize));
        }

        OperationResult<BaseScheduleMaintenancePage> advanced = await provider.Value.AdvanceSchedulesAsync(new BaseScheduleMaintenanceRequest
        {
            ScheduleId = definition.Id,
            ScheduleVersion = definition.Version,
            ExpectedAuthorityChecksum = current.Value.Checksum.ToArray().ToImmutableArray(),
            Occurrences = proposals.MoveToImmutable(),
            ResultingLastConsideredNominal = nominal[^1],
            ResultingNextNominal = following,
            AcceptedTime = time,
            Identity = identity,
            Limits = target.Limits.Provider,
        }, cancellationToken).ConfigureAwait(false);
        if (!advanced.IsSuccess() || advanced.Value is null) return advanced;
        foreach (BaseScheduleCancellationAuthority cancellation in advanced.Value.Cancellations)
        {
            BaseScheduleCancellationBoundary? after = null;
            for (int page = 0; ; page++)
            {
                byte[] fingerprint = SHA256.HashData(Encoding.UTF8.GetBytes(
                    $"base.activation.schedule.cancelPrevious.page.v2\0{cancellation.MaintenanceId}\n{page}\n{after?.EffectiveDueAt}\n{after?.ActivationId}"));
                OperationResult<BaseScheduleCancellationMaintenancePage> result = await provider.Value.AdvanceScheduleCancellationAsync(
                    new BaseScheduleCancellationMaintenanceRequest
                    {
                        MaintenanceId = cancellation.MaintenanceId,
                        ReplacementActivationId = cancellation.ReplacementActivationId,
                        OverlapKey = cancellation.OverlapKey.ToArray().ToImmutableArray(),
                        HighWater = cancellation.HighWater,
                        After = after,
                        AcceptedTime = acceptedTime.Capture(session.ApplicationId),
                        Identity = BaseMutationRequestIdentity.Create(
                            $"schedule:{definition.Id}:{advanced.Value.Authority.ScheduleEpoch}", "cancel-previous", $"{cancellation.MaintenanceId}:{page}",
                            BaseMutationRequestFingerprint.Create(fingerprint)),
                        Limits = target.Limits.Provider,
                    }, cancellationToken).ConfigureAwait(false);
                if (!result.IsSuccess() || result.Value is null)
                    return CopyFailure<BaseScheduleMaintenancePage, BaseScheduleCancellationMaintenancePage>(result);
                if (result.Value.Completed) break;
                after = result.Value.Next ?? throw new InvalidOperationException("base.activation.providerContractInvalid");
            }
        }
        return advanced;
    }

    private BaseActivationDefinition Target(BaseScheduleDefinition definition) =>
        activations.Find(definition.Activation.Id, definition.Activation.Version) is { } target &&
        CryptographicOperations.FixedTimeEquals(target.Checksum.AsSpan(), definition.Activation.Checksum.AsSpan())
            ? target
            : throw new InvalidOperationException("base.activation.definitionUnavailable");

    private long? Next(BaseScheduleDefinition definition, long? after) => BaseScheduleDefinitionBuilder.NextNominal(
        definition.Expression, after, timeZones, definition.GapPolicy, definition.TimeOverlapPolicy);

    private static BaseScheduleOccurrenceProposal Proposal(
        BaseSession session, BaseScheduleDefinition schedule, BaseActivationDefinition target,
        long epoch, long nominal, int overlapOrdinal, bool materialize)
    {
        string occurrenceId = Hex($"base.activation.schedule.occurrence.id.v2\0{schedule.Id}\n{schedule.Version}\n{Convert.ToHexString(schedule.Checksum.AsSpan())}\n{epoch}\n{nominal}\n{overlapOrdinal}");
        long splay = schedule.MaximumSplayMilliseconds == 0 ? 0 :
            (long)(System.Buffers.Binary.BinaryPrimitives.ReadUInt64BigEndian(SHA256.HashData(Encoding.UTF8.GetBytes(occurrenceId))) %
            checked((ulong)schedule.MaximumSplayMilliseconds + 1));
        long effective = checked(nominal + splay);
        string activationId = Hex($"base.activation.schedule.activation.id.v2\0{occurrenceId}\n{target.Id}\n{target.Version}");
        BaseScheduleOccurrenceDisposition disposition = materialize
            ? new BaseOccurrenceMaterialized(activationId)
            : new BaseOccurrenceSkippedMisfire();
        var fact = new BaseScheduleOccurrenceFact
        {
            OccurrenceId = occurrenceId, ScheduleId = schedule.Id, ScheduleEpoch = epoch,
            NominalAt = nominal, EffectiveAt = effective, OverlapOrdinal = overlapOrdinal,
            Disposition = disposition, Checksum = ImmutableArray<byte>.Empty,
        };
        fact = fact with { Checksum = OccurrenceChecksum(fact).ToImmutableArray() };
        BaseActivationCreateIntent? activation = null;
        if (materialize)
        {
            byte[] overlapKey = schedule.OverlapKeyKind switch
            {
                BaseScheduleOverlapKeyKind.Schedule => SHA256.HashData(Encoding.UTF8.GetBytes($"schedule\0{schedule.Id}\n{epoch}")),
                BaseScheduleOverlapKeyKind.DefinitionScope => SHA256.HashData(Encoding.UTF8.GetBytes(
                    $"definition-scope\0{target.Id}\n{target.Version}\n{(int)session.ActivationScope.Kind}\n{session.ActivationScope.Value ?? string.Empty}")),
                BaseScheduleOverlapKeyKind.CanonicalConcurrencyKey => SHA256.HashData(schedule.ConcurrencyKey.AsSpan()),
                _ => throw new InvalidOperationException("base.activation.scheduleInvalid"),
            };
            byte[] fingerprint = SHA256.HashData(Encoding.UTF8.GetBytes(
                $"base.activation.schedule.request.v2\0{occurrenceId}\n{target.Id}\n{target.Version}\n{effective}"));
            activation = new BaseActivationCreateIntent
            {
                Ordinal = 0,
                Definition = schedule.Activation with { Checksum = schedule.Activation.Checksum.ToArray().ToImmutableArray() },
                CanonicalInput = schedule.CanonicalInput.ToArray().ToImmutableArray(),
                InputChecksum = schedule.InputChecksum.ToArray().ToImmutableArray(),
                Scope = session.ActivationScope,
                RequestedDueAt = nominal,
                EffectiveDueAt = effective,
                OccurrenceId = occurrenceId,
                Priority = schedule.Priority,
                OverlapKey = overlapKey.ToImmutableArray(),
                OverlapPolicy = schedule.ActivationOverlapPolicy,
                InitiallyEligible = schedule.ActivationOverlapPolicy != BaseScheduleOverlapPolicy.CancelPrevious,
                Identity = BaseMutationRequestIdentity.Create(
                    $"schedule:{schedule.Id}:{epoch}", "materialize", occurrenceId,
                    BaseMutationRequestFingerprint.Create(fingerprint)),
            };
        }
        return new BaseScheduleOccurrenceProposal { Fact = fact, Activation = activation };
    }

    private async ValueTask<OperationResult<IBaseActivationProvider>> AuthorizeAsync(
        BaseSession session, BaseScheduleDefinition definition, string grantId,
        BaseOperationKind operationKind, CancellationToken cancellationToken)
    {
        if (!BaseSystemCollectionGate.Allows(session.Principal))
            return Failure<IBaseActivationProvider>(OperationStatus.PolicyDenied, "base.activation.unauthorized", ErrorCategory.Authorization);
        OperationContext operation = session.Operation(operationKind, definition.Id);
        OperationResult<BasePolicyEvaluation> decision = await policy.EvaluateWriteAsync(new BasePolicyRequest
        {
            Principal = session.Principal,
            Operation = operation,
            Collection = new CollectionDefinition
            {
                Id = definition.Id, Name = definition.Id, Kind = BaseCollectionKinds.Custom,
                SchemaMode = SchemaMode.Strict, UnknownFields = UnknownFieldPolicy.Reject,
                System = true, SystemOwnerModuleId = definition.OwningModuleId,
            },
            ResourceKind = PolicyResourceKind.ScheduleDefinition,
        }, cancellationToken).ConfigureAwait(false);
        if (!BaseSystemCollectionGate.HasExactActivationGrant(decision, grantId, definition.OwningModuleId, session.Principal, operation))
            return Failure<IBaseActivationProvider>(OperationStatus.PolicyDenied, "base.activation.unauthorized", ErrorCategory.Authorization);
        IBaseActivationProvider[] providers = stores.GetRegistrations().Select(static item => item.Store)
            .OfType<IBaseActivationProvider>().Distinct().ToArray();
        return providers.Length == 1
            ? OperationResults.Ok(providers[0])
            : Failure<IBaseActivationProvider>(OperationStatus.Unsupported, "base.activation.capabilityUnavailable", ErrorCategory.Unsupported);
    }

    private static byte[] OccurrenceChecksum(BaseScheduleOccurrenceFact fact) => SHA256.HashData(Encoding.UTF8.GetBytes(
        $"base.activation.schedule.occurrence.v2\0{fact.OccurrenceId}\n{fact.ScheduleId}\n{fact.ScheduleEpoch}\n{fact.NominalAt}\n{fact.EffectiveAt}\n{fact.OverlapOrdinal}\n{Disposition(fact.Disposition)}"));
    private static string Disposition(BaseScheduleOccurrenceDisposition value) => value switch
    {
        BaseOccurrenceMaterialized item => $"materialized:{item.ActivationId}",
        BaseOccurrenceSkippedMisfire => "skipped-misfire",
        BaseOccurrenceSkippedOverlap item => $"skipped-overlap:{item.BlockingActivationId}",
        BaseOccurrenceCancelled item => $"cancelled:{item.CancellationReceiptId}",
        BaseOccurrenceSuppressedByReplacement item => $"replacement:{item.ReplacementGeneration}",
        BaseOccurrenceSuppressedByRestoreFloor item => $"restore:{Convert.ToHexString(item.FloorChecksum.AsSpan())}",
        _ => throw new InvalidOperationException("base.activation.occurrenceInvalid"),
    };
    private static string Hex(string value) => Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static BaseActivationAccounting EmptyAccounting() => new()
    { Candidates = 0, Comparisons = 0, IndexOperations = 0, ReadIntervals = 0, EvidenceBytes = 0, TransientBytes = 0 };
    private static OperationResult<T> Failure<T>(OperationStatus status, string code, ErrorCategory category) => new()
    { Status = status, Error = new BaseError { Code = code, Message = "The durable schedule operation could not be completed.", Category = category } };
    private static OperationResult<T> CopyFailure<T, TSource>(OperationResult<TSource> source) => new()
    { Status = source.Status, Error = source.Error, Warnings = source.Warnings, Diagnostics = source.Diagnostics };
}
