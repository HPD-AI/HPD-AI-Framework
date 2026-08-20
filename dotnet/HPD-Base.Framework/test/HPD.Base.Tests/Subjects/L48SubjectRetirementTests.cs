using System.Collections.Immutable;
using System.Security.Cryptography;

namespace HPD.Base.Tests.Subjects;

#pragma warning disable HPDBASE0461

public sealed class L48SubjectRetirementTests
{
    [Fact]
    public void Required_participation_requires_exact_bilateral_graph_agreement()
    {
        (BaseSubjectLifecycleRegistry lifecycle, BaseSubjectRetirementConsumerDefinition consumer, BaseSubjectRetirementPolicy policy) = Graph();
        BaseSubjectRetirementRegistry installed = new([consumer], [policy], lifecycle);
        Assert.Single(installed.Consumers);
        Assert.Single(installed.Policies);

        BaseSubjectRetirementPolicy substituted = policy with
        {
            AcceptedConsumers = [policy.AcceptedConsumers[0] with { RetirementProfileVersion = 2 }],
        };
        Assert.Equal(BaseSubjectRetirementErrorCodes.RegistrationConflict,
            Assert.Throws<InvalidOperationException>(() => new BaseSubjectRetirementRegistry([consumer], [substituted], lifecycle)).Message);
    }

    [Fact]
    public void Barrier_checksum_binds_authority_epoch_and_every_acknowledgement()
    {
        BaseSubjectRetirementBarrier barrier = Barrier();
        string initial = BaseSubjectRetirementRegistry.BarrierChecksum(barrier, []);
        string acknowledged = BaseSubjectRetirementRegistry.BarrierChecksum(barrier,
            [BaseSubjectRetirementRegistry.AcknowledgementChecksumInput("consumer", 1, Hex('a'), 2, BaseSubjectAcknowledgementDisposition.Completed, 7)]);
        string restored = BaseSubjectRetirementRegistry.BarrierChecksum(barrier with
        {
            AuthorityEpoch = new BaseSubjectAuthorityEpoch(Enumerable.Repeat((byte)2, 16).ToArray()),
        }, []);
        Assert.NotEqual(initial, acknowledged);
        Assert.NotEqual(initial, restored);
    }

    [Fact]
    public void Publication_union_rejects_missing_additional_and_subject_bearing_restore_payloads()
    {
        BaseSubjectRetirementPublicationFact restore = new()
        {
            Position = new BaseSubjectRetirementPosition(1), Kind = BaseSubjectRetirementPublicationKind.RestoreTransformed,
            Restore = new BaseSubjectRetirementRestorePublication
            {
                ContractId = "example.subject", ContractVersion = 1, RestoreEpoch = 2,
                PreviousControlGeneration = 4, PublishedControlGeneration = 5,
                TransformedBarrierCount = 1, TransformedAcknowledgementCount = 1,
                TransformationChecksum = Hex('b'),
            },
        };
        BaseSubjectRetirementRegistry.ValidatePublication(new BaseSubjectRetirementPublicationRow { Scope = null, Fact = restore });
        Assert.Throws<InvalidDataException>(() => BaseSubjectRetirementRegistry.ValidatePublication(new BaseSubjectRetirementPublicationRow
        {
            Scope = new BaseProtectedSubjectScope { Kind = BaseSubjectScopeKind.Tenant, IndexDigest = new byte[32], ProtectedCanonicalValue = [1] },
            Fact = restore,
        }));
        Assert.Throws<InvalidDataException>(() => BaseSubjectRetirementRegistry.ValidatePublication(new BaseSubjectRetirementPublicationRow
        {
            Scope = null, Fact = restore with { Purged = Purged() },
        }));
    }

    [Fact]
    public void Shared_maintenance_receipt_is_the_only_permitted_dual_payload_shape()
    {
        BaseSubjectLifecycleMaintenanceResult lifecycle = new()
        {
            Kind = BaseSubjectLifecycleMaintenanceKind.RotateScopeProtection, ExaminedCount = 2, ChangedCount = 2,
            CanonicalBytes = 8, RollingChecksum = Hex('c'), DeliveryEpoch = 2, ProjectionGeneration = 2, Duplicate = false,
        };
        BaseSubjectRetirementMaintenanceResult retirement = new()
        {
            Kind = BaseSubjectRetirementMaintenanceKind.RotateScopeProtection,
            Outcome = BaseSubjectRetirementMutationOutcome.Applied, ExaminedCount = 1, ChangedCount = 1,
            CanonicalBytes = 8, RollingChecksum = Hex('c'), PublishedBarrierControlGeneration = 2,
        };
        BaseAtomicReceiptResult result = new()
        {
            Kind = BaseAtomicReceiptResultKind.SubjectLifecycleMaintenance, Mutations = [], SubjectLifecycleMaintenance = lifecycle,
            SubjectRetirement = new BaseSubjectRetirementReceiptResult { Operation = BaseSubjectRetirementReceiptOperation.Maintenance, Maintenance = retirement },
        };
        BaseAtomicReceiptResult owned = BaseAtomicReceiptWire.From(result).Materialize();
        Assert.NotNull(owned.SubjectRetirement?.Maintenance);
        Assert.Throws<InvalidOperationException>(() => BaseAtomicReceiptWire.From(result with
        {
            SubjectRetirement = new BaseSubjectRetirementReceiptResult
            {
                Operation = BaseSubjectRetirementReceiptOperation.Timeout,
                Timeout = new BaseSubjectRetirementTimeoutResult { Outcome = BaseSubjectRetirementMutationOutcome.Applied, State = BaseSubjectRetirementBarrierState.TimedOut, Generation = 2, BarrierChecksum = Hex('d') },
            },
        }));
    }

    private static (BaseSubjectLifecycleRegistry Lifecycle, BaseSubjectRetirementConsumerDefinition Consumer, BaseSubjectRetirementPolicy Policy) Graph()
    {
        BaseGeneratedSubjectRegistration subject = BaseGeneratedSubjects.Register<object>(new BaseExportedSubjectDefinition
        {
            Id = "example.subject", Version = 1, OwningModuleId = "example.exporter", Scope = BaseSubjectScopeKind.Tenant,
            SubjectIdKind = BaseSubjectIdKind.OrdinalString, MaximumSubjectIdUtf8Bytes = 128,
            TombstoneFieldId="tombstoned",SupportsCoordinatedRetirement=true,Audiences=[HPDBaseEndpointAudience.Application],
            ValidationPlan = new BaseSubjectValidationPlanDefinition { Id="example.subject.validation",Version=1,ContractId = "example.subject", ContractVersion = 1, ContractChecksum = Hex('1'), PrivateCollectionId = "private.subjects", SubjectId=BaseSubjectIdBinding.RecordId,Active=new(){Kind=BaseSubjectActiveBindingKind.RequiredBooleanField,FieldId="active",ActiveValue=true},Scope=new(){Kind=BaseSubjectScopeBindingKind.RequiredTenantField,FieldId="tenant"},Access=BaseSubjectValidationAccessShape.ContractAndSubjectPrimaryKeys,Limits=BaseSubjectValidationLimits.Default },
            AcquisitionGrantId = "subject.acquire", ValidationGrantId = "subject.validate", AdministrationGrantId = "subject.admin",
        });
        BaseSubjectLifecycleConsumerDefinition lifecycleDefinition = new()
        {
            Id = "consumer", Version = 1, OwningModuleId = "example.consumer", Audience = BaseSubjectLifecycleConsumerAudience.Service,
            ContractId = "example.subject", ContractVersion = 1, ObservedStates = [BaseSubjectLifecycleState.Tombstoned], DeliveryGrantId = "consumer.read",
            Limits = new BaseSubjectLifecycleConsumerLimits { MaximumFactsPerPage = 16, MaximumResultBytes = 65_536, MaximumCheckpointLag = TimeSpan.FromDays(1), ReadTimeout = TimeSpan.FromSeconds(1) },
        };
        var lifecycle = new BaseSubjectLifecycleRegistry([lifecycleDefinition], new BaseSubjectContractRegistry([subject]));
        string lifecycleChecksum = lifecycle.All.Single().Checksum;
        BaseSubjectRetirementConsumerDefinition consumer = new()
        {
            ConsumerId = "consumer", ConsumerVersion = 1, OwningModuleId = "example.consumer", Audience = BaseSubjectLifecycleConsumerAudience.Service,
            LifecycleConsumerChecksum = lifecycleChecksum, RetirementProfileId = "consumer.retirement", RetirementProfileVersion = 1,
            RetirementProfileChecksum = Hex('2'), Participation = BaseSubjectRetirementParticipation.RequiredBeforePurge,
            AcknowledgementGrantId = "consumer.ack", Limits = new BaseSubjectRetirementConsumerLimits { MaximumAcknowledgementsPerCommit = 16, MaximumAcknowledgementRequestBytes = 65_536, MaximumReceiptBytes = 65_536, AcknowledgementTimeout = TimeSpan.FromSeconds(1), ReceiptResolutionTimeout = TimeSpan.FromSeconds(1) },
        };
        string consumerChecksum = BaseSubjectRetirementRegistry.ConsumerChecksum(BaseSubjectRetirementRegistry.Normalize(consumer));
        BaseAcceptedRetirementConsumer accepted = new()
        {
            ConsumerId = consumer.ConsumerId, ConsumerVersion = consumer.ConsumerVersion, OwningModuleId = consumer.OwningModuleId, Audience = consumer.Audience,
            LifecycleConsumerChecksum = consumer.LifecycleConsumerChecksum, RetirementProfileId = consumer.RetirementProfileId, RetirementProfileVersion = consumer.RetirementProfileVersion,
            RetirementProfileChecksum = consumer.RetirementProfileChecksum, Participation = consumer.Participation, AcknowledgementGrantId = consumer.AcknowledgementGrantId,
            Limits = consumer.Limits, RetirementConsumerChecksum = consumerChecksum,
        };
        BaseSubjectRetirementPolicy policy = new()
        {
            ContractId = "example.subject", ContractVersion = 1, AcceptedConsumers = [accepted], CoordinationWindow = TimeSpan.FromHours(1),
            TimeoutBehavior = BaseSubjectRetirementTimeoutBehavior.Quarantine, PurgeRetention = new BaseSubjectPurgeRetentionPolicy { MinimumTombstoneAge = TimeSpan.Zero }, PolicyChecksum = Hex('0'),
        };
        policy = policy with { PolicyChecksum = BaseSubjectRetirementRegistry.PolicyChecksum(policy with { PolicyChecksum = string.Empty }) };
        return (lifecycle, consumer, policy);
    }

    private static BaseSubjectRetirementBarrier Barrier() => new()
    {
        ContractId = "example.subject", ContractVersion = 1, SubjectId = BaseSubjectId.Create("subject-1", BaseSubjectIdKind.OrdinalString),
        AuthorityEpoch = new BaseSubjectAuthorityEpoch(Enumerable.Repeat((byte)1, 16).ToArray()), Incarnation = new BaseSubjectIncarnation(Enumerable.Repeat((byte)1, 24).ToArray()),
        TombstoneSequence = 2, RequiredConsumerSetChecksum = Hex('e'), CreatedAtUtc = DateTimeOffset.UnixEpoch, DeadlineUtc = DateTimeOffset.UnixEpoch.AddHours(1),
        State = BaseSubjectRetirementBarrierState.Pending, Generation = 1, BarrierChecksum = Hex('f'),
    };

    private static BaseSubjectPurgedPublication Purged() => new()
    {
        ContractId = "example.subject", ContractVersion = 1, SubjectId = BaseSubjectId.Create("subject-1", BaseSubjectIdKind.OrdinalString),
        AuthorityEpoch = new BaseSubjectAuthorityEpoch(Enumerable.Repeat((byte)1,16).ToArray()), Incarnation = new BaseSubjectIncarnation(Enumerable.Repeat((byte)1,24).ToArray()), TombstoneSequence = 2,
        FinalBarrierGeneration = 1, FinalBarrierChecksum = Hex('a'), TerminalReceiptChecksum = Hex('b'), RetiredLifecyclePosition = new BaseMutationJournalPosition(1),
    };

    private static string Hex(char value) => new(value, 64);
}
