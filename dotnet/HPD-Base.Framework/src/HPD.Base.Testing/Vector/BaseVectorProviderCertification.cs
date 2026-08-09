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
    public const int ProtocolVersion = 3;

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

        OperationStatus externalStatus;
        switch (kind)
        {
            case BaseVectorCertificationFaultKind.RebuildPublishResponseLoss:
            case BaseVectorCertificationFaultKind.NonCooperativeOperation:
                externalStatus = (await host.Provider.RebuildAsync(BaseVectorCertificationRebuildRequest.Create(schema.CollectionId, schema.CosineIndexId), cancellationToken).ConfigureAwait(false)).Status;
                break;
            case BaseVectorCertificationFaultKind.DelaySearchVisibility:
                externalStatus = (await host.Provider.PublishVisibilityAsync(BaseVectorCertificationVisibilityRequest.Create(0), cancellationToken).ConfigureAwait(false)).Status;
                break;
            case BaseVectorCertificationFaultKind.CredentialFailure:
            case BaseVectorCertificationFaultKind.TerminalSchemaFailure:
            case BaseVectorCertificationFaultKind.MalformedCandidates:
            case BaseVectorCertificationFaultKind.DuplicateCandidates:
            case BaseVectorCertificationFaultKind.OversizedCandidates:
                externalStatus = (await host.Provider.InspectAsync(cancellationToken).ConfigureAwait(false)).Status;
                break;
            default:
                externalStatus = (await host.Provider.AdvanceAsync(BaseVectorCertificationAdvanceRequest.Create(0), cancellationToken).ConfigureAwait(false)).Status;
                break;
        }

        if (externalStatus is OperationStatus.Ok or OperationStatus.Created or OperationStatus.NoContent)
            return Failed(testCase.Id, "base.testing.vector.faultOutcomeInvalid", "The injected fault did not produce the required external failure outcome.");

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
        BaseSession session = host.Sessions.For(new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Admin, SubjectId = "certification-runner" });
        BaseCollectionSession<BaseVectorCertificationSchemaRecord> collection = session.Collection(BaseVectorCertificationSchemaRecord.Collection);
        BaseRecord<BaseVectorCertificationSchemaRecord> createdA = (await collection.CreateAsync(new RecordId("record-a"), Record("tenant-a", true, 10, null, "secret-a", [1, 0]), cancellationToken).ConfigureAwait(false)).RequireValue();
        _ = (await collection.CreateAsync(new RecordId("record-b"), Record("tenant-b", false, 20, "present", "secret-b", [0, 1]), cancellationToken).ConfigureAwait(false)).RequireValue();
        _ = (await collection.CreateAsync(new RecordId("record-c"), Record("tenant-a", true, 30, "present", "secret-c", [0.75f, 0.25f]), cancellationToken).ConfigureAwait(false)).RequireValue();
        BaseRecord<BaseVectorCertificationSchemaRecord> replacedA = (await collection.ReplaceAsync(new RecordId("record-a"), Record("tenant-a", true, 10, null, "secret-a-v2", [0.75f, 0.25f]), cancellationToken: cancellationToken).ConfigureAwait(false)).RequireValue();
        if (createdA.Revision is null || replacedA.Revision is null || createdA.Revision == replacedA.Revision)
            return Failed(caseId, "base.testing.vector.revisionInvalid", "Canonical mutation revisions were not authoritative.");

        if (caseId.Contains("atomicity", StringComparison.Ordinal))
        {
            BaseBatchBuilder batch = session.Atomic();
            batch.Create(BaseVectorCertificationSchemaRecord.Collection, new RecordId("rollback-probe"), Record("tenant-a", true, 1, null, null, [1, 0]));
            batch.Create(BaseVectorCertificationSchemaRecord.Collection, new RecordId("record-a"), Record("tenant-a", true, 1, null, null, [1, 0]));
            BaseBatchResult rolledBack = (await batch.CommitAsync(cancellationToken).ConfigureAwait(false)).RequireValue();
            if (rolledBack.Outcome != BaseRecordBatchOutcome.RolledBack || (await collection.GetAsync(new RecordId("rollback-probe"), cancellationToken).ConfigureAwait(false)) is BaseSuccess<BaseRecord<BaseVectorCertificationSchemaRecord>>)
                return Failed(caseId, "base.testing.vector.atomicityInvalid", "The canonical failed batch was not fully rolled back.");
        }

        if (!await ValidateRealQueriesAsync(host, replacedA.Revision.Value, cancellationToken).ConfigureAwait(false))
            return Failed(caseId, "base.testing.vector.queryEvidenceInvalid", "The BASE-owned canonical query oracle failed before rebuild.");

        OperationResult<BaseVectorCertificationAuthorityHead> head = await host.Authority.CaptureHeadAsync(cancellationToken).ConfigureAwait(false);
        OperationResult<BaseVectorCertificationAuthorityState> authority = await host.Authority.InspectAsync(cancellationToken).ConfigureAwait(false);
        OperationResult<BaseVectorCertificationProviderState> provider = await host.Provider.InspectAsync(cancellationToken).ConfigureAwait(false);
        OperationResult<BaseVectorCertificationObservationPage> observations = await host.Observations.ReadAsync(BaseVectorCertificationObservationRequest.Create(), cancellationToken).ConfigureAwait(false);
        if (!head.IsSuccess() || head.Value is null || head.Value.HighWaterPosition < head.Value.EarliestRetainedPosition || !authority.IsSuccess() || !provider.IsSuccess() || !observations.IsSuccess())
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
        BaseVectorResult<BaseVectorCertificationSchemaRecord> beforeRebuild = (await collection.Vector(BaseVectorCertificationSchemaRecord.VectorIndexes.Cosine).Nearest(BaseVector.Create([1, 0])).Take(3).ExecuteAsync(cancellationToken).ConfigureAwait(false)).RequireValue();
        BaseResult<BaseVectorRebuildResult> rebuild = await host.Application.Administration.RebuildVectorIndexAsync(new BaseVectorRebuildRequest
        {
            StoreId = host.StoreId,
            Principal = new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Admin, SubjectId = "certification-runner" },
            CollectionId = schema.CollectionId,
            VectorIndexId = schema.CosineIndexId,
            ExpectedGeneration = beforeRebuild.VectorIndexGeneration,
            ExpectedPurgeGeneration = 0,
            Confirmation = "rebuild",
        }, cancellationToken).ConfigureAwait(false);
        if (rebuild is not BaseSuccess<BaseVectorRebuildResult> rebuilt || rebuilt.Value.PublishedGeneration != beforeRebuild.VectorIndexGeneration + 1)
            return Failed(caseId, "base.testing.vector.rebuildInvalid", "The BASE administration rebuild did not publish the next generation.");
        if (!await ValidateRealQueriesAsync(host, replacedA.Revision.Value, cancellationToken).ConfigureAwait(false))
            return Failed(caseId, "base.testing.vector.queryEvidenceInvalid", "The BASE-owned canonical query oracle failed after rebuild.");
        OperationResult<BaseVectorCertificationFaultState> fault = await host.Provider.InspectFaultAsync(cancellationToken).ConfigureAwait(false);
        if (!fault.IsSuccess() || fault.Value is null || fault.Value.Kind != BaseVectorCertificationFaultKind.None)
            return Failed(caseId, "base.testing.vector.faultNotConsumed", "The certification fault state is invalid.");
        return Passed(caseId);

        static bool NotApplicable<T>(OperationResult<T> result) =>
            result.Status == OperationStatus.CapabilityUnavailable &&
            string.Equals(result.Error?.Code, "base.testing.vector.operationNotApplicable", StringComparison.Ordinal);

        static BaseVectorCertificationSchemaRecord Record(string tenant, bool active, long priority, string? optional, string? secret, float[] vector) => new()
        { Tenant = tenant, Active = active, Priority = priority, Optional = optional, Secret = secret, Embedding = BaseVector.Create(vector) };
    }
    private static async ValueTask<bool> ValidateRealQueriesAsync(IBaseVectorCertificationHost host, RevisionToken expectedARevision, CancellationToken cancellationToken)
    {
        BaseSession unrestricted = host.Sessions.For(new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Admin, SubjectId = "certification-runner" });
        BaseCollectionSession<BaseVectorCertificationSchemaRecord> collection = unrestricted.Collection(BaseVectorCertificationSchemaRecord.Collection);
        BaseVectorResult<BaseVectorCertificationSchemaRecord> cosine = (await collection.Vector(BaseVectorCertificationSchemaRecord.VectorIndexes.Cosine).Nearest(BaseVector.Create([1, 0])).Take(3).ExecuteAsync(cancellationToken).ConfigureAwait(false)).RequireValue();
        BaseVectorResult<BaseVectorCertificationSchemaRecord> euclidean = (await collection.Vector(BaseVectorCertificationSchemaRecord.VectorIndexes.Euclidean).Nearest(BaseVector.Create([1, 0])).Take(3).ExecuteAsync(cancellationToken).ConfigureAwait(false)).RequireValue();
        BaseVectorResult<BaseVectorCertificationSchemaRecord> dot = (await collection.Vector(BaseVectorCertificationSchemaRecord.VectorIndexes.Dot).Nearest(BaseVector.Create([1, 0])).Take(3).ExecuteAsync(cancellationToken).ConfigureAwait(false)).RequireValue();
        string[] expected = ["record-a", "record-c", "record-b"];
        if (!Ids(cosine).SequenceEqual(expected) || !Ids(euclidean).SequenceEqual(expected) || !Ids(dot).SequenceEqual(expected) ||
            !Near(cosine.Matches[0].Measure.Value, 0.9486832980505138) || !Near(euclidean.Matches[0].Measure.Value, Math.Sqrt(0.125)) || !Near(dot.Matches[0].Measure.Value, 0.75) ||
            cosine.Matches[0].Record.Revision != expectedARevision || cosine.Matches[0].Record.Value.Secret != "secret-a-v2")
            return false;
        BaseVectorResult<BaseVectorCertificationSchemaRecord> filters = (await collection.Vector(BaseVectorCertificationSchemaRecord.VectorIndexes.Cosine).Nearest(BaseVector.Create([1, 0]))
            .Where(BaseVectorCertificationSchemaRecord.Fields.Active, true).WhereAny(BaseVectorCertificationSchemaRecord.Fields.Priority, 10L, 30L)
            .Where(BaseVectorCertificationSchemaRecord.Fields.Optional, (string)null!).OrWhere(BaseVectorCertificationSchemaRecord.Fields.Optional, "present").Take(3).ExecuteAsync(cancellationToken).ConfigureAwait(false)).RequireValue();
        if (!Ids(filters).SequenceEqual(["record-a", "record-c", "record-b"])) return false;
        BaseSession restricted = host.Sessions.For(new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Authenticated, SubjectId = "certification-restricted" });
        BaseVectorResult<BaseVectorCertificationSchemaRecord> secured = (await restricted.Collection(BaseVectorCertificationSchemaRecord.Collection).Vector(BaseVectorCertificationSchemaRecord.VectorIndexes.Cosine)
            .Nearest(BaseVector.Create([0, 1])).Take(3).ExecuteAsync(cancellationToken).ConfigureAwait(false)).RequireValue();
        return Ids(secured).All(static id => id is "record-a" or "record-c") && secured.Matches.All(static match => match.Record.Redacted && match.Record.Value.Secret is null);

        static IEnumerable<string> Ids(BaseVectorResult<BaseVectorCertificationSchemaRecord> result) => result.Matches.Select(static match => match.Record.Id.Value);
        static bool Near(double actual, double expected) => Math.Abs(actual - expected) <= 1e-6;
    }
    private static BaseVectorCertificationCaseResult Passed(string id) => new(id, BaseVectorCertificationCaseOutcome.Passed);
    private static BaseVectorCertificationCaseResult Failed(string id, string code, string message) => new(id, BaseVectorCertificationCaseOutcome.Failed, code, message);
}
