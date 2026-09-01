using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Testing;

internal static class BaseLogicalIndexProviderCertification
{
    private static readonly RecordMutationExecutionRequest AtomicExecution = new()
    {
        AcquisitionTimeout = TimeSpan.FromSeconds(1),
        TransactionTimeout = TimeSpan.FromSeconds(1),
        CommitCompletionTimeout = TimeSpan.FromSeconds(1),
    };

    internal static async ValueTask<BaseLogicalIndexCertificationReport> RunAsync(
        IBaseLogicalIndexCertificationFixture fixture,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        ValidateIdentity(fixture.Identity);
        var results = ImmutableArray.CreateBuilder<BaseLogicalIndexCertificationCaseResult>(
            BaseLogicalIndexProviderContract.CaseIds.Length);
        for (int ordinal = 0; ordinal < BaseLogicalIndexProviderContract.CaseIds.Length; ordinal++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            results.Add(await ExecuteCaseAsync(
                fixture, BaseLogicalIndexProviderContract.CaseIds[ordinal], ordinal,
                cancellationToken).ConfigureAwait(false));
        }
        return BaseLogicalIndexProviderContract.SealReport(new BaseLogicalIndexCertificationReport
        {
            ProviderId = fixture.Identity.ProviderId,
            ProviderVersion = fixture.Identity.ProviderVersion,
            StoreProviderKind = fixture.Identity.StoreProviderKind,
            StoreProviderProtocolVersion = HPDBaseStoreProviderFactory.ProtocolVersion,
            ProductionCapabilityChecksum = BaseLogicalIndexProviderContract
                .BuiltInCapability().Checksum,
            BoundedCertificationCapabilityChecksum = BaseLogicalIndexProviderContract
                .BoundedCertificationCapability().Checksum,
            Cases = results.MoveToImmutable(),
            ContractChecksum = BaseLogicalIndexProviderContract.ContractChecksum(),
            Checksum = [],
        });
    }

    private static async ValueTask<BaseLogicalIndexCertificationCaseResult> ExecuteCaseAsync(
        IBaseLogicalIndexCertificationFixture fixture,
        string caseId,
        int ordinal,
        CancellationToken cancellationToken)
    {
        if (caseId is "maximum" or "maximum-plus-one")
            return await ExecuteBoundaryCaseAsync(
                fixture, caseId, ordinal, caseId == "maximum-plus-one", cancellationToken)
                .ConfigureAwait(false);
        bool bounded = caseId is "maximum" or "maximum-plus-one";
        bool constrained = caseId == "point-policy";
        var pausing = caseId == "point-generation-conflict"
            && fixture.Identity.GenerationConflictStrategy ==
                BaseLogicalIndexGenerationConflictStrategy.OptimisticCapture
            ? new PausingSelectionItemPolicyEvaluator()
            : null;
        await using BaseLogicalIndexCertificationRoot root = await fixture.CreateRootAsync(
            new BaseLogicalIndexCertificationRootRequest
            {
                CertificationCapability = bounded
                    ? CertificationGraphCapability()
                    : null,
                ConstrainPolicyToTenantA = constrained,
                PolicyEvaluator = pausing,
            }, cancellationToken).ConfigureAwait(false);
        await using ServiceProvider provider = BaseLogicalIndexCertificationHost.Create(
            root.StoreProvider, constrained, pausing);
        BaseCollectionSession<BaseLogicalIndexCertificationItem> collection =
            await InitializeAsync(provider, root.SchemaStoreId, cancellationToken)
                .ConfigureAwait(false);
        BaseLogicalIndexDefinition unique = collection.Contract.Definition.Indexes!.Single(index =>
            index.Id.ToString() == "base.cert.logicalIndex.tenantCode.v1");
        BaseLogicalIndexDefinition arbitrated = collection.Contract.Definition.Indexes!.Single(index =>
            index.Id.ToString() == "base.cert.logicalIndex.aTenantCode.v1");
        BaseLogicalIndexDefinition ordered = collection.Contract.Definition.Indexes!.Single(index =>
            index.Id.ToString() == "base.cert.logicalIndex.tenantSequence.v1");
        var inspection = (IBaseLogicalIndexCertificationInspection)provider
            .GetRequiredService<IRecordStore>();
        BaseLogicalIndexCertificationSnapshot before = await inspection
            .InspectLogicalIndexForCertificationAsync(collection.Contract.Id, unique.Checksum,
                cancellationToken).ConfigureAwait(false);
        BaseLogicalIndexCertificationSnapshot after = before;
        OperationStatus status = OperationStatus.Ok;
        string? errorCode = null;
        ImmutableArray<byte> evidence = [];

        switch (caseId)
        {
            case "empty-directory":
                Require(before.Directory.EqualityPostings.IsEmpty
                    && before.Directory.ComparatorEntries.IsEmpty,
                    "base.logicalIndex.certification.emptyDirectoryInvalid");
                after = before;
                break;
            case "membership":
                await InsertCanonicalFourAsync(collection, cancellationToken).ConfigureAwait(false);
                after = await InspectAsync(inspection, collection, unique, cancellationToken)
                    .ConfigureAwait(false);
                Require(after.Directory.EqualityPostings.Sum(static posting => posting.RecordIds.Length) == 3
                    && after.Directory.ComparatorEntries.All(entry => entry.RecordId != Id(3).Value),
                    "base.logicalIndex.certification.membershipInvalid");
                break;
            case "equality-key":
                await InsertCanonicalFourAsync(collection, cancellationToken).ConfigureAwait(false);
                after = await InspectAsync(inspection, collection, unique, cancellationToken)
                    .ConfigureAwait(false);
                Require(FindPosting(after, Point(collection, "a", "x")) == Id(1).Value,
                    "base.logicalIndex.certification.equalityKeyInvalid");
                break;
            case "comparator-order":
                await InsertCanonicalFourAsync(collection, cancellationToken).ConfigureAwait(false);
                after = await InspectAsync(inspection, collection, ordered, cancellationToken)
                    .ConfigureAwait(false);
                Require(after.Directory.ComparatorEntries.Select(static entry => entry.RecordId)
                    .SequenceEqual([Id(3).Value, Id(4).Value, Id(1).Value, Id(2).Value]),
                    "base.logicalIndex.certification.comparatorOrderInvalid");
                break;
            case "insert":
                (await collection.CreateAsync(Id(1), Item("a", "x", 1), cancellationToken))
                    .RequireValue();
                after = await InspectAsync(inspection, collection, unique, cancellationToken)
                    .ConfigureAwait(false);
                Require(FindPosting(after, Point(collection, "a", "x")) == Id(1).Value,
                    "base.logicalIndex.certification.insertInvalid");
                break;
            case "update-key-move":
                (await collection.CreateAsync(Id(1), Item("a", "x", 1), cancellationToken))
                    .RequireValue();
                before = await InspectAsync(inspection, collection, unique, cancellationToken)
                    .ConfigureAwait(false);
                (await collection.ReplaceAsync(Id(1), Item("a", "z", 1),
                    cancellationToken: cancellationToken))
                    .RequireValue();
                after = await InspectAsync(inspection, collection, unique, cancellationToken)
                    .ConfigureAwait(false);
                Require(!HasPosting(after, Point(collection, "a", "x"))
                    && FindPosting(after, Point(collection, "a", "z")) == Id(1).Value
                    && before.Authority.DirectoryPublicationChecksum.AsSpan().SequenceEqual(
                        after.Authority.PreviousDirectoryPublicationChecksum.AsSpan()),
                    "base.logicalIndex.certification.keyMoveInvalid");
                break;
            case "delete":
                (await collection.CreateAsync(Id(1), Item("a", "x", 1), cancellationToken))
                    .RequireValue();
                before = await InspectAsync(inspection, collection, unique, cancellationToken)
                    .ConfigureAwait(false);
                (await collection.DeleteAsync(Id(1), cancellationToken: cancellationToken)).RequireValue();
                after = await InspectAsync(inspection, collection, unique, cancellationToken)
                    .ConfigureAwait(false);
                Require(after.Directory.EqualityPostings.IsEmpty
                    && after.Directory.ComparatorEntries.IsEmpty,
                    "base.logicalIndex.certification.deleteInvalid");
                break;
            case "unique-final-overlay":
                await InsertPairAsync(collection, cancellationToken).ConfigureAwait(false);
                before = await InspectAsync(inspection, collection, unique, cancellationToken)
                    .ConfigureAwait(false);
                BaseBatchBuilder swap = collection.Session.Atomic(
                    BaseLogicalIndexCertificationHost.Identity(caseId));
                swap.Replace(collection.Contract, Id(1), Item("a", "y", 1));
                swap.Replace(collection.Contract, Id(2), Item("a", "x", 2));
                (await swap.CommitAsync(cancellationToken)).RequireValue();
                after = await InspectAsync(inspection, collection, unique, cancellationToken)
                    .ConfigureAwait(false);
                Require(FindPosting(after, Point(collection, "a", "x")) == Id(2).Value
                    && FindPosting(after, Point(collection, "a", "y")) == Id(1).Value,
                    "base.logicalIndex.certification.finalOverlayInvalid");
                break;
            case "duplicate-conflict":
                await InsertPairAsync(collection, cancellationToken).ConfigureAwait(false);
                before = await InspectAsync(inspection, collection, unique, cancellationToken)
                    .ConfigureAwait(false);
                BaseResult<BaseRecord<BaseLogicalIndexCertificationItem>> duplicate =
                    await collection.ReplaceAsync(
                        Id(2), Item("a", "x", 2), cancellationToken: cancellationToken);
                (status, errorCode) = Failure(duplicate);
                after = await InspectAsync(inspection, collection, unique, cancellationToken)
                    .ConfigureAwait(false);
                Require(SameAuthority(before, after),
                    "base.logicalIndex.certification.conflictPublished");
                break;
            case "point-hit":
                await InsertCanonicalFourAsync(collection, cancellationToken).ConfigureAwait(false);
                before = await InspectAsync(inspection, collection, unique, cancellationToken)
                    .ConfigureAwait(false);
                BaseSelectionMutationResult hit = (await PatchPointAsync(
                    collection, "a", "x", caseId, cancellationToken)).RequireValue();
                Require(hit.SelectedCount == 1 && hit.MutatedCount == 1
                    && (await collection.GetAsync(Id(1), cancellationToken)).RequireValue().Value.Sequence == 9,
                    "base.logicalIndex.certification.pointHitInvalid");
                BaseSelectionMutationResult replayedHit = (await PatchPointAsync(
                    collection, "a", "x", caseId, cancellationToken)).RequireValue();
                Require(replayedHit.SelectedCount == hit.SelectedCount
                    && replayedHit.MutatedCount == hit.MutatedCount
                    && replayedHit.RequestDisposition == BaseMutationRequestDisposition.Duplicate,
                    "base.logicalIndex.certification.pointReplayInvalid");
                BaseCapturedAtomicExecution hitCapture = await CaptureAsync(
                    provider, collection, Query(PointPredicate("a", "x").Expression), cancellationToken)
                    .ConfigureAwait(false);
                BaseLogicalIndexSelectionEvidence hitEvidence =
                    hitCapture.Selection?.LogicalIndexEvidence
                    ?? throw new InvalidOperationException(
                        "base.logicalIndex.certification.pointEvidenceInvalid");
                Require(hitCapture.Selection!.Records.Length == 1
                    && PointEvidenceMatches(hitEvidence, arbitrated),
                    "base.logicalIndex.certification.pointEvidenceInvalid");
                evidence = hitEvidence.Checksum;
                after = await InspectAsync(inspection, collection, unique, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case "point-miss":
                await InsertCanonicalFourAsync(collection, cancellationToken).ConfigureAwait(false);
                before = await InspectAsync(inspection, collection, unique, cancellationToken)
                    .ConfigureAwait(false);
                BaseSelectionMutationResult miss = (await PatchPointAsync(
                    collection, "a", "z", caseId, cancellationToken)).RequireValue();
                Require(miss.SelectedCount == 0 && miss.MutatedCount == 0,
                    "base.logicalIndex.certification.pointMissInvalid");
                BaseCapturedAtomicExecution missCapture = await CaptureAsync(
                    provider, collection, Query(PointPredicate("a", "z").Expression), cancellationToken)
                    .ConfigureAwait(false);
                BaseLogicalIndexSelectionEvidence missEvidence =
                    missCapture.Selection?.LogicalIndexEvidence
                    ?? throw new InvalidOperationException(
                        "base.logicalIndex.certification.pointEvidenceInvalid");
                Require(missCapture.Selection!.Records.IsEmpty
                    && PointEvidenceMatches(missEvidence, arbitrated),
                    "base.logicalIndex.certification.pointEvidenceInvalid");
                evidence = missEvidence.Checksum;
                after = await InspectAsync(inspection, collection, unique, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case "point-policy":
                (await collection.CreateAsync(Id(1), Item("a", "x", 1), cancellationToken))
                    .RequireValue();
                (await collection.CreateAsync(Id(4), Item("b", "x", 4), cancellationToken))
                    .RequireValue();
                before = await InspectAsync(inspection, collection, unique, cancellationToken)
                    .ConfigureAwait(false);
                BaseSelectionMutationResult policy = (await collection.Query()
                    .Where(BaseLogicalIndexCertificationItem.Fields.Code.Equal("x"))
                    .OrderBy(BaseLogicalIndexCertificationItem.Fields.Sequence).ThenByRecordId()
                    .Take(4).PatchSelectedAsync(
                        collection.GetMergePatchSelectionProfile(
                            BaseLogicalIndexCertificationHost.ProfileIdentity()),
                        BaseLogicalIndexCertificationHost.SequencePatch(9),
                        BasePreviousStateRequirement.None,
                        BaseLogicalIndexCertificationHost.Identity(caseId),
                        cancellationToken: cancellationToken)).RequireValue();
                Require(policy.SelectedCount == 1
                    && (await collection.GetAsync(Id(1), cancellationToken)).RequireValue().Value.Sequence == 9
                    && (await collection.GetAsync(Id(4), cancellationToken)).RequireValue().Value.Sequence == 4,
                    "base.logicalIndex.certification.pointPolicyInvalid");
                BaseCapturedAtomicExecution policyCapture = await CaptureAsync(
                    provider, collection, Query(PointPredicate("a", "x").Expression),
                    cancellationToken).ConfigureAwait(false);
                BaseLogicalIndexSelectionEvidence policyEvidence =
                    policyCapture.Selection?.LogicalIndexEvidence
                    ?? throw new InvalidOperationException(
                        "base.logicalIndex.certification.pointPolicyEvidenceInvalid");
                Require(policyCapture.Selection!.Records.Length == 1
                    && PointEvidenceMatches(policyEvidence, arbitrated),
                    "base.logicalIndex.certification.pointPolicyEvidenceInvalid");
                evidence = policyEvidence.Checksum;
                after = await InspectAsync(inspection, collection, unique, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case "point-generation-conflict":
                (status, errorCode, before, after, evidence) = await ExecuteGenerationConflictAsync(
                    fixture, root, provider, collection, inspection, unique, pausing,
                    cancellationToken).ConfigureAwait(false);
                break;
            case "scan-fallback":
                await InsertCanonicalFourAsync(collection, cancellationToken).ConfigureAwait(false);
                before = await InspectAsync(inspection, collection, unique, cancellationToken)
                    .ConfigureAwait(false);
                BaseCapturedAtomicExecution scan = await CaptureAsync(provider, collection,
                    Query(BaseLogicalIndexCertificationItem.Fields.Sequence.GreaterThan(1).Expression),
                    cancellationToken).ConfigureAwait(false);
                Require(scan.Selection?.LogicalIndexEvidence is null
                    && scan.Selection?.Records.Select(static record =>
                        record.MaterializeOwned().Id.Value).SequenceEqual(
                            [Id(2).Value, Id(3).Value, Id(4).Value]) == true
                    && scan.ReadIntervals.Length == 1
                    && scan.ReadIntervals[0].LogicalAccessPathId.StartsWith(
                        "collection:", StringComparison.Ordinal),
                    "base.logicalIndex.certification.scanFallbackInvalid");
                evidence = Evidence(caseId, scan.ReadIntervals[0].CanonicalUpperBound.AsSpan());
                after = await InspectAsync(inspection, collection, unique, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case "maximum":
                for (int index = 1; index <= 4; index++)
                    (await collection.CreateAsync(Id(index),
                        Item($"t{index}", $"v{index:0000}", index), cancellationToken)).RequireValue();
                after = await InspectAsync(inspection, collection, unique, cancellationToken)
                    .ConfigureAwait(false);
                Require(after.Directory.Accounting.Records == 4
                    && after.Directory.Accounting.Postings == 4,
                    "base.logicalIndex.certification.maximumInvalid");
                break;
            case "maximum-plus-one":
                for (int index = 1; index <= 4; index++)
                    (await collection.CreateAsync(Id(index),
                        Item($"t{index}", $"v{index:0000}", index), cancellationToken)).RequireValue();
                before = await InspectAsync(inspection, collection, unique, cancellationToken)
                    .ConfigureAwait(false);
                BaseResult<BaseRecord<BaseLogicalIndexCertificationItem>> overflow =
                    await collection.CreateAsync(Id(5), Item("t5", "v0005", 5), cancellationToken);
                (status, errorCode) = Failure(overflow);
                after = await InspectAsync(inspection, collection, unique, cancellationToken)
                    .ConfigureAwait(false);
                Require(SameAuthority(before, after),
                    "base.logicalIndex.certification.maximumPlusOnePublished");
                break;
            case "hostile-member-set":
                (await collection.CreateAsync(Id(1), Item("a", "x", 1), cancellationToken))
                    .RequireValue();
                before = await InspectAsync(inspection, collection, unique, cancellationToken)
                    .ConfigureAwait(false);
                await inspection.CorruptLogicalIndexMemberSetForCertificationAsync(
                    collection.Contract.Id, arbitrated.Checksum, cancellationToken)
                    .ConfigureAwait(false);
                BaseResult<BaseSelectionMutationResult> hostile = await PatchPointAsync(
                    collection, "a", "x", caseId, cancellationToken);
                (status, errorCode) = Failure(hostile);
                Require(!inspection.LogicalIndexesReady,
                    "base.logicalIndex.certification.quarantineMissing");
                after = before;
                break;
            case "hostile-result-ownership":
                (await collection.CreateAsync(Id(1), Item("a", "x", 1), cancellationToken))
                    .RequireValue();
                before = await InspectAsync(inspection, collection, unique, cancellationToken)
                    .ConfigureAwait(false);
                (BaseAtomicExecutionRequest hostileRequest, BaseAtomicMutationExecutionLimits limits) =
                    await CreateCaptureRequestAsync(provider, collection,
                        Query(PointPredicate("a", "x").Expression), cancellationToken)
                        .ConfigureAwait(false);
                var hostileProbe = new HostileResultOwnershipProbe(hostileRequest, limits);
                RecordMutationExecutionResult hostileExecution = await provider
                    .GetRequiredService<IAtomicRecordStore>()
                    .ExecuteAtomicAsync(hostileProbe, AtomicExecution, cancellationToken)
                    .ConfigureAwait(false);
                Require(hostileExecution.Outcome == RecordMutationExecutionOutcome.RollbackConfirmed
                    && hostileProbe.PrepareResult is { Status: OperationStatus.StoreError,
                        Error.Code: BaseSchemaErrorCodes.ProviderEvidenceInvalid }
                    && !inspection.LogicalIndexesReady,
                    "base.logicalIndex.certification.hostileOwnershipInvalid");
                status = hostileProbe.PrepareResult!.Status;
                errorCode = hostileProbe.PrepareResult.Error!.Code;
                after = before;
                break;
            default:
                throw new InvalidOperationException("base.logicalIndex.certification.caseUnknown");
        }

        (OperationStatus expectedStatus, string? expectedCode) =
            BaseLogicalIndexProviderContract.ExpectedOutcome(caseId);
        Require(status == expectedStatus && string.Equals(errorCode, expectedCode,
            StringComparison.Ordinal),
            $"base.logicalIndex.certification.outcomeInvalid:{caseId}:{status}:{errorCode ?? "<null>"}:{expectedStatus}:{expectedCode ?? "<null>"}");
        BaseLogicalIndexCertificationAccounting accounting = VerifyAccounting(
            caseId, collection, unique, ordered, after);
        if (evidence.IsEmpty)
            evidence = Evidence(caseId, before.Authority.MemberSetChecksum.AsSpan(),
                after.Authority.MemberSetChecksum.AsSpan(), accounting);
        return new BaseLogicalIndexCertificationCaseResult
        {
            Id = caseId,
            Ordinal = ordinal,
            ObservedStatus = status,
            ObservedErrorCode = errorCode,
            Accounting = accounting,
            BeforeMemberSetChecksum = before.Authority.MemberSetChecksum,
            AfterMemberSetChecksum = after.Authority.MemberSetChecksum,
            BeforePublicationChecksum = before.Authority.DirectoryPublicationChecksum,
            AfterPublicationChecksum = after.Authority.DirectoryPublicationChecksum,
            EvidenceChecksum = evidence,
        };
    }

    private static readonly ImmutableArray<string> BoundaryDimensionIds =
    [
        "indexes-per-collection",
        "parts-per-index",
        "predicate-nodes-per-index",
        "canonical-key-bytes",
        "indexed-records-per-collection",
        "postings-per-index",
        "records-per-posting-key",
        "postings-per-store",
        "directory-bytes-per-index",
        "directory-bytes-per-store",
        "directory-predicate-evaluations-per-publication",
        "directory-key-bytes-per-index",
        "directory-transient-bytes-per-operation",
    ];

    private static async ValueTask<BaseLogicalIndexCertificationCaseResult>
        ExecuteBoundaryCaseAsync(
            IBaseLogicalIndexCertificationFixture fixture,
            string caseId,
            int ordinal,
            bool overflow,
            CancellationToken cancellationToken)
    {
        var observed = ImmutableArray.CreateBuilder<BoundaryObservation>(
            BoundaryDimensionIds.Length);
        foreach (string dimension in BoundaryDimensionIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            observed.Add(await ExecuteBoundaryDimensionAsync(
                fixture, dimension, overflow, cancellationToken).ConfigureAwait(false));
        }

        OperationStatus expectedStatus = overflow
            ? OperationStatus.CapabilityUnavailable
            : OperationStatus.Ok;
        string? expectedError = overflow
            ? BaseSchemaErrorCodes.CapabilityUnavailable
            : null;
        foreach (BoundaryObservation item in observed)
            Require(item.Status == expectedStatus
                && string.Equals(item.ErrorCode, expectedError, StringComparison.Ordinal),
                $"base.logicalIndex.certification.boundaryOutcomeInvalid:{item.Dimension}");

        BaseLogicalIndexCertificationAccounting aggregate = SumAccounting(
            observed.Select(static value => value.Accounting));
        ImmutableArray<byte> beforeMember = BoundaryDigest(
            "before-member", observed, static value => value.BeforeMemberSetChecksum);
        ImmutableArray<byte> afterMember = BoundaryDigest(
            "after-member", observed, static value => value.AfterMemberSetChecksum);
        ImmutableArray<byte> beforePublication = BoundaryDigest(
            "before-publication", observed,
            static value => value.BeforePublicationChecksum);
        ImmutableArray<byte> afterPublication = BoundaryDigest(
            "after-publication", observed,
            static value => value.AfterPublicationChecksum);
        ImmutableArray<byte> evidence = BoundaryEvidence(caseId, observed);
        return new BaseLogicalIndexCertificationCaseResult
        {
            Id = caseId,
            Ordinal = ordinal,
            ObservedStatus = expectedStatus,
            ObservedErrorCode = expectedError,
            Accounting = aggregate,
            BeforeMemberSetChecksum = beforeMember,
            AfterMemberSetChecksum = afterMember,
            BeforePublicationChecksum = beforePublication,
            AfterPublicationChecksum = afterPublication,
            EvidenceChecksum = evidence,
        };
    }

    private static async ValueTask<BoundaryObservation> ExecuteBoundaryDimensionAsync(
        IBaseLogicalIndexCertificationFixture fixture,
        string dimension,
        bool overflow,
        CancellationToken cancellationToken)
    {
        BoundaryWorkload workload = BoundaryWorkloadFor(dimension);
        BaseLogicalIndexProviderCapability capability = BoundaryCapability(
            dimension, overflow, workload);
        await using BaseLogicalIndexCertificationRoot root = await fixture.CreateRootAsync(
            new BaseLogicalIndexCertificationRootRequest
            {
                CertificationCapability = capability,
                ConstrainPolicyToTenantA = false,
            }, cancellationToken).ConfigureAwait(false);
        await using ServiceProvider provider = BaseLogicalIndexCertificationHost.Create(
            root.StoreProvider);

        OperationResult<BaseApplicationReadiness> initialized = await TryInitializeAsync(
            provider, root.SchemaStoreId, cancellationToken).ConfigureAwait(false);
        bool schemaDimension = dimension is "indexes-per-collection"
            or "parts-per-index" or "predicate-nodes-per-index";
        if (schemaDimension && overflow)
        {
            Require(!initialized.IsSuccess()
                && string.Equals(initialized.Error?.Code,
                    BaseSchemaErrorCodes.CapabilityUnavailable, StringComparison.Ordinal),
                $"base.logicalIndex.certification.boundaryInitializationInvalid:{dimension}");
            ImmutableArray<byte> empty = BoundaryEmptyAuthority(dimension, capability);
            return new BoundaryObservation(
                dimension, capability.Checksum,
                OperationStatus.CapabilityUnavailable,
                BaseSchemaErrorCodes.CapabilityUnavailable,
                ZeroAccounting(), empty, empty, empty, empty);
        }
        Require(initialized.IsSuccess(),
            initialized.Error?.Code
                ?? $"base.logicalIndex.certification.boundaryInitializationInvalid:{dimension}");
        if (schemaDimension)
        {
            ImmutableArray<byte> empty = BoundaryEmptyAuthority(dimension, capability);
            return new BoundaryObservation(
                dimension, capability.Checksum,
                OperationStatus.Ok, null,
                ZeroAccounting(), empty, empty, empty, empty);
        }

        BaseCollectionSession<BaseLogicalIndexCertificationItem> collection = provider
            .GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
            {
                AuthenticationState = PrincipalAuthenticationState.Admin,
                SubjectKind = AccessSubjectKind.User,
                SubjectId = "base-certification",
            }).Collection(BaseLogicalIndexCertificationItem.Collection);
        BaseLogicalIndexDefinition index = collection.Contract.Definition.Indexes!.Single(value =>
            value.Id.ToString() == workload.IndexId);
        var inspection = (IBaseLogicalIndexCertificationInspection)provider
            .GetRequiredService<IRecordStore>();
        foreach (ExpectedItem item in workload.Prior)
        {
            BaseResult<BaseRecord<BaseLogicalIndexCertificationItem>> priorResult =
                await collection.CreateAsync(Id(item.Ordinal), item.Value, cancellationToken);
            (_, string? priorError) = priorResult.TryGetValue(out _)
                ? (OperationStatus.Ok, null)
                : Failure(priorResult);
            Require(priorResult.TryGetValue(out _),
                $"base.logicalIndex.certification.boundaryPriorRejected:{dimension}:"
                + (priorError ?? "unknown"));
        }
        BaseLogicalIndexCertificationSnapshot before = await InspectAsync(
            inspection, collection, index, cancellationToken).ConfigureAwait(false);

        OperationStatus status = OperationStatus.Ok;
        string? errorCode = null;
        if (workload.Final.Length > workload.Prior.Length)
        {
            ExpectedItem item = workload.Final[^1];
            BaseResult<BaseRecord<BaseLogicalIndexCertificationItem>> result =
                await collection.CreateAsync(Id(item.Ordinal), item.Value, cancellationToken);
            if (overflow)
                (status, errorCode) = Failure(result);
            else
            {
                (_, string? finalError) = result.TryGetValue(out _)
                    ? (OperationStatus.Ok, null)
                    : Failure(result);
                Require(result.TryGetValue(out _),
                    $"base.logicalIndex.certification.boundaryFinalRejected:{dimension}:"
                    + (finalError ?? "unknown"));
            }
        }
        BaseLogicalIndexCertificationSnapshot after = await InspectAsync(
            inspection, collection, index, cancellationToken).ConfigureAwait(false);
        BaseLogicalIndexCertificationAccounting accounting;
        if (overflow)
        {
            ExpectedItem[] retainedPrior = workload.Prior.Length == 0
                ? []
                : workload.Prior[..^1];
            accounting = VerifyBoundaryAccounting(
                collection.Contract,
                index,
                retainedPrior,
                workload.Prior,
                staged: workload.Prior.Length != 0,
                before);
        }
        else
        {
            accounting = VerifyBoundaryAccounting(
                collection.Contract,
                index,
                workload.Prior,
                workload.Final,
                staged: true,
                after);
        }
        if (overflow)
            Require(SameAuthority(before, after),
                $"base.logicalIndex.certification.boundaryPublished:{dimension}");
        return new BoundaryObservation(
            dimension, capability.Checksum,
            status, errorCode, accounting,
            before.Authority.MemberSetChecksum,
            after.Authority.MemberSetChecksum,
            before.Authority.DirectoryPublicationChecksum,
            after.Authority.DirectoryPublicationChecksum);
    }

    private static BoundaryWorkload BoundaryWorkloadFor(string dimension)
    {
        ExpectedItem[] canonical =
        [
            new(1, Item("t1", "v0001", 1)),
            new(2, Item("t2", "v0002", 2)),
            new(3, Item("t3", "v0003", 3)),
            new(4, Item("t4", "v0004", 4)),
        ];
        if (dimension == "canonical-key-bytes")
        {
            ExpectedItem[] one = [new(1, Item(new string('t', 24), "v0001", 1))];
            return new BoundaryWorkload(
                "base.cert.logicalIndex.tenantCode.v1", [], one);
        }
        if (dimension == "records-per-posting-key")
        {
            ExpectedItem[] samePosting =
            [
                new(1, Item("a", "w", 1)), new(2, Item("a", "x", 1)),
                new(3, Item("a", "y", 1)), new(4, Item("a", "z", 1)),
            ];
            return new BoundaryWorkload(
                "base.cert.logicalIndex.tenantSequence.v1",
                samePosting[..3], samePosting);
        }
        string indexId = dimension == "parts-per-index"
            ? "base.cert.logicalIndex.tenantCode.v1"
            : "base.cert.logicalIndex.tenantCode.v1";
        return new BoundaryWorkload(indexId, canonical[..3], canonical);
    }

    private static BaseLogicalIndexProviderCapability BoundaryCapability(
        string dimension,
        bool overflow,
        BoundaryWorkload workload)
    {
        BaseLogicalIndexProviderCapability bounded = CertificationGraphCapability();
        BaseLogicalIndexDefinition[] indexes = BaseLogicalIndexCertificationItem.Collection
            .Definition.Indexes!.Where(static value => value.StoreRequired).ToArray();
        BaseLogicalIndexDefinition target = indexes.Single(value =>
            value.Id.ToString() == workload.IndexId);
        long storePostings = indexes.Sum(index => IndependentlyEncodeDirectory(
            BaseLogicalIndexCertificationItem.Collection, index, workload.Final).Members);
        long storeBytes = indexes.Sum(index => IndependentlyEncodeDirectory(
            BaseLogicalIndexCertificationItem.Collection, index, workload.Final).RetainedBytes);
        long maximumIndexPostings = indexes.Max(index => IndependentlyEncodeDirectory(
            BaseLogicalIndexCertificationItem.Collection, index, workload.Final).Members);
        int maximumPostingRecords = indexes.Max(index => MaximumPostingRecords(
            BaseLogicalIndexCertificationItem.Collection, index, workload.Final));
        long maximumIndexBytes = indexes.Max(index => IndependentlyEncodeDirectory(
            BaseLogicalIndexCertificationItem.Collection, index, workload.Final).RetainedBytes);
        long maximumIndexKeyBytes = indexes.Max(index => IndependentlyEncodeDirectory(
            BaseLogicalIndexCertificationItem.Collection, index, workload.Final).KeyBytes);
        int maximumCanonicalKeyBytes = indexes.Max(index => MaximumCanonicalKeyBytes(
            BaseLogicalIndexCertificationItem.Collection, index, workload.Final));
        long maximumTransientBytes = indexes.Max(index =>
        {
            IndependentDirectory indexPrior = IndependentlyEncodeDirectory(
                BaseLogicalIndexCertificationItem.Collection, index, workload.Prior);
            IndependentDirectory indexFinal = IndependentlyEncodeDirectory(
                BaseLogicalIndexCertificationItem.Collection, index, workload.Final);
            return checked(indexPrior.RetainedBytes + 32L + indexFinal.RetainedBytes);
        });
        BaseLogicalIndexProviderCapability baseline =
            BaseLogicalIndexProviderContract.SealCapability(bounded with
            {
                MaximumCanonicalKeyBytes = Math.Max(
                    bounded.MaximumCanonicalKeyBytes, maximumCanonicalKeyBytes),
                MaximumIndexedRecordsPerCollection = Math.Max(
                    bounded.MaximumIndexedRecordsPerCollection, workload.Final.Length),
                MaximumPostingsPerIndex = Math.Max(
                    bounded.MaximumPostingsPerIndex, maximumIndexPostings),
                MaximumPostingRecordsPerKey = Math.Max(
                    bounded.MaximumPostingRecordsPerKey, maximumPostingRecords),
                MaximumPostingsPerStore = Math.Max(
                    bounded.MaximumPostingsPerStore, storePostings),
                MaximumDirectoryBytesPerIndex = Math.Max(
                    bounded.MaximumDirectoryBytesPerIndex, maximumIndexBytes),
                MaximumDirectoryBytesPerStore = Math.Max(
                    bounded.MaximumDirectoryBytesPerStore, storeBytes),
                MaximumDirectoryPredicateEvaluationsPerPublication = Math.Max(
                    bounded.MaximumDirectoryPredicateEvaluationsPerPublication,
                    workload.Final.Length),
                MaximumDirectoryKeyBytesPerIndex = Math.Max(
                    bounded.MaximumDirectoryKeyBytesPerIndex, maximumIndexKeyBytes),
                MaximumDirectoryTransientBytesPerOperation = Math.Max(
                    bounded.MaximumDirectoryTransientBytesPerOperation,
                    maximumTransientBytes),
                Checksum = [],
            });
        long targetValue = dimension switch
        {
            "indexes-per-collection" => indexes.Length,
            "parts-per-index" => indexes.Max(static value => value.Parts.Length),
            "predicate-nodes-per-index" => indexes.Max(
                static value => value.MembershipPredicate.Nodes.Length),
            "canonical-key-bytes" => maximumCanonicalKeyBytes,
            "indexed-records-per-collection" => workload.Final.Length,
            "postings-per-index" => maximumIndexPostings,
            "records-per-posting-key" => MaximumPostingRecords(
                BaseLogicalIndexCertificationItem.Collection, target, workload.Final),
            "postings-per-store" => storePostings,
            "directory-bytes-per-index" => maximumIndexBytes,
            "directory-bytes-per-store" => storeBytes,
            "directory-predicate-evaluations-per-publication" => workload.Final.Length,
            "directory-key-bytes-per-index" => maximumIndexKeyBytes,
            "directory-transient-bytes-per-operation" => maximumTransientBytes,
            _ => throw new InvalidOperationException(
                "base.logicalIndex.certification.boundaryDimensionUnknown"),
        };
        long admitted = overflow ? checked(targetValue - 1) : targetValue;
        Require(admitted > 0,
            $"base.logicalIndex.certification.boundaryTargetInvalid:{dimension}");
        BaseLogicalIndexProviderCapability derived = dimension switch
        {
            "indexes-per-collection" => baseline with
                { MaximumIndexesPerCollection = checked((int)admitted), Checksum = [] },
            "parts-per-index" => baseline with
                { MaximumPartsPerIndex = checked((int)admitted), Checksum = [] },
            "predicate-nodes-per-index" => baseline with
                { MaximumPredicateNodesPerIndex = checked((int)admitted), Checksum = [] },
            "canonical-key-bytes" => baseline with
                { MaximumCanonicalKeyBytes = checked((int)admitted), Checksum = [] },
            "indexed-records-per-collection" => baseline with
                { MaximumIndexedRecordsPerCollection = admitted, Checksum = [] },
            "postings-per-index" => baseline with
                {
                    MaximumPostingsPerIndex = admitted,
                    MaximumPostingsPerStore = Math.Max(
                        baseline.MaximumPostingsPerStore, admitted),
                    Checksum = [],
                },
            "records-per-posting-key" => baseline with
                { MaximumPostingRecordsPerKey = checked((int)admitted), Checksum = [] },
            "postings-per-store" => baseline with
                {
                    MaximumPostingsPerIndex = Math.Min(
                        baseline.MaximumPostingsPerIndex, admitted),
                    MaximumPostingsPerStore = admitted,
                    Checksum = [],
                },
            "directory-bytes-per-index" => baseline with
                {
                    MaximumDirectoryBytesPerIndex = admitted,
                    MaximumDirectoryBytesPerStore = Math.Max(
                        baseline.MaximumDirectoryBytesPerStore, admitted),
                    Checksum = [],
                },
            "directory-bytes-per-store" => baseline with
                {
                    MaximumDirectoryBytesPerIndex = indexes.Max(index =>
                        IndependentlyEncodeDirectory(
                            BaseLogicalIndexCertificationItem.Collection,
                            index, workload.Final).RetainedBytes),
                    MaximumDirectoryBytesPerStore = admitted,
                    Checksum = [],
                },
            "directory-predicate-evaluations-per-publication" => baseline with
                {
                    MaximumDirectoryPredicateEvaluationsPerPublication = admitted,
                    Checksum = [],
                },
            "directory-key-bytes-per-index" => baseline with
                { MaximumDirectoryKeyBytesPerIndex = admitted, Checksum = [] },
            "directory-transient-bytes-per-operation" => baseline with
                {
                    MaximumDirectoryTransientBytesPerOperation = admitted,
                    Checksum = [],
                },
            _ => throw new InvalidOperationException(
                "base.logicalIndex.certification.boundaryDimensionUnknown"),
        };
        return BaseLogicalIndexProviderContract.SealCapability(derived);
    }

    private static BaseLogicalIndexCertificationAccounting VerifyBoundaryAccounting(
        BaseCollection<BaseLogicalIndexCertificationItem> collection,
        BaseLogicalIndexDefinition index,
        ExpectedItem[] prior,
        ExpectedItem[] final,
        bool staged,
        BaseLogicalIndexCertificationSnapshot snapshot)
    {
        BaseLogicalIndexCertificationAccounting expected = IndependentlyEncodeAccounting(
            collection, index, prior, final, staged);
        BaseLogicalIndexDirectoryAccounting observed = snapshot.Directory.Accounting;
        Require(observed.Records == expected.Records
            && observed.PredicateEvaluations == expected.PredicateEvaluations
            && observed.Keys == expected.Keys
            && observed.KeyBytes == expected.KeyBytes
            && observed.PostingKeys == expected.PostingKeys
            && observed.Postings == expected.Postings
            && observed.ComparatorEntries == expected.ComparatorEntries
            && observed.Comparisons >= 0
            && observed.Comparisons <= ComparisonCeiling(final.Length)
            && observed.EvidenceBytes == expected.EvidenceBytes
            && observed.RetainedDirectoryBytes == expected.RetainedDirectoryBytes
            && observed.TransientBytes == expected.TransientBytes,
            "base.logicalIndex.certification.boundaryAccountingInvalid");
        return expected with { Comparisons = observed.Comparisons };
    }

    private static int MaximumCanonicalKeyBytes(
        BaseCollection<BaseLogicalIndexCertificationItem> collection,
        BaseLogicalIndexDefinition index,
        IEnumerable<ExpectedItem> records) => records.Max(item =>
    {
        RecordPayload payload = Payload(item.Value);
        return BaseLogicalIndexEvaluator.Key(collection.Definition, index, payload).Length;
    });

    private static int MaximumPostingRecords(
        BaseCollection<BaseLogicalIndexCertificationItem> collection,
        BaseLogicalIndexDefinition index,
        IEnumerable<ExpectedItem> records) => records
        .Select(item => Convert.ToHexString(BaseLogicalIndexEvaluator.Key(
            collection.Definition, index, Payload(item.Value))))
        .GroupBy(static value => value, StringComparer.Ordinal)
        .Max(static group => group.Count());

    private static RecordPayload Payload(BaseLogicalIndexCertificationItem value)
    {
        JsonElement encoded = JsonSerializer.SerializeToElement(value,
            BaseLogicalIndexCertificationJsonContext.Default
                .BaseLogicalIndexCertificationItem);
        return new RecordPayload
        {
            Kind = RecordPayloadKind.FieldMap,
            Fields = encoded.EnumerateObject().ToDictionary(
                static property => property.Name,
                static property => property.Value.Clone(),
                StringComparer.Ordinal),
        };
    }

    private static BaseLogicalIndexCertificationAccounting SumAccounting(
        IEnumerable<BaseLogicalIndexCertificationAccounting> values)
    {
        BaseLogicalIndexCertificationAccounting sum = ZeroAccounting();
        foreach (BaseLogicalIndexCertificationAccounting value in values)
            sum = new BaseLogicalIndexCertificationAccounting
            {
                Records = checked(sum.Records + value.Records),
                PredicateEvaluations = checked(sum.PredicateEvaluations + value.PredicateEvaluations),
                Keys = checked(sum.Keys + value.Keys),
                KeyBytes = checked(sum.KeyBytes + value.KeyBytes),
                PostingKeys = checked(sum.PostingKeys + value.PostingKeys),
                Postings = checked(sum.Postings + value.Postings),
                ComparatorEntries = checked(sum.ComparatorEntries + value.ComparatorEntries),
                Comparisons = checked(sum.Comparisons + value.Comparisons),
                EvidenceBytes = checked(sum.EvidenceBytes + value.EvidenceBytes),
                RetainedDirectoryBytes = checked(
                    sum.RetainedDirectoryBytes + value.RetainedDirectoryBytes),
                TransientBytes = checked(sum.TransientBytes + value.TransientBytes),
            };
        return sum;
    }

    private static BaseLogicalIndexCertificationAccounting ZeroAccounting() => new()
    {
        Records = 0,
        PredicateEvaluations = 0,
        Keys = 0,
        KeyBytes = 0,
        PostingKeys = 0,
        Postings = 0,
        ComparatorEntries = 0,
        Comparisons = 0,
        EvidenceBytes = 0,
        RetainedDirectoryBytes = 0,
        TransientBytes = 0,
    };

    private static ImmutableArray<byte> BoundaryEmptyAuthority(
        string dimension,
        BaseLogicalIndexProviderCapability capability) => SHA256.HashData(
            Encoding.UTF8.GetBytes($"base.logicalIndex.boundaryEmpty.v1\n{dimension}\n"
                + Convert.ToHexString(capability.Checksum.AsSpan())))
            .ToImmutableArray();

    private static ImmutableArray<byte> BoundaryDigest(
        string purpose,
        IEnumerable<BoundaryObservation> values,
        Func<BoundaryObservation, ImmutableArray<byte>> select)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(
            $"base.logicalIndex.boundaryDigest.v1\n{purpose}\n"));
        foreach (BoundaryObservation value in values)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(value.Dimension));
            hash.AppendData([0]);
            hash.AppendData(select(value).AsSpan());
        }
        return hash.GetHashAndReset().ToImmutableArray();
    }

    private static ImmutableArray<byte> BoundaryEvidence(
        string caseId,
        IEnumerable<BoundaryObservation> values)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Encoding.UTF8.GetBytes(
            $"base.logicalIndex.boundaryEvidence.v1\n{caseId}\n"));
        byte[] statusBytes = new byte[sizeof(int)];
        foreach (BoundaryObservation value in values)
        {
            hash.AppendData(Encoding.UTF8.GetBytes(value.Dimension));
            hash.AppendData([0]);
            hash.AppendData(value.CapabilityChecksum.AsSpan());
            BinaryPrimitives.WriteInt32BigEndian(statusBytes, (int)value.Status);
            hash.AppendData(statusBytes);
            hash.AppendData(Encoding.UTF8.GetBytes(value.ErrorCode ?? string.Empty));
            hash.AppendData([0]);
            hash.AppendData(BaseLogicalIndexProviderContract.CaseChecksum(
                new BaseLogicalIndexCertificationCaseResult
                {
                    Id = value.Dimension,
                    Ordinal = BoundaryDimensionIds.IndexOf(value.Dimension),
                    ObservedStatus = value.Status,
                    ObservedErrorCode = value.ErrorCode,
                    Accounting = value.Accounting,
                    BeforeMemberSetChecksum = value.BeforeMemberSetChecksum,
                    AfterMemberSetChecksum = value.AfterMemberSetChecksum,
                    BeforePublicationChecksum = value.BeforePublicationChecksum,
                    AfterPublicationChecksum = value.AfterPublicationChecksum,
                    EvidenceChecksum = new byte[32].ToImmutableArray(),
                }).AsSpan());
        }
        return hash.GetHashAndReset().ToImmutableArray();
    }

    private sealed record BoundaryWorkload(
        string IndexId, ExpectedItem[] Prior, ExpectedItem[] Final);

    private sealed record BoundaryObservation(
        string Dimension,
        ImmutableArray<byte> CapabilityChecksum,
        OperationStatus Status,
        string? ErrorCode,
        BaseLogicalIndexCertificationAccounting Accounting,
        ImmutableArray<byte> BeforeMemberSetChecksum,
        ImmutableArray<byte> AfterMemberSetChecksum,
        ImmutableArray<byte> BeforePublicationChecksum,
        ImmutableArray<byte> AfterPublicationChecksum);

    private static async ValueTask<(OperationStatus Status, string? Error,
        BaseLogicalIndexCertificationSnapshot Before,
        BaseLogicalIndexCertificationSnapshot After,
        ImmutableArray<byte> Evidence)> ExecuteGenerationConflictAsync(
        IBaseLogicalIndexCertificationFixture fixture,
        BaseLogicalIndexCertificationRoot root,
        ServiceProvider provider,
        BaseCollectionSession<BaseLogicalIndexCertificationItem> collection,
        IBaseLogicalIndexCertificationInspection inspection,
        BaseLogicalIndexDefinition unique,
        PausingSelectionItemPolicyEvaluator? pausing,
        CancellationToken cancellationToken)
    {
        (await collection.CreateAsync(Id(1), Item("a", "x", 1), cancellationToken))
            .RequireValue();
        BaseLogicalIndexCertificationSnapshot before = await InspectAsync(
            inspection, collection, unique, cancellationToken).ConfigureAwait(false);
        BaseResult<BaseSelectionMutationResult> result;
        if (fixture.Identity.GenerationConflictStrategy ==
            BaseLogicalIndexGenerationConflictStrategy.OptimisticCapture)
        {
            Require(pausing is not null,
                "base.logicalIndex.certification.generationStrategyInvalid");
            PausingSelectionItemPolicyEvaluator evaluator = pausing!;
            Task<BaseResult<BaseSelectionMutationResult>> pending = PatchPointAsync(
                collection, "a", "x", "point-generation-conflict", cancellationToken).AsTask();
            await evaluator.Captured.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            (await collection.ReplaceAsync(Id(1), Item("a", "z", 1),
                cancellationToken: cancellationToken))
                .RequireValue();
            evaluator.Release.TrySetResult();
            result = await pending.ConfigureAwait(false);
        }
        else
        {
            Require(root.AcquireCompetingWriteOwner is not null,
                "base.logicalIndex.certification.generationStrategyInvalid");
            Func<CancellationToken, ValueTask<IAsyncDisposable>> acquire =
                root.AcquireCompetingWriteOwner!;
            await using (IAsyncDisposable owner = await acquire(
                cancellationToken).ConfigureAwait(false))
            {
                result = await PatchPointAsync(collection, "a", "x",
                    "point-generation-conflict", cancellationToken).ConfigureAwait(false);
            }
            (await collection.ReplaceAsync(Id(1), Item("a", "z", 1),
                cancellationToken: cancellationToken))
                .RequireValue();
        }
        (OperationStatus status, string? error) = Failure(result);
        Require(status == OperationStatus.Conflict
            && string.Equals(error, BaseSelectionErrorCodes.TransactionConflict,
                StringComparison.Ordinal),
            "base.logicalIndex.certification.generationConflictClassificationInvalid");
        error = BaseSchemaErrorCodes.TransactionConflict;
        BaseRecord<BaseLogicalIndexCertificationItem> stored =
            (await collection.GetAsync(Id(1), cancellationToken)).RequireValue();
        Require(stored.Value.Code == "z" && stored.Value.Sequence == 1,
            "base.logicalIndex.certification.generationConflictMutated");
        BaseLogicalIndexCertificationSnapshot after = await InspectAsync(
            inspection, collection, unique, cancellationToken).ConfigureAwait(false);
        return (status, error, before, after,
            Evidence("point-generation-conflict", after.Authority.MemberSetChecksum.AsSpan()));
    }

    private static async ValueTask<BaseCollectionSession<BaseLogicalIndexCertificationItem>>
        InitializeAsync(ServiceProvider provider, string? schemaStoreId,
            CancellationToken cancellationToken)
    {
        OperationResult<BaseApplicationReadiness> initialized = await TryInitializeAsync(
            provider, schemaStoreId, cancellationToken).ConfigureAwait(false);
        Require(initialized.IsSuccess(),
            initialized.Error?.Code ?? "base.logicalIndex.certificationInvalid");
        return provider.GetRequiredService<IBaseSessionFactory>().For(new PrincipalContext
        {
            AuthenticationState = PrincipalAuthenticationState.Admin,
            SubjectKind = AccessSubjectKind.User,
            SubjectId = "base-certification",
        }).Collection(BaseLogicalIndexCertificationItem.Collection);
    }

    private static async ValueTask<OperationResult<BaseApplicationReadiness>> TryInitializeAsync(
        ServiceProvider provider,
        string? schemaStoreId,
        CancellationToken cancellationToken)
    {
        try
        {
            if (schemaStoreId is not null)
            {
                IBaseSchemaManager manager = provider.GetRequiredService<IBaseSchemaManager>();
                OperationResult<BaseSchemaPlan> planned = await manager.PlanAsync(
                    new BaseSchemaPlanRequest { StoreId = schemaStoreId }, cancellationToken)
                    .ConfigureAwait(false);
                Require(planned.IsSuccess() && planned.Value is not null,
                    planned.Error?.Code ?? "base.logicalIndex.certification.schemaPlanInvalid");
                BaseSchemaPlan plan = planned.Value!;
                OperationResult<BaseSchemaApplyResult> applied = await manager.ApplyAsync(
                    new BaseSchemaApplyRequest { ProtectedArtifact = plan.ProtectedArtifact },
                    cancellationToken).ConfigureAwait(false);
                Require(applied.IsSuccess(),
                    applied.Error?.Code ?? "base.logicalIndex.certification.schemaApplyInvalid");
            }
            return await provider.GetRequiredService<IHPDBaseApplication>()
                .InitializeAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (InvalidOperationException exception) when (string.Equals(
            exception.Message, BaseSchemaErrorCodes.CapabilityUnavailable,
            StringComparison.Ordinal))
        {
            return new OperationResult<BaseApplicationReadiness>
            {
                Status = OperationStatus.CapabilityUnavailable,
                Error = new BaseError
                {
                    Code = BaseSchemaErrorCodes.CapabilityUnavailable,
                    Message = "The required logical-index graph exceeded provider capability.",
                    Category = ErrorCategory.Capability,
                },
            };
        }
    }

    private static async ValueTask InsertCanonicalFourAsync(
        BaseCollectionSession<BaseLogicalIndexCertificationItem> collection,
        CancellationToken cancellationToken)
    {
        (await collection.CreateAsync(Id(1), Item("a", "x", 1), cancellationToken)).RequireValue();
        (await collection.CreateAsync(Id(2), Item("a", "y", 2), cancellationToken)).RequireValue();
        (await collection.CreateAsync(Id(3), Item("b", null, 3), cancellationToken)).RequireValue();
        (await collection.CreateAsync(Id(4), Item("b", "x", 4), cancellationToken)).RequireValue();
    }

    private static async ValueTask InsertPairAsync(
        BaseCollectionSession<BaseLogicalIndexCertificationItem> collection,
        CancellationToken cancellationToken)
    {
        (await collection.CreateAsync(Id(1), Item("a", "x", 1), cancellationToken)).RequireValue();
        (await collection.CreateAsync(Id(2), Item("a", "y", 2), cancellationToken)).RequireValue();
    }

    private static ValueTask<BaseResult<BaseSelectionMutationResult>> PatchPointAsync(
        BaseCollectionSession<BaseLogicalIndexCertificationItem> collection,
        string tenant,
        string code,
        string caseId,
        CancellationToken cancellationToken) => collection.Query()
            .Where(PointPredicate(tenant, code))
            .OrderBy(BaseLogicalIndexCertificationItem.Fields.Sequence)
            .ThenByRecordId()
            .Take(4)
            .PatchSelectedAsync(
                collection.GetMergePatchSelectionProfile(
                    BaseLogicalIndexCertificationHost.ProfileIdentity()),
                BaseLogicalIndexCertificationHost.SequencePatch(9),
                BasePreviousStateRequirement.None,
                BaseLogicalIndexCertificationHost.Identity(caseId),
                cancellationToken: cancellationToken);

    private static BasePredicate<BaseLogicalIndexCertificationItem> PointPredicate(
        string tenant, string code) =>
        BasePredicate<BaseLogicalIndexCertificationItem>.And(
            BaseLogicalIndexCertificationItem.Fields.Tenant.Equal(tenant),
            BaseLogicalIndexCertificationItem.Fields.Code.Equal(code));

    private static RecordQuery Query(FilterExpression filter) => new()
    {
        Filter = filter,
        Sort =
        [
            new QuerySort(BaseLogicalIndexCertificationItem.Fields.Sequence.Id),
            new QuerySort("id"),
        ],
        Page = new QueryPage
        {
            Mode = QueryPaginationMode.Offset,
            Offset = 0,
            Limit = 4,
        },
        Count = QueryCountMode.None,
    };

    private static async ValueTask<BaseCapturedAtomicExecution> CaptureAsync(
        ServiceProvider provider,
        BaseCollectionSession<BaseLogicalIndexCertificationItem> collection,
        RecordQuery query,
        CancellationToken cancellationToken)
    {
        (BaseAtomicExecutionRequest request, _) = await CreateCaptureRequestAsync(
            provider, collection, query, cancellationToken).ConfigureAwait(false);
        var probe = new CaptureProbe(request);
        RecordMutationExecutionResult execution = await provider
            .GetRequiredService<IAtomicRecordStore>()
            .ExecuteAtomicAsync(probe, AtomicExecution, cancellationToken).ConfigureAwait(false);
        Require(execution.Outcome == RecordMutationExecutionOutcome.RollbackConfirmed
            && probe.Result?.IsSuccess() == true && probe.Result.Value is not null,
            probe.Result?.Error?.Code ?? "base.logicalIndex.certification.captureInvalid");
        return probe.Result!.Value!;
    }

    private static async ValueTask<(BaseAtomicExecutionRequest Request,
        BaseAtomicMutationExecutionLimits Limits)> CreateCaptureRequestAsync(
        ServiceProvider provider,
        BaseCollectionSession<BaseLogicalIndexCertificationItem> collection,
        RecordQuery applicationQuery,
        CancellationToken cancellationToken)
    {
        IAtomicRecordStore store = provider.GetRequiredService<IAtomicRecordStore>();
        BaseSelectionOperationProfile profile = BaseLogicalIndexCertificationHost.Profile();
        BaseAtomicMutationExecutionLimits limits = BaseAtomicSchemaContract.AttachLimits(
            DefaultBaseSelectionMutationRuntime.CreateExecutionLimits(profile.Limits),
            [collection.Contract.Definition]);
        OperationResult<BaseAtomicMutationAuthorityRequirement> authorityResult = await store
            .CaptureAtomicMutationAuthorityRequirementAsync(
                BaseLogicalIndexCertificationHost.ApplicationId,
                [collection.Contract.Definition], limits, cancellationToken)
            .ConfigureAwait(false);
        Require(authorityResult.IsSuccess() && authorityResult.Value is not null,
            authorityResult.Error?.Code ?? "base.logicalIndex.certification.authorityInvalid");
        BaseAtomicMutationAuthorityRequirement authority = authorityResult.Value!;
        BaseLogicalIndexPointSelection? point = BaseLogicalIndexPointPlanContract.Derive(
            collection.Contract.Definition, applicationQuery.Filter);
        return (new BaseAtomicExecutionRequest
        {
            Kind = BaseAtomicMutationExecutionKind.SelectionMutation,
            Intent = new BaseAtomicMutationIntent
            {
                IntentDigest = Convert.ToHexString(SHA256.HashData(
                    "base.logicalIndex.certificationCapture.v1"u8)),
                Authority = authority,
                Items = [],
            },
            Selection = new BaseSelectionMutationCaptureExtension
            {
                OperationProfileId = profile.Id,
                OperationProfileVersion = profile.Version,
                OperationProfileChecksum = BaseSelectionProfileChecksum.Compute(profile),
                Selection = new BaseAtomicSelectionRequest
                {
                    Collection = collection.Contract.Definition,
                    Query = BaseQueryFieldResolver.ToStoredNames(
                        collection.Contract.Definition, applicationQuery),
                    CanonicalRecordCodecVersion = 1,
                    LogicalIndexPoint = point,
                },
            },
            Schema = BaseAtomicSchemaContract.CaptureRequest(
                authority, [collection.Contract.Definition], limits),
            Limits = limits,
        }, limits);
    }

    private static BaseLogicalIndexCertificationAccounting VerifyAccounting(
        string caseId,
        BaseCollectionSession<BaseLogicalIndexCertificationItem> collection,
        BaseLogicalIndexDefinition unique,
        BaseLogicalIndexDefinition ordered,
        BaseLogicalIndexCertificationSnapshot snapshot)
    {
        (BaseLogicalIndexDefinition index, ExpectedItem[] prior, ExpectedItem[] final,
            bool staged) = AccountingScenario(caseId, unique, ordered);
        BaseLogicalIndexCertificationAccounting expected = IndependentlyEncodeAccounting(
            collection.Contract, index, prior, final, staged);
        BaseLogicalIndexDirectoryAccounting observed = snapshot.Directory.Accounting;
        long comparisonCeiling = ComparisonCeiling(final.Length);
        Require(observed.Records == expected.Records
            && observed.PredicateEvaluations == expected.PredicateEvaluations
            && observed.Keys == expected.Keys
            && observed.KeyBytes == expected.KeyBytes
            && observed.PostingKeys == expected.PostingKeys
            && observed.Postings == expected.Postings
            && observed.ComparatorEntries == expected.ComparatorEntries
            && observed.Comparisons >= 0
            && observed.Comparisons <= comparisonCeiling
            && observed.EvidenceBytes == expected.EvidenceBytes
            && observed.RetainedDirectoryBytes == expected.RetainedDirectoryBytes
            && observed.TransientBytes == expected.TransientBytes,
            $"base.logicalIndex.certification.accountingInvalid:{caseId}:"
            + $"observed={FormatAccounting(observed)}:expected={FormatAccounting(expected)}:"
            + $"comparisonCeiling={comparisonCeiling}");
        return new BaseLogicalIndexCertificationAccounting
        {
            Records = expected.Records,
            PredicateEvaluations = expected.PredicateEvaluations,
            Keys = expected.Keys,
            KeyBytes = expected.KeyBytes,
            PostingKeys = expected.PostingKeys,
            Postings = expected.Postings,
            ComparatorEntries = expected.ComparatorEntries,
            Comparisons = observed.Comparisons,
            EvidenceBytes = expected.EvidenceBytes,
            RetainedDirectoryBytes = expected.RetainedDirectoryBytes,
            TransientBytes = expected.TransientBytes,
        };
    }

    private static (BaseLogicalIndexDefinition Index, ExpectedItem[] Prior,
        ExpectedItem[] Final, bool Staged) AccountingScenario(
        string caseId,
        BaseLogicalIndexDefinition unique,
        BaseLogicalIndexDefinition ordered)
    {
        ExpectedItem One(string tenant = "a", string? code = "x", long sequence = 1) =>
            new(1, Item(tenant, code, sequence));
        ExpectedItem[] canonical =
        [
            One(), new(2, Item("a", "y", 2)), new(3, Item("b", null, 3)),
            new(4, Item("b", "x", 4)),
        ];
        ExpectedItem[] three = canonical[..3];
        ExpectedItem[] pair = canonical[..2];
        return caseId switch
        {
            "empty-directory" => (unique, [], [], false),
            "membership" or "equality-key" => (unique, three, canonical, true),
            "comparator-order" => (ordered, three, canonical, true),
            "insert" => (unique, [], [One()], true),
            "update-key-move" => (unique, [One()], [One(code: "z")], true),
            "delete" => (unique, [One()], [], true),
            "unique-final-overlay" => (unique, pair,
                [One(code: "y"), new(2, Item("a", "x", 2))], true),
            "duplicate-conflict" => (unique, [One()], pair, true),
            "point-hit" => (unique, three,
                [One(sequence: 9), .. canonical[1..]], true),
            "point-miss" => (unique, three, canonical, true),
            "point-policy" => (unique,
                [One()],
                [One(sequence: 9), new(4, Item("b", "x", 4))], true),
            "point-generation-conflict" => (unique, [One()], [One(code: "z")], true),
            "scan-fallback" => (unique, three, canonical, true),
            "maximum" or "maximum-plus-one" => (unique,
                [
                    new(1, Item("t1", "v0001", 1)),
                    new(2, Item("t2", "v0002", 2)),
                    new(3, Item("t3", "v0003", 3)),
                ],
                [
                    new(1, Item("t1", "v0001", 1)),
                    new(2, Item("t2", "v0002", 2)),
                    new(3, Item("t3", "v0003", 3)),
                    new(4, Item("t4", "v0004", 4)),
                ], true),
            "hostile-member-set" or "hostile-result-ownership" =>
                (unique, [], [One()], true),
            _ => throw new InvalidOperationException(
                "base.logicalIndex.certification.accountingScenarioUnknown"),
        };
    }

    private static BaseLogicalIndexCertificationAccounting IndependentlyEncodeAccounting(
        BaseCollection<BaseLogicalIndexCertificationItem> collection,
        BaseLogicalIndexDefinition index,
        ExpectedItem[] prior,
        ExpectedItem[] final,
        bool staged)
    {
        IndependentDirectory finalDirectory = IndependentlyEncodeDirectory(collection, index, final);
        IndependentDirectory priorDirectory = IndependentlyEncodeDirectory(collection, index, prior);
        long transient = staged
            ? checked(priorDirectory.RetainedBytes + 32L + finalDirectory.RetainedBytes)
            : finalDirectory.RetainedBytes;
        return new BaseLogicalIndexCertificationAccounting
        {
            Records = final.Length,
            PredicateEvaluations = final.Length,
            Keys = finalDirectory.Members,
            KeyBytes = finalDirectory.KeyBytes,
            PostingKeys = finalDirectory.PostingKeys,
            Postings = finalDirectory.Members,
            ComparatorEntries = finalDirectory.Members,
            Comparisons = 0,
            EvidenceBytes = 0,
            RetainedDirectoryBytes = finalDirectory.RetainedBytes,
            TransientBytes = transient,
        };
    }

    private static IndependentDirectory IndependentlyEncodeDirectory(
        BaseCollection<BaseLogicalIndexCertificationItem> collection,
        BaseLogicalIndexDefinition index,
        IEnumerable<ExpectedItem> records)
    {
        var postings = new SortedDictionary<byte[], List<string>>(
            UnsignedBytesComparer.Instance);
        long keyBytes = 0;
        long comparatorBytes = sizeof(int);
        long members = 0;
        foreach (ExpectedItem expected in records.OrderBy(static value => value.Ordinal))
        {
            JsonElement encoded = JsonSerializer.SerializeToElement(expected.Value,
                BaseLogicalIndexCertificationJsonContext.Default
                    .BaseLogicalIndexCertificationItem);
            var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
            foreach (JsonProperty property in encoded.EnumerateObject())
                fields.Add(property.Name, property.Value.Clone());
            RecordPayload payload = new()
            {
                Kind = RecordPayloadKind.FieldMap,
                Fields = fields,
            };
            if (!BaseLogicalIndexEvaluator.Includes(collection.Definition, index, payload))
                continue;
            byte[] key = BaseLogicalIndexEvaluator.Key(collection.Definition, index, payload);
            byte[] comparator = IndependentlyEncodeComparator(
                collection.Definition, index, Id(expected.Ordinal), payload, out long scalarBytes);
            keyBytes = checked(keyBytes + sizeof(int) + key.LongLength + scalarBytes);
            comparatorBytes = checked(comparatorBytes + sizeof(int) + comparator.LongLength + 32L);
            if (!postings.TryGetValue(key, out List<string>? ids))
                postings.Add(key.ToArray(), ids = []);
            ids.Add(Id(expected.Ordinal).Value);
            members++;
        }
        long postingBytes = sizeof(int);
        foreach ((byte[] key, List<string> ids) in postings)
        {
            postingBytes = checked(postingBytes + sizeof(int) + key.LongLength
                + sizeof(int) + 32L);
            foreach (string id in ids.Order(StringComparer.Ordinal))
                postingBytes = checked(postingBytes + sizeof(int)
                    + BaseStrictUtf8.Encode(id).LongLength);
        }
        return new IndependentDirectory(
            members, keyBytes, postings.Count, checked(postingBytes + comparatorBytes));
    }

    private static byte[] IndependentlyEncodeComparator(
        CollectionDefinition collection,
        BaseLogicalIndexDefinition index,
        RecordId id,
        RecordPayload payload,
        out long scalarBytes)
    {
        FieldDefinition[] fields = (collection.Fields ?? [])
            .OrderBy(static value => value.Id, StringComparer.Ordinal).ToArray();
        var writer = new ArrayBufferWriter<byte>();
        WriteInt32(writer, index.Parts.Length);
        scalarBytes = 0;
        foreach (BaseLogicalIndexPart part in index.Parts)
        {
            FieldDefinition field = fields[part.FieldOrdinal];
            JsonElement value = default;
            bool present = payload.Kind == RecordPayloadKind.Json
                ? payload.Json.ValueKind == JsonValueKind.Object
                    && payload.Json.TryGetProperty(field.WireName, out value)
                : payload.Fields?.TryGetValue(field.WireName, out value) == true;
            byte state = !present ? (byte)0
                : value.ValueKind == JsonValueKind.Null ? (byte)1 : (byte)2;
            byte[] canonical = state == 2
                ? BaseScalarCanonical.Encode(field.ScalarKind
                    ?? throw new InvalidOperationException(
                        "base.logicalIndex.certification.scalarKindMissing"), value)
                : [];
            scalarBytes = checked(scalarBytes + canonical.LongLength);
            WriteInt32(writer, part.FieldOrdinal);
            writer.Write([state]);
            WriteBytes(writer, BaseStrictUtf8.Encode((field.ScalarCodec
                ?? throw new InvalidOperationException(
                    "base.logicalIndex.certification.scalarCodecMissing")).Id.ToString()));
            WriteBytes(writer, canonical);
        }
        WriteBytes(writer, BaseStrictUtf8.Encode(id.Value));
        return writer.WrittenSpan.ToArray();
    }

    private static long ComparisonCeiling(long retainedEntries)
    {
        if (retainedEntries <= 1) return retainedEntries;
        long power = 1;
        long logarithm = 0;
        while (power < retainedEntries)
        {
            power = checked(power * 2);
            logarithm++;
        }
        return checked(retainedEntries * logarithm + retainedEntries);
    }

    private static string FormatAccounting(BaseLogicalIndexDirectoryAccounting value) =>
        $"{value.Records},{value.PredicateEvaluations},{value.Keys},{value.KeyBytes},"
        + $"{value.PostingKeys},{value.Postings},{value.ComparatorEntries},{value.Comparisons},"
        + $"{value.EvidenceBytes},{value.RetainedDirectoryBytes},{value.TransientBytes}";

    private static string FormatAccounting(BaseLogicalIndexCertificationAccounting value) =>
        $"{value.Records},{value.PredicateEvaluations},{value.Keys},{value.KeyBytes},"
        + $"{value.PostingKeys},{value.Postings},{value.ComparatorEntries},{value.Comparisons},"
        + $"{value.EvidenceBytes},{value.RetainedDirectoryBytes},{value.TransientBytes}";

    private static void WriteBytes(ArrayBufferWriter<byte> writer, ReadOnlySpan<byte> value)
    {
        WriteInt32(writer, value.Length);
        writer.Write(value);
    }

    private static void WriteInt32(ArrayBufferWriter<byte> writer, int value)
    {
        Span<byte> target = writer.GetSpan(sizeof(int));
        BinaryPrimitives.WriteInt32BigEndian(target, value);
        writer.Advance(sizeof(int));
    }

    private sealed record ExpectedItem(int Ordinal, BaseLogicalIndexCertificationItem Value);

    private sealed record IndependentDirectory(
        long Members, long KeyBytes, long PostingKeys, long RetainedBytes);

    private sealed class UnsignedBytesComparer : IComparer<byte[]>
    {
        internal static UnsignedBytesComparer Instance { get; } = new();

        public int Compare(byte[]? left, byte[]? right) =>
            left.AsSpan().SequenceCompareTo(right);
    }

    private static ImmutableArray<byte> Evidence(
        string caseId,
        ReadOnlySpan<byte> first,
        ReadOnlySpan<byte> second = default,
        BaseLogicalIndexCertificationAccounting? accounting = null)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData("base.logicalIndex.certificationObservedEvidence.v1\0"u8);
        hash.AppendData(Encoding.UTF8.GetBytes(caseId));
        hash.AppendData(first);
        hash.AppendData(second);
        if (accounting is not null)
            hash.AppendData(BaseLogicalIndexProviderContract.CaseChecksum(
                new BaseLogicalIndexCertificationCaseResult
                {
                    Id = caseId,
                    Ordinal = BaseLogicalIndexProviderContract.CaseIds.IndexOf(caseId),
                    ObservedStatus = BaseLogicalIndexProviderContract.ExpectedOutcome(caseId).Status,
                    ObservedErrorCode = BaseLogicalIndexProviderContract.ExpectedOutcome(caseId).ErrorCode,
                    Accounting = accounting,
                    BeforeMemberSetChecksum = new byte[32].ToImmutableArray(),
                    AfterMemberSetChecksum = new byte[32].ToImmutableArray(),
                    BeforePublicationChecksum = new byte[32].ToImmutableArray(),
                    AfterPublicationChecksum = new byte[32].ToImmutableArray(),
                    EvidenceChecksum = new byte[32].ToImmutableArray(),
                }).AsSpan());
        return hash.GetHashAndReset().ToImmutableArray();
    }

    private static BaseLogicalIndexCertificationItem Item(
        string tenant, string? code, long sequence) => new()
    {
        Tenant = tenant,
        Code = code,
        Sequence = sequence,
    };

    private static RecordId Id(int ordinal) => RecordId.Create(
        $"00000000-0000-0000-0000-{ordinal:000000000000}");

    private static BaseLogicalIndexPointSelection Point(
        BaseCollectionSession<BaseLogicalIndexCertificationItem> collection,
        string tenant,
        string code) => BaseLogicalIndexPointPlanContract.Derive(
            collection.Contract.Definition, PointPredicate(tenant, code).Expression)
            ?? throw new InvalidOperationException(
                "base.logicalIndex.certification.pointPlanInvalid");

    private static string FindPosting(
        BaseLogicalIndexCertificationSnapshot snapshot,
        BaseLogicalIndexPointSelection point) => snapshot.Directory.EqualityPostings.Single(posting =>
            posting.EqualityKey.AsSpan().SequenceEqual(point.EqualityKey.AsSpan())).RecordIds.Single();

    private static bool HasPosting(
        BaseLogicalIndexCertificationSnapshot snapshot,
        BaseLogicalIndexPointSelection point) => snapshot.Directory.EqualityPostings.Any(posting =>
            posting.EqualityKey.AsSpan().SequenceEqual(point.EqualityKey.AsSpan()));

    private static bool SameAuthority(
        BaseLogicalIndexCertificationSnapshot left,
        BaseLogicalIndexCertificationSnapshot right) =>
        left.Authority.MemberSetChecksum.AsSpan().SequenceEqual(
            right.Authority.MemberSetChecksum.AsSpan())
        && left.Authority.DirectoryPublicationChecksum.AsSpan().SequenceEqual(
            right.Authority.DirectoryPublicationChecksum.AsSpan())
        && left.Directory.CanonicalEncoding.AsSpan().SequenceEqual(
            right.Directory.CanonicalEncoding.AsSpan());

    private static bool PointEvidenceMatches(
        BaseLogicalIndexSelectionEvidence evidence,
        BaseLogicalIndexDefinition expected) =>
        evidence.IndexId == expected.Id
        && evidence.IndexVersion == expected.Version
        && evidence.IndexChecksum == expected.Checksum
        && evidence.AccessShape == BaseIndexAccessShape.LogicalIndexPoint
        && evidence.ReadInterval.CanonicalLowerBound.AsSpan().SequenceEqual(
            evidence.ReadInterval.CanonicalUpperBound.AsSpan())
        && evidence.ReadInterval.LowerInclusive
        && evidence.ReadInterval.UpperInclusive;

    private static ValueTask<BaseLogicalIndexCertificationSnapshot> InspectAsync(
        IBaseLogicalIndexCertificationInspection inspection,
        BaseCollectionSession<BaseLogicalIndexCertificationItem> collection,
        BaseLogicalIndexDefinition index,
        CancellationToken cancellationToken) => inspection.InspectLogicalIndexForCertificationAsync(
            collection.Contract.Id, index.Checksum, cancellationToken);

    private static (OperationStatus Status, string? Error) Failure<T>(BaseResult<T> result)
    {
        if (result is not BaseFailure<T> failure)
            throw new InvalidOperationException(
                "base.logicalIndex.certification.expectedFailureMissing");
        return (failure.Status, failure.Error.Code);
    }

    private static void ValidateIdentity(BaseLogicalIndexCertificationFixtureIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        Require(!string.IsNullOrWhiteSpace(identity.ProviderId)
            && identity.ProviderVersion > 0
            && !string.IsNullOrWhiteSpace(identity.StoreProviderKind)
            && identity.NativeDependencyReceipts.SequenceEqual(
                identity.NativeDependencyReceipts.Order(StringComparer.Ordinal),
                StringComparer.Ordinal)
            && identity.NativeDependencyReceipts.Distinct(StringComparer.Ordinal).Count()
                == identity.NativeDependencyReceipts.Length,
            "base.logicalIndex.certification.fixtureIdentityInvalid");
    }

    private static BaseLogicalIndexProviderCapability CertificationGraphCapability() =>
        BaseLogicalIndexProviderContract.SealCapability(
            BaseLogicalIndexProviderContract.BoundedCertificationCapability() with
            {
                MaximumIndexesPerCollection = 3,
                MaximumPostingsPerStore = 12,
                Checksum = [],
            });

    private static void Require(bool condition, string code)
    {
        if (!condition)
            throw new InvalidOperationException(code);
    }

    private sealed class CaptureProbe(BaseAtomicExecutionRequest request) : IAtomicMutationProcessor
    {
        internal OperationResult<BaseCapturedAtomicExecution>? Result { get; private set; }

        public async ValueTask<AtomicMutationProcessingResult> ProcessAsync(
            IAtomicRecordSession session,
            CancellationToken cancellationToken = default)
        {
            Result = await session.CaptureAtomicExecutionAsync(request, cancellationToken)
                .ConfigureAwait(false);
            return Failure(Result.Error);
        }
    }

    private sealed class HostileResultOwnershipProbe(
        BaseAtomicExecutionRequest request,
        BaseAtomicMutationExecutionLimits limits) : IAtomicMutationProcessor
    {
        internal OperationResult<BasePreparedAtomicExecution>? PrepareResult { get; private set; }

        public async ValueTask<AtomicMutationProcessingResult> ProcessAsync(
            IAtomicRecordSession session,
            CancellationToken cancellationToken = default)
        {
            OperationResult<BaseCapturedAtomicExecution> result = await session
                .CaptureAtomicExecutionAsync(request, cancellationToken).ConfigureAwait(false);
            if (!result.IsSuccess() || result.Value?.Selection?.LogicalIndexEvidence is null)
                return Failure(result.Error);
            BaseCapturedAtomicExecution captured = result.Value;
            byte[] retained = System.Runtime.InteropServices.ImmutableCollectionsMarshal.AsArray(
                captured.Selection.LogicalIndexEvidence.MemberSetChecksum)
                ?? throw new InvalidOperationException(
                    "base.logicalIndex.certification.hostileOwnershipInvalid");
            retained[0] ^= 0x01;
            ImmutableArray<BaseAtomicMutationPlanItem> items = captured.Selection.Records
                .Select((owned, index) => PlanItem(
                    request, owned.MaterializeOwned(), index)).ToImmutableArray();
            PrepareResult = await session.PrepareAtomicExecutionAsync(captured,
                new BaseFinalizedAtomicExecutionPlan
                {
                    Kind = BaseAtomicMutationExecutionKind.SelectionMutation,
                    PlanDigest = "base.logicalIndex.hostileResultOwnership.v1",
                    IntentDigest = request.Intent.IntentDigest,
                    CaptureDigest = captured.CaptureDigest,
                    PolicyAuthorityDigest = BaseAtomicPolicyAuthorityDigest.Create(new byte[32]),
                    Authority = request.Intent.Authority,
                    Items = items,
                    SubjectValidations = [],
                    Limits = limits,
                }, cancellationToken).ConfigureAwait(false);
            return Failure(PrepareResult.Error);
        }
    }

    private static BaseAtomicMutationPlanItem PlanItem(
        BaseAtomicExecutionRequest request,
        RecordEnvelope record,
        int ordinal) => new()
    {
        Ordinal = ordinal,
        ItemId = $"selection:{ordinal}",
        EventId = $"base-logical-index-hostile-result-{ordinal}",
        Collection = request.Selection!.Selection.Collection,
        Kind = BaseCommittedRecordMutationKind.Patch,
        RequestedKind = BaseRecordMutationKind.Patch,
        RecordId = record.Id,
        ProposedPayload = record.Payload,
        RemovedFieldIds = [],
        Current = record,
        ChangedFields = [],
        Operation = new OperationContext
        {
            ApplicationId = request.Intent.Authority.ApplicationId,
            Operation = BaseOperationKind.SelectionMutation,
            CollectionId = request.Selection.Selection.Collection.Id,
            RecordId = record.Id.Value,
            Now = DateTimeOffset.UnixEpoch,
        },
    };

    private static AtomicMutationProcessingResult Failure(BaseError? error) => new(
        AtomicMutationProcessingOutcome.Failed,
        [],
        error ?? new BaseError
        {
            Code = "base.logicalIndex.certificationProbeComplete",
            Message = "The logical-index certification probe completed.",
            Category = ErrorCategory.Validation,
        });

    private sealed class PausingSelectionItemPolicyEvaluator : IPolicyEvaluator
    {
        internal TaskCompletionSource Captured { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask<PolicyDecision> EvaluateAsync(
            PolicyEvaluationRequest request,
            CancellationToken cancellationToken = default)
        {
            if (request.Operation.Operation == BaseOperationKind.SelectionMutation
                && request.Resource.Kind == PolicyResourceKind.UpdatePayload
                && request.Resource.ExistingRecord is not null)
            {
                Captured.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            return new PolicyDecision
            {
                Effect = PolicyEffect.Allow,
                Outcome = PolicyOutcome.Allowed,
                Audit = new PolicyAuditInfo
                {
                    MatchedGrantIds = [BaseLogicalIndexCertificationHost.GrantId],
                },
            };
        }
    }
}
