namespace HPD.Base.Testing;

/// <summary>Identifies a certification plan.</summary>
public enum BaseVectorCertificationPlanKind
{
    /// <summary>Runs deterministic local conformance.</summary>
    Local,
    /// <summary>Runs live-service conformance.</summary>
    Live,
    /// <summary>Runs an N-1 upgrade conformance plan.</summary>
    Upgrade,
}

/// <summary>Identifies one certification case outcome.</summary>
public enum BaseVectorCertificationCaseOutcome
{
    /// <summary>The applicable case passed.</summary>
    Passed,
    /// <summary>The applicable case failed.</summary>
    Failed,
    /// <summary>The protocol excludes the case for this provider class.</summary>
    NotApplicable,
}

/// <summary>Identifies one previous provider version used by an upgrade plan.</summary>
public sealed class BaseVectorCertificationVersion
{
    private BaseVectorCertificationVersion(string packageVersion, string adapterVersion) { PackageVersion = packageVersion; AdapterVersion = adapterVersion; }
    /// <summary>Gets the package version.</summary>
    public string PackageVersion { get; }
    /// <summary>Gets the adapter version.</summary>
    public string AdapterVersion { get; }
    /// <summary>Creates a validated immutable previous-version identity.</summary>
    public static BaseVectorCertificationVersion Create(string packageVersion, string adapterVersion) => new(BaseVectorCertificationValidation.Id(packageVersion, nameof(packageVersion)), BaseVectorCertificationValidation.Id(adapterVersion, nameof(adapterVersion)));
    internal BaseVectorCertificationVersion Copy() => Create(PackageVersion, AdapterVersion);
}

/// <summary>Contains one immutable protocol-owned certification plan.</summary>
public sealed class BaseVectorCertificationPlan
{
    private BaseVectorCertificationPlan(BaseVectorCertificationPlanKind kind, BaseVectorCertificationVersion? previous)
    { Kind = kind; PreviousVersion = previous?.Copy(); }
    /// <summary>Gets the plan kind.</summary>
    public BaseVectorCertificationPlanKind Kind { get; }
    /// <summary>Gets the previous version for an upgrade plan.</summary>
    public BaseVectorCertificationVersion? PreviousVersion { get; }
    /// <summary>Creates the required deterministic local plan.</summary>
    public static BaseVectorCertificationPlan RequiredLocal() => new(BaseVectorCertificationPlanKind.Local, null);
    /// <summary>Creates the required live-service plan.</summary>
    public static BaseVectorCertificationPlan RequiredLive() => new(BaseVectorCertificationPlanKind.Live, null);
    /// <summary>Creates the required upgrade plan.</summary>
    public static BaseVectorCertificationPlan RequiredUpgrade(BaseVectorCertificationVersion previous) => new(BaseVectorCertificationPlanKind.Upgrade, previous ?? throw new ArgumentNullException(nameof(previous)));
}

/// <summary>Contains one immutable certification case result.</summary>
public sealed class BaseVectorCertificationCaseResult
{
    internal BaseVectorCertificationCaseResult(string caseId, BaseVectorCertificationCaseOutcome outcome, string? code = null, string? message = null) { CaseId = new string(caseId.AsSpan()); Outcome = outcome; Code = code is null ? null : new string(code.AsSpan()); Message = message is null ? null : new string(message.AsSpan()); }
    /// <summary>Gets the stable case identifier.</summary>
    public string CaseId { get; }
    /// <summary>Gets the protocol-computed outcome.</summary>
    public BaseVectorCertificationCaseOutcome Outcome { get; }
    /// <summary>Gets the bounded stable failure code.</summary>
    public string? Code { get; }
    /// <summary>Gets the fixed sanitized message.</summary>
    public string? Message { get; }
}

/// <summary>Contains one immutable vector-provider certification report.</summary>
public sealed class BaseVectorCertificationReport
{
    internal BaseVectorCertificationReport(BaseVectorCertificationIdentity identity, BaseVectorCertificationPlanKind planKind, DateTimeOffset startedAt, DateTimeOffset completedAt, int seed, IReadOnlyList<BaseVectorCertificationCaseResult> cases)
    { Identity = identity.Copy(); PlanKind = planKind; StartedAt = startedAt; CompletedAt = completedAt; Seed = seed; Cases = Array.AsReadOnly(cases.Select(static value => new BaseVectorCertificationCaseResult(value.CaseId, value.Outcome, value.Code, value.Message)).ToArray()); Succeeded = Cases.All(static item => item.Outcome != BaseVectorCertificationCaseOutcome.Failed); }
    /// <summary>Gets the certification protocol version.</summary>
    public int ProtocolVersion => BaseVectorProviderCertification.ProtocolVersion;
    /// <summary>Gets copied provider identity.</summary>
    public BaseVectorCertificationIdentity Identity { get; }
    /// <summary>Gets the plan kind.</summary>
    public BaseVectorCertificationPlanKind PlanKind { get; }
    /// <summary>Gets the fake-time report start.</summary>
    public DateTimeOffset StartedAt { get; }
    /// <summary>Gets the fake-time report completion.</summary>
    public DateTimeOffset CompletedAt { get; }
    /// <summary>Gets the deterministic plan seed.</summary>
    public int Seed { get; }
    /// <summary>Gets ordered case results.</summary>
    public IReadOnlyList<BaseVectorCertificationCaseResult> Cases { get; }
    /// <summary>Gets whether every applicable case passed.</summary>
    public bool Succeeded { get; }
}

/// <summary>Runs the versioned provider-neutral vector certification protocol.</summary>
public static class BaseVectorProviderCertification
{
    /// <summary>Gets the supported certification protocol version.</summary>
    public const int ProtocolVersion = 2;

    /// <summary>Runs the selected required plan against fresh isolated hosts.</summary>
    public static async ValueTask<BaseVectorCertificationReport> RunAsync(IBaseVectorProviderCertificationFixture fixture, BaseVectorCertificationPlan plan, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ArgumentNullException.ThrowIfNull(plan);
        if (fixture.Identity.ProtocolVersion != ProtocolVersion)
            return new BaseVectorCertificationReport(fixture.Identity, plan.Kind, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 0, [Failed("protocol", "base.testing.vector.protocolUnsupported", "The certification protocol is unsupported.")]);
        if (fixture.Identity.ProviderClass != fixture.ProviderClass)
            return new BaseVectorCertificationReport(fixture.Identity, plan.Kind, DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch, 0, [Failed("identity", "base.testing.vector.adapterFailed", "The certification adapter identity is invalid.")]);
        const int planSeed = 40_014;
        DateTimeOffset startedAt = DateTimeOffset.UnixEpoch;
        var results = new List<BaseVectorCertificationCaseResult>();
        foreach (CaseSpec testCase in RequiredCases(plan.Kind, fixture.ProviderClass))
        {
            cancellationToken.ThrowIfCancellationRequested();
            string caseId = testCase.Id;
            IBaseVectorCertificationHost? host = null;
            try
            {
                host = await fixture.CreateHostAsync(new BaseVectorCertificationHostRequest(caseId, StableSeed(caseId), BaseVectorCertificationSchema.Version1, DateTimeOffset.UnixEpoch, TimeSpan.FromMinutes(5), testCase.Fault), cancellationToken).ConfigureAwait(false);
                OperationResult<BaseApplicationReadiness> readiness = await host.Application.InitializeAsync(cancellationToken).ConfigureAwait(false);
                if (!readiness.IsSuccess()) results.Add(Failed(caseId, "base.testing.vector.adapterFailed", "The certification host did not become ready."));
                else results.Add(testCase.Fault.Kind == BaseVectorCertificationFaultKind.None
                    ? await ExecuteProtocolCaseAsync(host, fixture.ProviderClass, caseId, cancellationToken).ConfigureAwait(false)
                    : await ExecuteFaultCaseAsync(host, fixture.ProviderClass, testCase, cancellationToken).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch { results.Add(Failed(caseId, "base.testing.vector.adapterFailed", "The certification adapter failed.")); }
            finally { if (host is not null) try { await host.DisposeAsync().ConfigureAwait(false); } catch { if (results.Count != 0) results[^1] = Failed(caseId, "base.testing.vector.adapterFailed", "The certification host failed to close."); } }
        }
        return new BaseVectorCertificationReport(fixture.Identity, plan.Kind, startedAt, startedAt + TimeSpan.FromTicks(results.Count), planSeed, results);
    }

    private static IReadOnlyList<CaseSpec> RequiredCases(BaseVectorCertificationPlanKind kind, BaseVectorCertificationProviderClass providerClass)
    {
        string prefix = kind switch { BaseVectorCertificationPlanKind.Local => "vector", BaseVectorCertificationPlanKind.Live => "vector.live", _ => "vector.upgrade" };
        var cases = new List<CaseSpec>
        {
            None($"{prefix}.contract"), None($"{prefix}.atomicity"), None($"{prefix}.ranking"),
            None($"{prefix}.lifecycle"), None($"{prefix}.observability"),
        };
        BaseVectorCertificationFaultKind[] common =
        [
            BaseVectorCertificationFaultKind.RebuildPublishResponseLoss,
            BaseVectorCertificationFaultKind.NonCooperativeOperation,
            BaseVectorCertificationFaultKind.MalformedCandidates,
            BaseVectorCertificationFaultKind.DuplicateCandidates,
            BaseVectorCertificationFaultKind.OversizedCandidates,
            BaseVectorCertificationFaultKind.CredentialFailure,
            BaseVectorCertificationFaultKind.TerminalSchemaFailure,
        ];
        BaseVectorCertificationFaultKind[] derived =
        [
            BaseVectorCertificationFaultKind.FailBeforeSend,
            BaseVectorCertificationFaultKind.AcceptThenLoseResponse,
            BaseVectorCertificationFaultKind.PartialBatchSuccess,
            BaseVectorCertificationFaultKind.DuplicateReplay,
            BaseVectorCertificationFaultKind.DelaySearchVisibility,
            BaseVectorCertificationFaultKind.CheckpointCompareExchangeLoss,
            BaseVectorCertificationFaultKind.CheckpointAheadOfCarrier,
            BaseVectorCertificationFaultKind.CheckpointBehindCarrier,
            BaseVectorCertificationFaultKind.EmptyPageBelowCapturedHead,
            BaseVectorCertificationFaultKind.JournalGap,
            BaseVectorCertificationFaultKind.RetentionOvertake,
            BaseVectorCertificationFaultKind.LeaseExpiry,
            BaseVectorCertificationFaultKind.FencingLoss,
        ];
        foreach (BaseVectorCertificationFaultKind fault in common.Concat(providerClass == BaseVectorCertificationProviderClass.DerivedJournal ? derived : []))
            cases.Add(new CaseSpec($"{prefix}.fault.{FaultId(fault)}", Fault(fault)));
        return cases;

        static CaseSpec None(string id) => new(id, BaseVectorCertificationFaultPlan.Create(BaseVectorCertificationFaultKind.None));
        static BaseVectorCertificationFaultPlan Fault(BaseVectorCertificationFaultKind kind) => BaseVectorCertificationFaultPlan.Create(
            kind,
            delay: kind is BaseVectorCertificationFaultKind.DelaySearchVisibility or BaseVectorCertificationFaultKind.NonCooperativeOperation ? TimeSpan.FromMilliseconds(10) : default,
            partialSuccessCount: kind == BaseVectorCertificationFaultKind.PartialBatchSuccess ? 1 : 0);
        static string FaultId(BaseVectorCertificationFaultKind kind) => string.Concat(kind.ToString().Select((character, index) => char.IsUpper(character) && index != 0 ? "-" + char.ToLowerInvariant(character) : char.ToLowerInvariant(character).ToString()));
    }

    private static async ValueTask<BaseVectorCertificationCaseResult> ExecuteFaultCaseAsync(IBaseVectorCertificationHost host, BaseVectorCertificationProviderClass providerClass, CaseSpec testCase, CancellationToken cancellationToken)
    {
        BaseVectorCertificationSchema schema = BaseVectorCertificationSchema.Version1;
        BaseVectorCertificationFaultKind kind = testCase.Fault.Kind;
        if (providerClass == BaseVectorCertificationProviderClass.CoLocatedTransactional && IsDerivedOnly(kind))
            return Failed(testCase.Id, "base.testing.vector.adapterFailed", "The certification runner selected an inapplicable fault.");

        switch (kind)
        {
            case BaseVectorCertificationFaultKind.RebuildPublishResponseLoss:
            case BaseVectorCertificationFaultKind.NonCooperativeOperation:
                _ = await host.Provider.RebuildAsync(BaseVectorCertificationRebuildRequest.Create(schema.CollectionId, schema.CosineIndexId), cancellationToken).ConfigureAwait(false);
                break;
            case BaseVectorCertificationFaultKind.DelaySearchVisibility:
                _ = await host.Provider.PublishVisibilityAsync(BaseVectorCertificationVisibilityRequest.Create(0), cancellationToken).ConfigureAwait(false);
                break;
            case BaseVectorCertificationFaultKind.CredentialFailure:
            case BaseVectorCertificationFaultKind.TerminalSchemaFailure:
            case BaseVectorCertificationFaultKind.MalformedCandidates:
            case BaseVectorCertificationFaultKind.DuplicateCandidates:
            case BaseVectorCertificationFaultKind.OversizedCandidates:
                _ = await host.Provider.InspectAsync(cancellationToken).ConfigureAwait(false);
                break;
            default:
                _ = await host.Provider.AdvanceAsync(BaseVectorCertificationAdvanceRequest.Create(0), cancellationToken).ConfigureAwait(false);
                break;
        }

        OperationResult<BaseVectorCertificationFaultState> state = await host.Provider.InspectFaultAsync(cancellationToken).ConfigureAwait(false);
        if (!state.IsSuccess() || state.Value is null || state.Value.Kind != kind ||
            state.Value.TargetOccurrence != testCase.Fault.Occurrence || !state.Value.Consumed ||
            state.Value.ObservedOccurrences != state.Value.TargetOccurrence)
            return Failed(testCase.Id, "base.testing.vector.faultNotConsumed", "The certification fault was not consumed exactly as required.");
        OperationResult<BaseVectorCertificationObservationPage> observations = await host.Observations.ReadAsync(BaseVectorCertificationObservationRequest.Create(), cancellationToken).ConfigureAwait(false);
        if (!observations.IsSuccess() || observations.Value is null || !ValidObservationPage(observations.Value, TimeSpan.FromMinutes(5)))
            return Failed(testCase.Id, "base.testing.vector.adapterFailed", "The certification fault produced invalid observation evidence.");
        return Passed(testCase.Id);
    }

    private static bool IsDerivedOnly(BaseVectorCertificationFaultKind kind) => kind is >= BaseVectorCertificationFaultKind.FailBeforeSend and <= BaseVectorCertificationFaultKind.FencingLoss;

    private static bool ValidObservationPage(BaseVectorCertificationObservationPage page, TimeSpan deadline)
    {
        long previous = 0;
        foreach (BaseVectorCertificationObservation observation in page.Entries)
        {
            if (observation.Sequence <= previous || observation.Duration > deadline) return false;
            previous = observation.Sequence;
        }
        return page.Entries.Count <= 256 && (page.Entries.Count == 0 || page.NextSequence >= page.Entries[^1].Sequence);
    }

    private sealed record CaseSpec(string Id, BaseVectorCertificationFaultPlan Fault);
    private static int StableSeed(string value) { unchecked { int hash = 17; foreach (char item in value) hash = hash * 31 + item; return hash; } }
    private static async ValueTask<BaseVectorCertificationCaseResult> ExecuteProtocolCaseAsync(IBaseVectorCertificationHost host, BaseVectorCertificationProviderClass providerClass, string caseId, CancellationToken cancellationToken)
    {
        BaseVectorCertificationSchema schema = BaseVectorCertificationSchema.Version1;
        BaseVectorCertificationRecord first = Record("record-a", "tenant-a", [1, 0]);
        BaseVectorCertificationRecord second = Record("record-b", "tenant-b", [0, 1]);
        OperationResult<BaseVectorCertificationSeedResult> seed = await host.Authority.SeedAsync(BaseVectorCertificationSeedRequest.Create([first, second]), cancellationToken).ConfigureAwait(false);
        if (!seed.IsSuccess() || seed.Value is null || seed.Value.SeededRecords != 2)
            return Failed(caseId, "base.testing.vector.adapterFailed", "The certification authority rejected canonical seed state.");
        if (seed.Value.Head.HighWaterPosition < seed.Value.Head.EarliestRetainedPosition)
            return Failed(caseId, "base.testing.vector.adapterFailed", "The certification authority returned invalid seed evidence.");
        BaseVectorCertificationRecord replaced = Record("record-a", "tenant-a", [0.75f, 0.25f]);
        OperationResult<BaseVectorCertificationMutationResult> mutation = await host.Authority.CommitAsync(BaseVectorCertificationMutationRequest.Create([
            BaseVectorCertificationMutation.Create(BaseVectorCertificationMutationKind.Replace, "record-a", replaced),
        ]), cancellationToken).ConfigureAwait(false);
        if (!mutation.IsSuccess() || mutation.Value is null || mutation.Value.CommittedMutations != 1 || mutation.Value.FirstPosition <= 0 || mutation.Value.LastPosition < mutation.Value.FirstPosition)
            return Failed(caseId, "base.testing.vector.adapterFailed", "The certification authority returned invalid mutation evidence.");
        OperationResult<BaseVectorCertificationAuthorityHead> head = await host.Authority.CaptureHeadAsync(cancellationToken).ConfigureAwait(false);
        OperationResult<BaseVectorCertificationAuthorityState> authority = await host.Authority.InspectAsync(cancellationToken).ConfigureAwait(false);
        OperationResult<BaseVectorCertificationProviderState> provider = await host.Provider.InspectAsync(cancellationToken).ConfigureAwait(false);
        OperationResult<BaseVectorCertificationObservationPage> observations = await host.Observations.ReadAsync(BaseVectorCertificationObservationRequest.Create(), cancellationToken).ConfigureAwait(false);
        if (!head.IsSuccess() || head.Value is null || head.Value.HighWaterPosition < mutation.Value.LastPosition || head.Value.HighWaterPosition < head.Value.EarliestRetainedPosition || !authority.IsSuccess() || !provider.IsSuccess() || !observations.IsSuccess())
            return Failed(caseId, "base.testing.vector.adapterFailed", "The certification adapter returned invalid protocol state.");
        if (!string.Equals(authority.Value!.Head.StoreIdentityDigest, head.Value.StoreIdentityDigest, StringComparison.Ordinal) ||
            observations.Value is null || observations.Value.Entries.Count > 256)
            return Failed(caseId, "base.testing.vector.adapterFailed", "The certification adapter returned contradictory protocol state.");
        if (providerClass == BaseVectorCertificationProviderClass.DerivedJournal)
        {
            OperationResult<BaseVectorCertificationAdvanceResult> advance = await host.Provider.AdvanceAsync(BaseVectorCertificationAdvanceRequest.Create(head.Value.HighWaterPosition), cancellationToken).ConfigureAwait(false);
            if (!advance.IsSuccess()) return Failed(caseId, "base.testing.vector.adapterFailed", "The certification provider could not reach the captured authority head.");
            OperationResult<BaseVectorCertificationVisibilityResult> visibility = await host.Provider.PublishVisibilityAsync(BaseVectorCertificationVisibilityRequest.Create(head.Value.HighWaterPosition), cancellationToken).ConfigureAwait(false);
            if (!visibility.IsSuccess()) return Failed(caseId, "base.testing.vector.adapterFailed", "The certification provider could not publish searchable visibility.");
            if (caseId.Contains("lifecycle", StringComparison.Ordinal))
            {
                OperationResult<BaseVectorCertificationPruneResult> prune = await host.Authority.PruneHistoryAsync(BaseVectorCertificationPruneRequest.Create(head.Value.HighWaterPosition), cancellationToken).ConfigureAwait(false);
                if (!prune.IsSuccess()) return Failed(caseId, "base.testing.vector.adapterFailed", "The certification authority could not prune retained history.");
            }
        }
        else
        {
            OperationResult<BaseVectorCertificationAdvanceResult> advance = await host.Provider.AdvanceAsync(BaseVectorCertificationAdvanceRequest.Create(head.Value.HighWaterPosition), cancellationToken).ConfigureAwait(false);
            OperationResult<BaseVectorCertificationPruneResult> prune = await host.Authority.PruneHistoryAsync(BaseVectorCertificationPruneRequest.Create(head.Value.EarliestRetainedPosition), cancellationToken).ConfigureAwait(false);
            if (!NotApplicable(advance) || !NotApplicable(prune))
                return Failed(caseId, "base.testing.vector.adapterFailed", "The certification adapter violated provider-class applicability.");
        }
        if (!await ValidQueryEvidenceAsync(host, BaseVectorCertificationQueryScenario.CosineRanking, cancellationToken).ConfigureAwait(false))
            return Failed(caseId, "base.testing.vector.queryEvidenceInvalid", "The provider did not prove vector behavior before rebuild.");
        if (caseId.Contains("lifecycle", StringComparison.Ordinal))
        {
            OperationResult<BaseVectorCertificationTransitionResult> transition = await host.Authority.TransitionAsync(
                BaseVectorCertificationTransitionRequest.Create(BaseVectorCertificationTransitionKind.AdvanceIndexGeneration, schema.CollectionId, schema.CosineIndexId),
                cancellationToken).ConfigureAwait(false);
            if (!transition.IsSuccess() || transition.Value is null || transition.Value.CurrentGeneration != transition.Value.PreviousGeneration + 1)
                return Failed(caseId, "base.testing.vector.adapterFailed", "The certification authority rejected a generation transition.");
        }
        OperationResult<BaseVectorCertificationRebuildResult> rebuild = await host.Provider.RebuildAsync(BaseVectorCertificationRebuildRequest.Create(schema.CollectionId, schema.CosineIndexId), cancellationToken).ConfigureAwait(false);
        if (!rebuild.IsSuccess() || rebuild.Value is null || rebuild.Value.CurrentGeneration != rebuild.Value.PreviousGeneration + 1 ||
            !string.Equals(rebuild.Value.CollectionId, schema.CollectionId, StringComparison.Ordinal) ||
            !string.Equals(rebuild.Value.IndexId, schema.CosineIndexId, StringComparison.Ordinal))
            return Failed(caseId, "base.testing.vector.adapterFailed", "The certification provider rebuild failed.");
        OperationResult<BaseVectorCertificationFaultState> fault = await host.Provider.InspectFaultAsync(cancellationToken).ConfigureAwait(false);
        if (!fault.IsSuccess() || fault.Value is null || fault.Value.Kind != BaseVectorCertificationFaultKind.None)
            return Failed(caseId, "base.testing.vector.faultNotConsumed", "The certification fault state is invalid.");
        BaseVectorCertificationQueryScenario[] scenarios = caseId.Contains("ranking", StringComparison.Ordinal)
            ? Enum.GetValues<BaseVectorCertificationQueryScenario>()
            : [BaseVectorCertificationQueryScenario.CosineRanking];
        foreach (BaseVectorCertificationQueryScenario scenario in scenarios)
        {
            OperationResult<BaseVectorCertificationQueryResult> query = await host.Queries.ExecuteAsync(BaseVectorCertificationQueryRequest.Create(scenario), cancellationToken).ConfigureAwait(false);
            if (!query.IsSuccess() || query.Value is null || !ValidQueryEvidence(query.Value, scenario))
                return Failed(caseId, "base.testing.vector.queryEvidenceInvalid", "The provider did not prove the required vector query behavior.");
        }
        return Passed(caseId);

        static bool NotApplicable<T>(OperationResult<T> result) =>
            result.Status == OperationStatus.CapabilityUnavailable &&
            string.Equals(result.Error?.Code, "base.testing.vector.operationNotApplicable", StringComparison.Ordinal);

        BaseVectorCertificationRecord Record(string id, string tenant, float[] vector) => BaseVectorCertificationRecord.Create(id,
        [
            BaseVectorCertificationField.Create(schema.TenantFieldId, BaseVectorCertificationValue.String(tenant)),
            BaseVectorCertificationField.Create(schema.VectorFieldId, BaseVectorCertificationValue.Vector(BaseVector.Create(vector))),
        ]);
    }
    private static async ValueTask<bool> ValidQueryEvidenceAsync(IBaseVectorCertificationHost host, BaseVectorCertificationQueryScenario scenario, CancellationToken cancellationToken)
    {
        OperationResult<BaseVectorCertificationQueryResult> query = await host.Queries.ExecuteAsync(BaseVectorCertificationQueryRequest.Create(scenario), cancellationToken).ConfigureAwait(false);
        return query.IsSuccess() && query.Value is not null && ValidQueryEvidence(query.Value, scenario);
    }
    private static bool ValidQueryEvidence(BaseVectorCertificationQueryResult value, BaseVectorCertificationQueryScenario scenario)
    {
        string[] expected = scenario is BaseVectorCertificationQueryScenario.CosineRanking or BaseVectorCertificationQueryScenario.EuclideanRanking or BaseVectorCertificationQueryScenario.DotProductRanking
            ? ["record-a", "record-b"] : ["record-a"];
        return value.Scenario == scenario && value.HydratedRecords == expected.Length && value.AuthorizedCandidates >= expected.Length &&
            value.Matches.Select(static match => match.RecordId).SequenceEqual(expected, StringComparer.Ordinal) &&
            value.Matches.All(static match => !string.IsNullOrEmpty(match.Revision) && double.IsFinite(match.Measure) && match.IndexedPosition >= 1);
    }
    private static BaseVectorCertificationCaseResult Passed(string id) => new(id, BaseVectorCertificationCaseOutcome.Passed);
    private static BaseVectorCertificationCaseResult Failed(string id, string code, string message) => new(id, BaseVectorCertificationCaseOutcome.Failed, code, message);
}
