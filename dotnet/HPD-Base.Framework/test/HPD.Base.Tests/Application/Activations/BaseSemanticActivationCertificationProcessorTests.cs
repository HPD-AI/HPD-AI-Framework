using System.Collections.Immutable;
using System.Security.Cryptography;
using HPD.Base.Testing;

namespace HPD.Base.Tests.Application.Activations;

public sealed class BaseSemanticActivationCertificationProcessorTests
{
    [Fact]
    public async Task InMemory_executes_complete_semantic_activation_provider_matrix()
    {
        BaseSemanticActivationCertificationReport report = await
            BaseSemanticActivationProviderCertification.RunAsync(
                new BaseInMemorySemanticActivationCertificationFixtureFactory(), TimeSpan.FromSeconds(5));

        report.Passed.Should().BeTrue(string.Join("; ", report.Cases
            .Where(static item => item.Status != OperationStatus.Ok)
            .Select(static item => $"{item.Id}:{item.ObservedStatus}:{item.ObservedErrorCode}")));
        BaseSemanticActivationCertificationContract.ValidateReport(report).Should().BeTrue();
        BaseSemanticActivationCertificationReport frozen = BaseSemanticActivationBuiltInCertification
            .LoadFrozenExecutedReport(report.Subject, BaseSemanticActivationCapabilityContract.BuiltIn(durable: false));
        report.Should().BeEquivalentTo(frozen, options => options.WithStrictOrdering());
    }

    [Fact]
    public async Task Different_parent_processors_commit_real_distinct_outer_receipts_for_one_semantic_activation()
    {
        byte[] definition = SHA256.HashData("certification-semantic-definition"u8);
        var store = new InMemoryRecordStore(new HPDBaseInMemoryStoreOptions
        {
            StoreId = "certification-store", SemanticActivationApplicationId = "certification-application",
            SemanticActivationOwnerGeneration = 1, SemanticActivationDefinitionSetChecksum = definition,
        });
        BaseAtomicMutationExecutionLimits limits =
            DefaultBaseModuleMutationRuntime.ResolveExecutionLimits(BaseModuleMutationPlatform.MaximumLimits);
        BaseAtomicMutationAuthorityRequirement authority = (await store.CaptureAtomicMutationAuthorityRequirementAsync(
            "certification-application", [], limits)).Value!;
        var left = new BaseSemanticActivationCertificationProcessor(authority, limits, "certification-store", "parent-left");
        var right = new BaseSemanticActivationCertificationProcessor(authority, limits, "certification-store", "parent-right");

        RecordMutationExecutionResult first = await store.ExecuteAtomicAsync(left, Request("left"));
        RecordMutationExecutionResult second = await store.ExecuteAtomicAsync(right, Request("right"));

        first.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
        second.Outcome.Should().Be(RecordMutationExecutionOutcome.Committed);
        first.Processing!.Receipt.ModuleMutation!.SemanticActivation!.EnsureDisposition
            .Should().Be(BaseSemanticActivationEnsureDisposition.Created);
        second.Processing!.Receipt.ModuleMutation!.SemanticActivation!.EnsureDisposition
            .Should().Be(BaseSemanticActivationEnsureDisposition.Existing);
        left.Provisional!.ActivationId.Should().Be(right.Provisional!.ActivationId);
        left.ParentActivationAuthorityChecksum.Should().NotEqual(right.ParentActivationAuthorityChecksum);
        left.SemanticIntentChecksum.Should().Equal(right.SemanticIntentChecksum);
        first.ReceiptAuthority!.ReceiptChecksum.Should().NotEqual(second.ReceiptAuthority!.ReceiptChecksum);
    }

    [Fact]
    public void Certification_case_checksum_binds_every_structured_observation_field()
    {
        ImmutableArray<byte> bytes = SHA256.HashData("observation"u8).ToImmutableArray();
        var value = new BaseSemanticActivationCertificationObservation
        {
            Sequence = 1, Evidence = bytes, LiveSlots = 1, RetiredSlots = 2, AbsenceMarkers = 3,
            Activations = 4, Receipts = 5, ActiveWork = 6, QuarantinedWork = 7, ReleasedWork = 8,
            RejectedLateCompletions = 9, ExactLimitAccepted = true, MaxPlusOneRejected = true,
            RecoveryFloorVerified = true, ReceiptResolved = true,
            AuthorityBeforeChecksum = bytes, AuthorityAfterChecksum = bytes,
        };
        ImmutableArray<byte> baseline = Evidence(value);
        BaseSemanticActivationCertificationObservation[] substitutions =
        [
            value with { Sequence = 2 }, value with { LiveSlots = 2 }, value with { RetiredSlots = 3 },
            value with { AbsenceMarkers = 4 }, value with { Activations = 5 }, value with { Receipts = 6 },
            value with { ActiveWork = 7 }, value with { QuarantinedWork = 8 }, value with { ReleasedWork = 9 },
            value with { RejectedLateCompletions = 10 }, value with { ExactLimitAccepted = false },
            value with { MaxPlusOneRejected = false }, value with { RecoveryFloorVerified = false },
            value with { ReceiptResolved = false }, value with { AuthorityBeforeChecksum = [] },
            value with { AuthorityAfterChecksum = [] }, value with { Evidence = [] },
        ];
        foreach (BaseSemanticActivationCertificationObservation substitution in substitutions)
            Evidence(substitution).Should().NotEqual(baseline);

        static ImmutableArray<byte> Evidence(BaseSemanticActivationCertificationObservation observation) =>
            BaseSemanticActivationCertificationContract.CaseEvidenceChecksum(
                "mutation-vector", 0, BaseSemanticActivationCertificationApplicability.Executed,
                OperationStatus.Ok, null, OperationStatus.Ok, null, RecordMutationExecutionOutcome.Committed,
                BaseAtomicReceiptResolutionDisposition.Found, BaseMutationRequestDisposition.Committed,
                SHA256.HashData("receipt"u8).ToImmutableArray(), observation);
    }

    private static RecordMutationExecutionRequest Request(string id)
    {
        byte[] fingerprint = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes("certification:" + id));
        return new RecordMutationExecutionRequest
        {
            AcquisitionTimeout = TimeSpan.FromSeconds(5), TransactionTimeout = TimeSpan.FromSeconds(5),
            CommitCompletionTimeout = TimeSpan.FromSeconds(5),
            AtomicRequest = new BaseAtomicMutationExecutionRequest
            {
                Identity = BaseMutationRequestIdentity.Create("certification", "semantic.ensure", id,
                    BaseMutationRequestFingerprint.Create(fingerprint)),
                StructuralDigest = fingerprint, ExpiresAt = DateTimeOffset.UtcNow.AddDays(1), MaxReceiptBytes = 1_048_576,
            },
        };
    }
}
