using Microsoft.Extensions.DependencyInjection;

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
    public const int ProtocolVersion = 4;

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
            catch (Exception) { results.Add(Failed(caseId, "base.testing.vector.adapterFailed", "The certification adapter failed.")); }
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
            BaseVectorCertificationFaultKind.NonCooperativeQuery,
            BaseVectorCertificationFaultKind.NonCooperativeInspection,
            BaseVectorCertificationFaultKind.NonCooperativeRebuild,
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
            BaseVectorCertificationFaultKind.NonCooperativeWrite,
        ];
        foreach (BaseVectorCertificationFaultKind fault in common.Concat(providerClass == BaseVectorCertificationProviderClass.DerivedJournal ? derived : []))
            cases.Add(new CaseSpec($"{prefix}.fault.{FaultId(fault)}", Fault(fault)));
        return cases;

        static CaseSpec None(string id) => new(id, BaseVectorCertificationFaultPlan.Create(BaseVectorCertificationFaultKind.None));
        static BaseVectorCertificationFaultPlan Fault(BaseVectorCertificationFaultKind kind) => BaseVectorCertificationFaultPlan.Create(
            kind,
            delay: kind is BaseVectorCertificationFaultKind.DelaySearchVisibility or BaseVectorCertificationFaultKind.NonCooperativeQuery or BaseVectorCertificationFaultKind.NonCooperativeWrite or BaseVectorCertificationFaultKind.NonCooperativeInspection or BaseVectorCertificationFaultKind.NonCooperativeRebuild ? TimeSpan.FromMilliseconds(10) : default,
            partialSuccessCount: kind == BaseVectorCertificationFaultKind.PartialBatchSuccess ? 1 : 0);
        static string FaultId(BaseVectorCertificationFaultKind kind) => string.Concat(kind.ToString().Select((character, index) => char.IsUpper(character) && index != 0 ? "-" + char.ToLowerInvariant(character) : char.ToLowerInvariant(character).ToString()));
    }

    private static async ValueTask<BaseVectorCertificationCaseResult> ExecuteFaultCaseAsync(IBaseVectorCertificationHost host, BaseVectorCertificationProviderClass providerClass, CaseSpec testCase, CancellationToken cancellationToken)
    {
        BaseVectorCertificationSchema schema = BaseVectorCertificationSchema.Version1;
        BaseVectorCertificationFaultKind kind = testCase.Fault.Kind;
        if (providerClass == BaseVectorCertificationProviderClass.CoLocatedTransactional && IsDerivedOnly(kind))
            return Failed(testCase.Id, "base.testing.vector.adapterFailed", "The certification runner selected an inapplicable fault.");

        if (kind == BaseVectorCertificationFaultKind.RebuildPublishResponseLoss)
        {
            BaseResult<BaseVectorRebuildResult> observed = await host.Application.Administration.RebuildVectorIndexAsync(new BaseVectorRebuildRequest
            {
                StoreId = host.StoreId,
                Principal = new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Admin, SubjectId = "certification-runner" },
                CollectionId = schema.CollectionId,
                VectorIndexId = schema.CosineIndexId,
                ExpectedGeneration = 1,
                ExpectedPurgeGeneration = 0,
                Confirmation = "rebuild",
            }, cancellationToken).ConfigureAwait(false);
            if (observed is not BaseFailure<BaseVectorRebuildResult> failure || !string.Equals(failure.Error.Code, BaseVectorErrorCodes.RebuildIndeterminate, StringComparison.Ordinal))
                return Failed(testCase.Id, "base.testing.vector.faultOutcomeInvalid", "The rebuild response-loss fault did not produce an indeterminate BASE outcome.");
            return await ValidateFaultEvidenceAsync(host, testCase, cancellationToken).ConfigureAwait(false);
        }

        if (kind == BaseVectorCertificationFaultKind.NonCooperativeRebuild)
        {
            BaseResult<BaseVectorRebuildResult> observed = await host.Application.Administration.RebuildVectorIndexAsync(new BaseVectorRebuildRequest { StoreId = host.StoreId, Principal = Admin(), CollectionId = schema.CollectionId, VectorIndexId = schema.CosineIndexId, ExpectedGeneration = 1, ExpectedPurgeGeneration = 0, Confirmation = "rebuild" }, cancellationToken).ConfigureAwait(false);
            if (observed is not BaseFailure<BaseVectorRebuildResult> failure || failure.Error.Code != BaseVectorErrorCodes.Timeout)
                return Failed(testCase.Id, "base.testing.vector.faultOutcomeInvalid", "The non-cooperative rebuild did not produce the exact BASE timeout.");
            if (!await ValidateRuntimeQuarantineAsync(host, "hpd.base.vector.provider", HealthStatus.Degraded, cancellationToken).ConfigureAwait(false))
                return Failed(testCase.Id, "base.testing.vector.faultStateInvalid", "The non-cooperative rebuild did not retain and release visible quarantine capacity.");
            return await ValidateFaultEvidenceAsync(host, testCase, cancellationToken).ConfigureAwait(false);
        }

        if (kind == BaseVectorCertificationFaultKind.NonCooperativeInspection)
        {
            HealthDescriptor? timedOut = await VectorHealthAsync(host, "hpd.base.vector.provider", cancellationToken).ConfigureAwait(false);
            if (timedOut?.Status != HealthStatus.Unhealthy || Metric(timedOut, "quarantinedOperations") < 1)
                return Failed(testCase.Id, "base.testing.vector.faultOutcomeInvalid", "The non-cooperative inspection did not time out with retained quarantine.");
            await Task.Delay(TimeSpan.FromMilliseconds(1250), cancellationToken).ConfigureAwait(false);
            HealthDescriptor? recovered = await VectorHealthAsync(host, "hpd.base.vector.provider", cancellationToken).ConfigureAwait(false);
            if (recovered?.Status != HealthStatus.Healthy || Metric(recovered, "quarantinedOperations") != 0)
                return Failed(testCase.Id, "base.testing.vector.faultStateInvalid", "The non-cooperative inspection did not recover after late completion.");
            return await ValidateFaultEvidenceAsync(host, testCase, cancellationToken).ConfigureAwait(false);
        }

        if (kind == BaseVectorCertificationFaultKind.NonCooperativeWrite)
        {
            Task<OperationResult<BaseVectorCertificationAdvanceResult>> work = host.Provider.AdvanceAsync(BaseVectorCertificationAdvanceRequest.Create(0), cancellationToken).AsTask();
            try { _ = await work.WaitAsync(TimeSpan.FromMilliseconds(100), cancellationToken).ConfigureAwait(false); return Failed(testCase.Id, "base.testing.vector.faultOutcomeInvalid", "The non-cooperative write completed inside its deadline."); }
            catch (TimeoutException) { }
            HealthDescriptor? quarantined = await VectorHealthAsync(host, "hpd.base.vector.certification.write", cancellationToken).ConfigureAwait(false);
            if (quarantined?.Status != HealthStatus.Degraded || Metric(quarantined, "quarantinedOperations") != 1)
                return Failed(testCase.Id, "base.testing.vector.faultStateInvalid", "The non-cooperative write did not retain visible quarantine capacity.");
            OperationResult<BaseVectorCertificationAdvanceResult> late = await work.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
            if (late.Error?.Code != FaultCode(kind))
                return Failed(testCase.Id, "base.testing.vector.faultOutcomeInvalid", "The late non-cooperative write produced the wrong stable outcome.");
            HealthDescriptor? recovered = await VectorHealthAsync(host, "hpd.base.vector.certification.write", cancellationToken).ConfigureAwait(false);
            if (recovered?.Status != HealthStatus.Healthy || Metric(recovered, "quarantinedOperations") != 0)
                return Failed(testCase.Id, "base.testing.vector.faultStateInvalid", "The non-cooperative write did not recover after late completion.");
            return await ValidateFaultEvidenceAsync(host, testCase, cancellationToken).ConfigureAwait(false);
        }

        if (kind is BaseVectorCertificationFaultKind.MalformedCandidates or BaseVectorCertificationFaultKind.DuplicateCandidates or BaseVectorCertificationFaultKind.OversizedCandidates or BaseVectorCertificationFaultKind.CredentialFailure or BaseVectorCertificationFaultKind.TerminalSchemaFailure or BaseVectorCertificationFaultKind.NonCooperativeQuery)
        {
            BaseSession session = host.Sessions.For(new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Admin, SubjectId = "certification-runner" });
            BaseCollectionSession<BaseVectorCertificationSchemaRecord> collection = session.Collection(BaseVectorCertificationSchemaRecord.Collection);
            _ = (await collection.CreateAsync(RecordId.Create("fault-a"), new BaseVectorCertificationSchemaRecord { Tenant = "tenant-a", Active = true, Priority = 1, Optional = null, Secret = null, Embedding = BaseVector.Create([1, 0]) }, cancellationToken).ConfigureAwait(false)).RequireValue();
            _ = (await collection.CreateAsync(RecordId.Create("fault-b"), new BaseVectorCertificationSchemaRecord { Tenant = "tenant-a", Active = true, Priority = 2, Optional = "present", Secret = null, Embedding = BaseVector.Create([0, 1]) }, cancellationToken).ConfigureAwait(false)).RequireValue();
            if (providerClass == BaseVectorCertificationProviderClass.DerivedJournal)
            {
                OperationResult<BaseVectorCertificationAuthorityHead> captured = await host.Authority.CaptureHeadAsync(cancellationToken).ConfigureAwait(false);
                if (!captured.IsSuccess() || captured.Value is null || captured.Value.HighWaterPosition == 0 || !(await host.Provider.AdvanceAsync(BaseVectorCertificationAdvanceRequest.Create(captured.Value.HighWaterPosition), cancellationToken).ConfigureAwait(false)).IsSuccess() ||
                    !(await host.Provider.PublishVisibilityAsync(BaseVectorCertificationVisibilityRequest.Create(captured.Value.HighWaterPosition), cancellationToken).ConfigureAwait(false)).IsSuccess())
                    return Failed(testCase.Id, "base.testing.vector.faultOutcomeInvalid", "The fault precondition did not establish derived visibility.");
            }
            BaseResult<BaseVectorResult<BaseVectorCertificationSchemaRecord>> observed = await collection.Vector(BaseVectorCertificationSchemaRecord.VectorIndexes.Cosine).Nearest(BaseVector.Create([1, 0])).Take(2).ExecuteAsync(cancellationToken).ConfigureAwait(false);
            string expectedCode = kind switch
            {
                BaseVectorCertificationFaultKind.MalformedCandidates or BaseVectorCertificationFaultKind.DuplicateCandidates or BaseVectorCertificationFaultKind.OversizedCandidates => BaseVectorErrorCodes.ProviderResultInvalid,
                BaseVectorCertificationFaultKind.NonCooperativeQuery => BaseVectorErrorCodes.Timeout,
                BaseVectorCertificationFaultKind.CredentialFailure => BaseVectorErrorCodes.ProviderUnavailable,
                _ => BaseVectorErrorCodes.RebuildRequired,
            };
            if (observed is not BaseFailure<BaseVectorResult<BaseVectorCertificationSchemaRecord>> failure || !string.Equals(failure.Error.Code, expectedCode, StringComparison.Ordinal))
                return Failed(testCase.Id, "base.testing.vector.faultOutcomeInvalid", "The injected fault did not produce its exact BASE query failure.");
            if (kind == BaseVectorCertificationFaultKind.NonCooperativeQuery)
            {
                IHPDBaseRuntime runtime = session.Services.GetRequiredService<IHPDBaseRuntime>();
                var healthOperation = new OperationContext { Operation = BaseOperationKind.AdminInspect, CollectionId = schema.CollectionId, Mode = OperationMode.System };
                OperationResult<HealthDescriptor[]> quarantined = await runtime.Health.GetHealthAsync(session.Principal, healthOperation, VisibilityLevel.Admin, cancellationToken).ConfigureAwait(false);
                HealthDescriptor? vectorHealth = quarantined.Value?.SingleOrDefault(static item => item.Id == "hpd.base.vector.provider");
                if (vectorHealth?.Status != HealthStatus.Degraded || Metric(vectorHealth, "quarantinedOperations") < 1)
                    return Failed(testCase.Id, "base.testing.vector.faultStateInvalid", "The non-cooperative query did not retain visible quarantine capacity.");
                await Task.Delay(TimeSpan.FromMilliseconds(350), cancellationToken).ConfigureAwait(false);
                OperationResult<HealthDescriptor[]> recovered = await runtime.Health.GetHealthAsync(session.Principal, healthOperation, VisibilityLevel.Admin, cancellationToken).ConfigureAwait(false);
                vectorHealth = recovered.Value?.SingleOrDefault(static item => item.Id == "hpd.base.vector.provider");
                if (vectorHealth?.Status != HealthStatus.Healthy || Metric(vectorHealth, "quarantinedOperations") != 0)
                    return Failed(testCase.Id, "base.testing.vector.faultStateInvalid", "The non-cooperative query did not release quarantine after late completion.");
            }
            return await ValidateFaultEvidenceAsync(host, testCase, cancellationToken).ConfigureAwait(false);
        }

        OperationStatus externalStatus;
        string? externalCode;
        switch (kind)
        {
            case BaseVectorCertificationFaultKind.DelaySearchVisibility:
                OperationResult<BaseVectorCertificationVisibilityResult> visible = await host.Provider.PublishVisibilityAsync(BaseVectorCertificationVisibilityRequest.Create(0), cancellationToken).ConfigureAwait(false);
                (externalStatus, externalCode) = (visible.Status, visible.Error?.Code);
                break;
            case BaseVectorCertificationFaultKind.CredentialFailure:
            case BaseVectorCertificationFaultKind.TerminalSchemaFailure:
            case BaseVectorCertificationFaultKind.MalformedCandidates:
            case BaseVectorCertificationFaultKind.DuplicateCandidates:
            case BaseVectorCertificationFaultKind.OversizedCandidates:
                OperationResult<BaseVectorCertificationProviderState> inspected = await host.Provider.InspectAsync(cancellationToken).ConfigureAwait(false);
                (externalStatus, externalCode) = (inspected.Status, inspected.Error?.Code);
                break;
            default:
                OperationResult<BaseVectorCertificationAdvanceResult> advanced = await host.Provider.AdvanceAsync(BaseVectorCertificationAdvanceRequest.Create(0), cancellationToken).ConfigureAwait(false);
                (externalStatus, externalCode) = (advanced.Status, advanced.Error?.Code);
                break;
        }

        if (externalStatus is OperationStatus.Ok or OperationStatus.Created or OperationStatus.NoContent)
            return Failed(testCase.Id, "base.testing.vector.faultOutcomeInvalid", "The injected fault did not produce the required external failure outcome.");
        if (!string.Equals(externalCode, FaultCode(kind), StringComparison.Ordinal))
            return Failed(testCase.Id, "base.testing.vector.faultOutcomeInvalid", "The injected fault did not produce its exact control failure code.");

        return await ValidateFaultEvidenceAsync(host, testCase, cancellationToken).ConfigureAwait(false);
    }

    private static async ValueTask<BaseVectorCertificationCaseResult> ValidateFaultEvidenceAsync(IBaseVectorCertificationHost host, CaseSpec testCase, CancellationToken cancellationToken)
    {
        OperationResult<BaseVectorCertificationFaultState> state = await host.Provider.InspectFaultAsync(cancellationToken).ConfigureAwait(false);
        BaseVectorCertificationFaultKind kind = testCase.Fault.Kind;
        if (!state.IsSuccess() || state.Value is null || state.Value.Kind != kind ||
            state.Value.TargetOccurrence != testCase.Fault.Occurrence || !state.Value.Consumed ||
            state.Value.ObservedOccurrences != state.Value.TargetOccurrence)
            return Failed(testCase.Id, "base.testing.vector.faultNotConsumed", "The certification fault was not consumed exactly as required.");
        OperationResult<BaseVectorCertificationObservationPage> observations = await host.Observations.ReadAsync(BaseVectorCertificationObservationRequest.Create(), cancellationToken).ConfigureAwait(false);
        if (!observations.IsSuccess() || observations.Value is null || !ValidObservationPage(observations.Value, TimeSpan.FromMinutes(5)))
            return Failed(testCase.Id, "base.testing.vector.adapterFailed", "The certification fault produced invalid observation evidence.");
        return Passed(testCase.Id);
    }

    private static bool IsDerivedOnly(BaseVectorCertificationFaultKind kind) => kind is >= BaseVectorCertificationFaultKind.FailBeforeSend and <= BaseVectorCertificationFaultKind.FencingLoss or BaseVectorCertificationFaultKind.NonCooperativeWrite;

    private static string FaultCode(BaseVectorCertificationFaultKind kind) => "base.testing.vector.fault." + string.Concat(kind.ToString().Select((character, index) => char.IsUpper(character) && index != 0 ? "-" + char.ToLowerInvariant(character) : char.ToLowerInvariant(character).ToString()));

    private static double Metric(HealthDescriptor health, string name) => health.Metrics?.SingleOrDefault(metric => metric.Name == name)?.NumberValue ?? -1;

    private static PrincipalContext Admin() => new() { AuthenticationState = PrincipalAuthenticationState.Admin, SubjectId = "certification-runner" };

    private static async ValueTask<HealthDescriptor?> VectorHealthAsync(IBaseVectorCertificationHost host, string id, CancellationToken cancellationToken)
    {
        IHPDBaseRuntime runtime = host.Sessions.For(Admin()).Services.GetRequiredService<IHPDBaseRuntime>();
        OperationResult<HealthDescriptor[]> health = await runtime.Health.GetHealthAsync(Admin(), new OperationContext { Operation = BaseOperationKind.AdminInspect, CollectionId = BaseVectorCertificationSchema.Version1.CollectionId, Mode = OperationMode.System }, VisibilityLevel.Admin, cancellationToken).ConfigureAwait(false);
        return health.Value?.SingleOrDefault(item => item.Id == id);
    }

    private static async ValueTask<bool> ValidateRuntimeQuarantineAsync(IBaseVectorCertificationHost host, string id, HealthStatus during, CancellationToken cancellationToken)
    {
        HealthDescriptor? quarantined = await VectorHealthAsync(host, id, cancellationToken).ConfigureAwait(false);
        if (quarantined?.Status != during || Metric(quarantined, "quarantinedOperations") < 1) return false;
        await Task.Delay(id == "hpd.base.vector.provider" ? TimeSpan.FromMilliseconds(1250) : TimeSpan.FromMilliseconds(350), cancellationToken).ConfigureAwait(false);
        HealthDescriptor? recovered = await VectorHealthAsync(host, id, cancellationToken).ConfigureAwait(false);
        return recovered?.Status == HealthStatus.Healthy && Metric(recovered, "quarantinedOperations") == 0;
    }

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
        BaseRecord<BaseVectorCertificationSchemaRecord> createdA = (await collection.CreateAsync(RecordId.Create("record-a"), Record("tenant-a", true, 10, null, "secret-a", [1, 0]), cancellationToken).ConfigureAwait(false)).RequireValue();
        _ = (await collection.CreateAsync(RecordId.Create("record-b"), Record("tenant-b", false, 20, "present", "secret-b", [0, 1]), cancellationToken).ConfigureAwait(false)).RequireValue();
        _ = (await collection.CreateAsync(RecordId.Create("record-c"), Record("tenant-a", true, 30, "present", "secret-c", [0.75f, 0.25f]), cancellationToken).ConfigureAwait(false)).RequireValue();
        BaseRecord<BaseVectorCertificationSchemaRecord> replacedA = (await collection.ReplaceAsync(RecordId.Create("record-a"), Record("tenant-a", true, 10, null, "secret-a-v2", [0.75f, 0.25f]), cancellationToken: cancellationToken).ConfigureAwait(false)).RequireValue();
        if (createdA.Revision is null || replacedA.Revision is null || createdA.Revision == replacedA.Revision)
            return Failed(caseId, "base.testing.vector.revisionInvalid", "Canonical mutation revisions were not authoritative.");

        if (caseId.Contains("atomicity", StringComparison.Ordinal))
        {
            BaseBatchBuilder batch = session.Atomic();
            batch.Create(BaseVectorCertificationSchemaRecord.Collection, RecordId.Create("rollback-probe"), Record("tenant-a", true, 1, null, null, [1, 0]));
            batch.Create(BaseVectorCertificationSchemaRecord.Collection, RecordId.Create("record-a"), Record("tenant-a", true, 1, null, null, [1, 0]));
            BaseBatchResult rolledBack = (await batch.CommitAsync(cancellationToken).ConfigureAwait(false)).RequireValue();
            if (rolledBack.Outcome != BaseRecordBatchOutcome.RolledBack || (await collection.GetAsync(RecordId.Create("rollback-probe"), cancellationToken).ConfigureAwait(false)) is BaseSuccess<BaseRecord<BaseVectorCertificationSchemaRecord>>)
                return Failed(caseId, "base.testing.vector.atomicityInvalid", "The canonical failed batch was not fully rolled back.");
        }

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
            if (head.Value.HighWaterPosition == 0)
                return Failed(caseId, "base.testing.vector.derivedHeadInvalid", "Canonical mutations did not advance the derived authority head.");
            OperationResult<BaseVectorCertificationAdvanceResult> advance = await host.Provider.AdvanceAsync(BaseVectorCertificationAdvanceRequest.Create(head.Value.HighWaterPosition), cancellationToken).ConfigureAwait(false);
            if (!advance.IsSuccess() || advance.Value?.CurrentCheckpoint != head.Value.HighWaterPosition || advance.Value.SearchVisibleThrough != 0)
                return Failed(caseId, "base.testing.vector.adapterFailed", "The certification provider could not apply the captured authority head without publishing it.");
            OperationResult<BaseVectorCertificationProviderState> appliedState = await host.Provider.InspectAsync(cancellationToken).ConfigureAwait(false);
            if (!DistinctDerivedState(appliedState, head.Value.HighWaterPosition, 0))
                return Failed(caseId, "base.testing.vector.visibilityInvalid", "Provider inspection did not separate applied and search-visible positions.");
            BaseVectorResult<BaseVectorCertificationSchemaRecord> unpublished = (await collection.Vector(BaseVectorCertificationSchemaRecord.VectorIndexes.Cosine).Nearest(BaseVector.Create([1, 0])).Take(1).ExecuteAsync(cancellationToken).ConfigureAwait(false)).RequireValue();
            if (unpublished.Matches.Length != 0)
                return Failed(caseId, "base.testing.vector.visibilityInvalid", "Applied carriers became searchable before visibility publication.");
            OperationResult<BaseVectorCertificationVisibilityResult> visibility = await host.Provider.PublishVisibilityAsync(BaseVectorCertificationVisibilityRequest.Create(head.Value.HighWaterPosition), cancellationToken).ConfigureAwait(false);
            if (!visibility.IsSuccess() || visibility.Value?.CurrentSearchVisibleThrough != head.Value.HighWaterPosition)
                return Failed(caseId, "base.testing.vector.adapterFailed", "The certification provider could not publish searchable visibility.");
            OperationResult<BaseVectorCertificationProviderState> visibleState = await host.Provider.InspectAsync(cancellationToken).ConfigureAwait(false);
            if (!DistinctDerivedState(visibleState, head.Value.HighWaterPosition, head.Value.HighWaterPosition))
                return Failed(caseId, "base.testing.vector.visibilityInvalid", "Provider inspection did not report published searchable visibility.");
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
        if (!await ValidateRealQueriesAsync(host, replacedA.Revision.Value, cancellationToken).ConfigureAwait(false))
            return Failed(caseId, "base.testing.vector.queryEvidenceInvalid", "The BASE-owned canonical query oracle failed before rebuild.");
        RevisionToken expectedQueryRevision = replacedA.Revision.Value;
        if (providerClass == BaseVectorCertificationProviderClass.DerivedJournal)
        {
            RevisionToken? derivedRevision = await ValidateDerivedConsistencyAsync(host, collection, cancellationToken).ConfigureAwait(false);
            if (derivedRevision is null)
                return Failed(caseId, "base.testing.vector.derivedConsistencyInvalid", "The derived provider did not honor finite Current catch-up, AtLeast, bounded staleness, or exact historical hydration.");
            expectedQueryRevision = derivedRevision.Value;
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
        if (!await ValidateRealQueriesAsync(host, expectedQueryRevision, cancellationToken).ConfigureAwait(false))
            return Failed(caseId, "base.testing.vector.queryEvidenceInvalid", "The BASE-owned canonical query oracle failed after rebuild.");
        OperationResult<BaseVectorCertificationFaultState> fault = await host.Provider.InspectFaultAsync(cancellationToken).ConfigureAwait(false);
        if (!fault.IsSuccess() || fault.Value is null || fault.Value.Kind != BaseVectorCertificationFaultKind.None)
            return Failed(caseId, "base.testing.vector.faultNotConsumed", "The certification fault state is invalid.");
        return Passed(caseId);

        static bool NotApplicable<T>(OperationResult<T> result) =>
            result.Status == OperationStatus.CapabilityUnavailable &&
            string.Equals(result.Error?.Code, "base.testing.vector.operationNotApplicable", StringComparison.Ordinal);

        static bool DistinctDerivedState(OperationResult<BaseVectorCertificationProviderState> result, long applied, long visible) =>
            result.IsSuccess() && result.Value is { Indexes.Count: 3 } && result.Value.Indexes.All(index => index.DurableCheckpoint == applied && index.SearchVisibleThrough == visible);

        static BaseVectorCertificationSchemaRecord Record(string tenant, bool active, long priority, string? optional, string? secret, float[] vector) => new()
        { Tenant = tenant, Active = active, Priority = priority, Optional = optional, Secret = secret, Embedding = BaseVector.Create(vector) };
    }

    private static async ValueTask<RevisionToken?> ValidateDerivedConsistencyAsync(IBaseVectorCertificationHost host, BaseCollectionSession<BaseVectorCertificationSchemaRecord> collection, CancellationToken cancellationToken)
    {
        BaseVectorQuery<BaseVectorCertificationSchemaRecord> query = collection.Vector(BaseVectorCertificationSchemaRecord.VectorIndexes.Cosine).Nearest(BaseVector.Create([1, 0]));
        BaseResult<BaseVectorResult<BaseVectorCertificationSchemaRecord>> current = await query.WithConsistency(new BaseVectorConsistencyRequirement.Current()).Take(3).ExecuteAsync(cancellationToken).ConfigureAwait(false);
        if (current is not BaseSuccess<BaseVectorResult<BaseVectorCertificationSchemaRecord>> currentSuccess) return null;
        RevisionToken historicalRevision = currentSuccess.Value.Matches.Single(static match => match.Record.Id.Value == "record-a").Record.Revision!.Value;
        RevisionToken historicalBRevision = currentSuccess.Value.Matches.Single(static match => match.Record.Id.Value == "record-b").Record.Revision!.Value;
        BaseRecord<BaseVectorCertificationSchemaRecord> newerA = (await collection.ReplaceAsync(RecordId.Create("record-a"), new BaseVectorCertificationSchemaRecord { Tenant = "tenant-a", Active = true, Priority = 10, Optional = null, Secret = "secret-a-v2", Embedding = BaseVector.Create([0.75f, 0.25f]) }, cancellationToken: cancellationToken).ConfigureAwait(false)).RequireValue();
        BaseResult<BaseVectorResult<BaseVectorCertificationSchemaRecord>> atLeast = await query.WithConsistency(new BaseVectorConsistencyRequirement.AtLeast(currentSuccess.Value.ConsistencyToken)).Take(3).ExecuteAsync(cancellationToken).ConfigureAwait(false);
        BaseResult<BaseVectorResult<BaseVectorCertificationSchemaRecord>> bounded = await query.WithConsistency(new BaseVectorConsistencyRequirement.BoundedStaleness(TimeSpan.FromMinutes(5))).Take(3).ExecuteAsync(cancellationToken).ConfigureAwait(false);
        if (atLeast is not BaseSuccess<BaseVectorResult<BaseVectorCertificationSchemaRecord>> atLeastSuccess || bounded is not BaseSuccess<BaseVectorResult<BaseVectorCertificationSchemaRecord>> boundedSuccess ||
            !atLeastSuccess.Value.Matches.Select(static match => match.Record.Id.Value).SequenceEqual(currentSuccess.Value.Matches.Select(static match => match.Record.Id.Value)) ||
            atLeastSuccess.Value.Matches.Single(static match => match.Record.Id.Value == "record-a").Record.Revision != historicalRevision ||
            atLeastSuccess.Value.Matches.Single(static match => match.Record.Id.Value == "record-a").Record.Value.Secret != "secret-a-v2" ||
            !boundedSuccess.Value.Matches.Select(static match => match.Record.Id.Value).SequenceEqual(currentSuccess.Value.Matches.Select(static match => match.Record.Id.Value))) return null;

        OperationResult<BaseVectorCertificationAuthorityHead> finite = await host.Authority.CaptureHeadAsync(cancellationToken).ConfigureAwait(false);
        if (!finite.IsSuccess() || finite.Value is null) return null;
        Task<BaseResult<BaseVectorResult<BaseVectorCertificationSchemaRecord>>> waitingCurrent = query.WithConsistency(new BaseVectorConsistencyRequirement.Current()).Take(3).ExecuteAsync(cancellationToken).AsTask();
        await Task.Delay(TimeSpan.FromMilliseconds(40), cancellationToken).ConfigureAwait(false);
        if (waitingCurrent.IsCompleted) return null;
        _ = (await collection.ReplaceAsync(RecordId.Create("record-b"), new BaseVectorCertificationSchemaRecord { Tenant = "tenant-b", Active = false, Priority = 20, Optional = "present", Secret = "secret-b", Embedding = BaseVector.Create([0, 1]) }, cancellationToken: cancellationToken).ConfigureAwait(false)).RequireValue();
        OperationResult<BaseVectorCertificationAuthorityHead> moving = await host.Authority.CaptureHeadAsync(cancellationToken).ConfigureAwait(false);
        if (!moving.IsSuccess() || moving.Value is null || moving.Value.HighWaterPosition <= finite.Value.HighWaterPosition) return null;
        if (!(await host.Provider.AdvanceAsync(BaseVectorCertificationAdvanceRequest.Create(finite.Value.HighWaterPosition), cancellationToken).ConfigureAwait(false)).IsSuccess() ||
            !(await host.Provider.PublishVisibilityAsync(BaseVectorCertificationVisibilityRequest.Create(finite.Value.HighWaterPosition), cancellationToken).ConfigureAwait(false)).IsSuccess()) return null;
        BaseResult<BaseVectorResult<BaseVectorCertificationSchemaRecord>> caughtUp = await waitingCurrent.WaitAsync(TimeSpan.FromSeconds(1), cancellationToken).ConfigureAwait(false);
        if (caughtUp is not BaseSuccess<BaseVectorResult<BaseVectorCertificationSchemaRecord>> caughtUpSuccess ||
            caughtUpSuccess.Value.Matches.Single(static match => match.Record.Id.Value == "record-a").Record.Revision != newerA.Revision ||
            caughtUpSuccess.Value.Matches.Single(static match => match.Record.Id.Value == "record-b").Record.Revision != historicalBRevision) return null;
        OperationResult<BaseVectorCertificationProviderState> state = await host.Provider.InspectAsync(cancellationToken).ConfigureAwait(false);
        if (!state.IsSuccess() || state.Value is null || state.Value.Indexes.Any(index => index.DurableCheckpoint != finite.Value.HighWaterPosition || index.SearchVisibleThrough != finite.Value.HighWaterPosition) ||
            state.Value.Indexes.Any(index => index.DurableCheckpoint >= moving.Value.HighWaterPosition)) return null;
        if (!(await host.Provider.AdvanceAsync(BaseVectorCertificationAdvanceRequest.Create(moving.Value.HighWaterPosition), cancellationToken).ConfigureAwait(false)).IsSuccess() ||
            !(await host.Provider.PublishVisibilityAsync(BaseVectorCertificationVisibilityRequest.Create(moving.Value.HighWaterPosition), cancellationToken).ConfigureAwait(false)).IsSuccess()) return null;
        return newerA.Revision;
    }
    private static async ValueTask<bool> ValidateRealQueriesAsync(IBaseVectorCertificationHost host, RevisionToken expectedARevision, CancellationToken cancellationToken)
    {
        BaseSession unrestricted = host.Sessions.For(new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Admin, SubjectId = "certification-runner" });
        BaseCollectionSession<BaseVectorCertificationSchemaRecord> collection = unrestricted.Collection(BaseVectorCertificationSchemaRecord.Collection);
        BaseVectorResult<BaseVectorCertificationSchemaRecord> cosine = (await collection.Vector(BaseVectorCertificationSchemaRecord.VectorIndexes.Cosine).Nearest(BaseVector.Create([1, 0])).Take(3).ExecuteAsync(cancellationToken).ConfigureAwait(false)).RequireValue();
        BaseVectorResult<BaseVectorCertificationSchemaRecord> euclidean = (await collection.Vector(BaseVectorCertificationSchemaRecord.VectorIndexes.Euclidean).Nearest(BaseVector.Create([1, 0])).Take(3).ExecuteAsync(cancellationToken).ConfigureAwait(false)).RequireValue();
        BaseVectorResult<BaseVectorCertificationSchemaRecord> dot = (await collection.Vector(BaseVectorCertificationSchemaRecord.VectorIndexes.Dot).Nearest(BaseVector.Create([1, 0])).Take(3).ExecuteAsync(cancellationToken).ConfigureAwait(false)).RequireValue();
        string[] expected = ["record-a", "record-c", "record-b"];
        if (cosine.Matches.Length == 0 || euclidean.Matches.Length == 0 || dot.Matches.Length == 0) return false;
        if (!Ids(cosine).SequenceEqual(expected) || !Ids(euclidean).SequenceEqual(expected) || !Ids(dot).SequenceEqual(expected) ||
            !Near(cosine.Matches[0].Measure.Value, 0.9486832980505138) || !Near(euclidean.Matches[0].Measure.Value, Math.Sqrt(0.125)) || !Near(dot.Matches[0].Measure.Value, 0.75) ||
            cosine.Matches[0].Record.Revision != expectedARevision || cosine.Matches[0].Record.Value.Secret != "secret-a-v2")
            return false;
        BaseVectorQuery<BaseVectorCertificationSchemaRecord> query = collection.Vector(BaseVectorCertificationSchemaRecord.VectorIndexes.Cosine).Nearest(BaseVector.Create([1, 0]));
        BaseVectorResult<BaseVectorCertificationSchemaRecord> equality = (await query.Where(BaseVectorCertificationSchemaRecord.Fields.Active, true).Take(3).ExecuteAsync(cancellationToken).ConfigureAwait(false)).RequireValue();
        BaseVectorResult<BaseVectorCertificationSchemaRecord> @in = (await query.WhereAny(BaseVectorCertificationSchemaRecord.Fields.Priority, 10L, 30L).Take(3).ExecuteAsync(cancellationToken).ConfigureAwait(false)).RequireValue();
        BaseVectorResult<BaseVectorCertificationSchemaRecord> and = (await query.Where(BaseVectorCertificationSchemaRecord.Fields.Active, true).WhereAny(BaseVectorCertificationSchemaRecord.Fields.Priority, 10L, 30L).Take(3).ExecuteAsync(cancellationToken).ConfigureAwait(false)).RequireValue();
        BaseVectorResult<BaseVectorCertificationSchemaRecord> @null = (await query.Where(BaseVectorCertificationSchemaRecord.Fields.Optional, (string)null!).Take(3).ExecuteAsync(cancellationToken).ConfigureAwait(false)).RequireValue();
        BaseVectorResult<BaseVectorCertificationSchemaRecord> or = (await query.Where(BaseVectorCertificationSchemaRecord.Fields.Optional, (string)null!).OrWhere(BaseVectorCertificationSchemaRecord.Fields.Optional, "present").Take(3).ExecuteAsync(cancellationToken).ConfigureAwait(false)).RequireValue();
        if (!Ids(equality).SequenceEqual(["record-a", "record-c"]) || !Ids(@in).SequenceEqual(["record-a", "record-c"]) ||
            !Ids(and).SequenceEqual(["record-a", "record-c"]) || !Ids(@null).SequenceEqual(["record-a"]) ||
            !Ids(or).SequenceEqual(["record-a", "record-c", "record-b"])) return false;
        BaseVectorCertificationPolicy policy = (BaseVectorCertificationPolicy)unrestricted.Services
            .GetRequiredService<BasePolicyAuthorityOwner>().Policies.Single().Evaluator!;
        int policyCalls = Volatile.Read(ref policy.RestrictedVectorQueries);
        BaseSession restricted = host.Sessions.For(new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Authenticated, SubjectId = "certification-restricted" });
        BaseVectorResult<BaseVectorCertificationSchemaRecord> secured = (await restricted.Collection(BaseVectorCertificationSchemaRecord.Collection).Vector(BaseVectorCertificationSchemaRecord.VectorIndexes.Cosine)
            .Nearest(BaseVector.Create([0, 1])).Take(3).ExecuteAsync(cancellationToken).ConfigureAwait(false)).RequireValue();
        return Ids(secured).SequenceEqual(["record-a", "record-c"]) &&
            secured.Matches.Length == 2 &&
            secured.Matches.All(static match => match.Record.Redacted && match.Record.Value.Secret is null) &&
            Volatile.Read(ref policy.RestrictedVectorQueries) == policyCalls + 3;

        static IEnumerable<string> Ids(BaseVectorResult<BaseVectorCertificationSchemaRecord> result) => result.Matches.Select(static match => match.Record.Id.Value);
        static bool Near(double actual, double expected) => Math.Abs(actual - expected) <= 1e-6;
    }
    private static BaseVectorCertificationCaseResult Passed(string id) => new(id, BaseVectorCertificationCaseOutcome.Passed);
    private static BaseVectorCertificationCaseResult Failed(string id, string code, string message) => new(id, BaseVectorCertificationCaseOutcome.Failed, code, message);
}
