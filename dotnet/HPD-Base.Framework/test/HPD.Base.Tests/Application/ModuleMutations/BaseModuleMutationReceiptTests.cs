using System.Collections.Immutable;
using System.Text.Json;

namespace HPD.Base.Tests.Application.ModuleMutations;

public sealed class BaseModuleMutationReceiptTests
{
    [Fact]
    public void Semantic_maintenance_receipt_round_trips_as_one_closed_l37_variant()
    {
        var initial = new BaseSemanticActivationMaintenanceResult
        {
            Disposition = BaseSemanticActivationMaintenanceDisposition.Completed,
            PreviousAuthorityGeneration = 3, ResultingAuthorityGeneration = 4,
            ExaminedRows = 2, ChangedRows = 2, CanonicalBytes = 128,
            AuthorityChecksum = Enumerable.Repeat((byte)0x31, 32).ToImmutableArray(),
            ResultChecksum = [], CommitObservationChecksum = [], Checkpoint = null,
            ReceiptDisposition = BaseMutationRequestDisposition.Committed,
        };
        ImmutableArray<byte> resultChecksum = BaseSemanticActivationMaintenanceContract.ResultChecksum(
            initial, initial.AuthorityChecksum.AsSpan());
        BaseSemanticActivationMaintenanceResult result = initial with
        {
            ResultChecksum = resultChecksum,
            CommitObservationChecksum = BaseSemanticActivationMaintenanceContract.CommitObservationChecksum(resultChecksum.AsSpan()),
        };
        var receipt = new BaseAtomicReceiptResult
        {
            Kind = BaseAtomicReceiptResultKind.SemanticActivationMaintenance,
            Mutations = [], SemanticActivationMaintenance = result,
        };

        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(BaseAtomicReceiptWire.From(receipt),
            HPDBaseJsonSerializerContext.Default.BaseAtomicReceiptWire);
        BaseAtomicReceiptResult restored = JsonSerializer.Deserialize(bytes,
            HPDBaseJsonSerializerContext.Default.BaseAtomicReceiptWire)!.Materialize();

        restored.Kind.Should().Be(BaseAtomicReceiptResultKind.SemanticActivationMaintenance);
        restored.SemanticActivationMaintenance.Should().BeEquivalentTo(result);
        restored.ModuleMutation.Should().BeNull();
        restored.SubjectLifecycleMaintenance.Should().BeNull();
    }

    [Fact]
    public void Transactional_activation_receipt_has_one_outer_replay_authority()
    {
        BaseAtomicReceiptResult receipt = new()
        {
            Kind = BaseAtomicReceiptResultKind.ActivationTransactionalOperation,
            Mutations = [],
            ActivationTransactionalOperation = new BaseActivationTransactionalReceiptResult
            {
                ActivationId = "activation-1",
                ActivationGeneration = 2,
                TargetKind = "moduleMutation",
                TargetId = "payments.apply",
                TargetVersion = 1,
                TargetChecksum = new string('a', 64),
                Generations = [],
                CanonicalResultBytes = "{\"applied\":true}"u8.ToArray().ToImmutableArray(),
                ActivationControlChecksum = Enumerable.Repeat((byte)7, 32).ToImmutableArray(),
            },
        };

        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
            BaseAtomicReceiptWire.From(receipt), HPDBaseJsonSerializerContext.Default.BaseAtomicReceiptWire);
        BaseAtomicReceiptResult restored = JsonSerializer.Deserialize(
            bytes, HPDBaseJsonSerializerContext.Default.BaseAtomicReceiptWire)!.Materialize();

        restored.Kind.Should().Be(BaseAtomicReceiptResultKind.ActivationTransactionalOperation);
        restored.ModuleMutation.Should().BeNull();
        restored.ActivationTransactionalOperation!.ActivationId.Should().Be("activation-1");
        restored.ActivationTransactionalOperation.CanonicalResultBytes.Should()
            .Equal(receipt.ActivationTransactionalOperation.CanonicalResultBytes);
    }

    [Fact]
    public void Module_receipt_round_trips_exact_result_and_generation_evidence()
    {
        BaseAtomicReceiptResult receipt = new()
        {
            Kind = BaseAtomicReceiptResultKind.ModuleMutation,
            Mutations = [],
            ModuleMutation = new BaseModuleMutationReceiptResult
            {
                OperationId = "payments.apply.v1",
                OperationVersion = 1,
                Disposition = BaseMutationRequestDisposition.Committed,
                Outcome = BaseModuleMutationOutcome.Committed,
                Generations =
                [
                    new BaseModuleCommittedGeneration
                    {
                        CaptureId = "owner-generation",
                        CellId = "payments.owner-generation.v1",
                        CellVersion = 1,
                        Previous = BaseModuleGeneration.Create(41),
                        Resulting = BaseModuleGeneration.Create(42),
                    },
                ],
                CanonicalResultBytes = "{\"applied\":true}"u8.ToArray().ToImmutableArray(),
            },
        };

        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
            BaseAtomicReceiptWire.From(receipt),
            HPDBaseJsonSerializerContext.Default.BaseAtomicReceiptWire);
        BaseAtomicReceiptWire wire = JsonSerializer.Deserialize(
            bytes,
            HPDBaseJsonSerializerContext.Default.BaseAtomicReceiptWire)!;
        BaseAtomicReceiptResult restored = wire.Materialize();

        restored.Kind.Should().Be(BaseAtomicReceiptResultKind.ModuleMutation);
        restored.SelectionMutation.Should().BeNull();
        restored.ModuleMutation!.CanonicalResultBytes.Should().Equal(receipt.ModuleMutation.CanonicalResultBytes);
        BaseModuleCommittedGeneration generation = restored.ModuleMutation.Generations.Should().ContainSingle().Subject;
        generation.Previous!.ToCanonicalString().Should().Be("41");
        generation.Resulting.ToCanonicalString().Should().Be("42");
    }

    [Fact]
    public void Semantic_module_receipt_round_trips_exact_authority()
    {
        BaseAtomicReceiptResult receipt = new()
        {
            Kind = BaseAtomicReceiptResultKind.ModuleMutation,
            Mutations = [],
            ModuleMutation = new BaseModuleMutationReceiptResult
            {
                OperationId = "auth.ensure-cleanup.v1",
                OperationVersion = 1,
                Disposition = BaseMutationRequestDisposition.Committed,
                Outcome = BaseModuleMutationOutcome.Committed,
                Generations = [],
                CanonicalResultBytes = "{\"created\":true}"u8.ToArray().ToImmutableArray(),
                SemanticActivation = WithSemanticChecksum(new BaseSemanticActivationReceiptEvidence
                {
                    Operation = BaseSemanticActivationOperationKind.Ensure,
                    DefinitionId = "hpd.auth.cleanup.v1",
                    DefinitionVersion = 1,
                    DefinitionChecksum = Enumerable.Repeat((byte)1, 32).ToImmutableArray(),
                    Key = BaseSemanticActivationKeyDigest.Create(Enumerable.Repeat((byte)3, 32).ToArray()),
                    State = BaseSemanticActivationSlotState.Live,
                    SlotGeneration = 1,
                    EnsureDisposition = BaseSemanticActivationEnsureDisposition.Created,
                    ActivationId = new string('a', 64),
                    SlotChecksum = Enumerable.Repeat((byte)4, 32).ToImmutableArray(),
                    JournalPosition = 7,
                    CommitEvidenceChecksum = Enumerable.Repeat((byte)5, 32).ToImmutableArray(),
                    Checksum = ImmutableArray<byte>.Empty,
                }),
            },
        };

        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
            BaseAtomicReceiptWire.From(receipt),
            HPDBaseJsonSerializerContext.Default.BaseAtomicReceiptWire);
        BaseAtomicReceiptResult restored = JsonSerializer.Deserialize(
            bytes, HPDBaseJsonSerializerContext.Default.BaseAtomicReceiptWire)!.Materialize();

        BaseSemanticActivationReceiptEvidence evidence = restored.ModuleMutation!.SemanticActivation!;
        evidence.Operation.Should().Be(BaseSemanticActivationOperationKind.Ensure);
        evidence.EnsureDisposition.Should().Be(BaseSemanticActivationEnsureDisposition.Created);
        evidence.RetirementDisposition.Should().BeNull();
        evidence.SlotGeneration.Should().Be(1);
        evidence.ActivationId.Should().Be(new string('a', 64));
        evidence.DefinitionChecksum.Should().Equal(Enumerable.Repeat((byte)1, 32));
        evidence.Checksum.Should().Equal(BaseSemanticActivationEvidenceContract.ReceiptChecksum(evidence));
    }

    [Fact]
    public void Semantic_key_digest_is_deeply_owned_and_value_comparable()
    {
        byte[] source = Enumerable.Range(0, 32).Select(static value => (byte)value).ToArray();
        BaseSemanticActivationKeyDigest digest = BaseSemanticActivationKeyDigest.Create(source);
        BaseSemanticActivationKeyDigest equal = BaseSemanticActivationKeyDigest.Create(source);

        source[0] = 255;
        byte[] copy = new byte[BaseSemanticActivationKeyDigest.Length];
        digest.CopyTo(copy);
        copy[1] = 255;

        byte[] secondCopy = new byte[BaseSemanticActivationKeyDigest.Length];
        digest.CopyTo(secondCopy);

        digest.Should().Be(equal);
        secondCopy[0].Should().Be(0);
        secondCopy[1].Should().Be(1);
        Action invalid = () => BaseSemanticActivationKeyDigest.Create(new byte[31]);
        invalid.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Semantic_receipt_rejects_every_invalid_shape()
    {
        BaseSemanticActivationReceiptEvidence valid = SemanticEvidence();
        BaseSemanticActivationReceiptEvidence[] hostile =
        [
            valid with { EnsureDisposition = null },
            valid with { RetirementDisposition = BaseSemanticActivationRetirementDisposition.RetiredNow },
            valid with { Operation = BaseSemanticActivationOperationKind.Retire },
            valid with { State = BaseSemanticActivationSlotState.Retired },
            valid with { ActivationId = null },
            valid with { SlotGeneration = 0 },
            valid with { JournalPosition = 0 },
            valid with { DefinitionChecksum = new byte[31].ToImmutableArray() },
            valid with { SlotChecksum = new byte[31].ToImmutableArray() },
            valid with { CommitEvidenceChecksum = new byte[31].ToImmutableArray() },
            valid with { Checksum = new byte[31].ToImmutableArray() },
        ];

        foreach (BaseSemanticActivationReceiptEvidence evidence in hostile)
        {
            Action serialize = () => BaseAtomicReceiptWire.From(ModuleReceipt(evidence));
            serialize.Should().Throw<InvalidOperationException>().WithMessage("base.mutation.receipt.invalid");
        }

        Action mixedCreation = () => BaseAtomicReceiptWire.From(ModuleReceipt(valid) with
        {
            ModuleMutation = ModuleReceipt(valid).ModuleMutation! with { CreatedActivationIds = ["other"] },
        });
        mixedCreation.Should().Throw<InvalidOperationException>().WithMessage("base.mutation.receipt.invalid");
    }

    [Fact]
    public void Semantic_wire_rejects_noncanonical_integer_and_invalid_enum()
    {
        BaseAtomicReceiptWire wire = BaseAtomicReceiptWire.From(ModuleReceipt(SemanticEvidence()));

        Action leadingZero = () => (wire with
        {
            ModuleMutation = wire.ModuleMutation! with
            {
                SemanticActivation = wire.ModuleMutation.SemanticActivation! with { SlotGeneration = "01" },
            },
        }).Materialize();
        Action invalidEnum = () => (wire with
        {
            ModuleMutation = wire.ModuleMutation! with
            {
                SemanticActivation = wire.ModuleMutation.SemanticActivation! with { State = 999 },
            },
        }).Materialize();

        leadingZero.Should().Throw<InvalidOperationException>();
        invalidEnum.Should().Throw<InvalidOperationException>();
    }

    [Theory]
    [InlineData(BaseSemanticActivationRetirementDisposition.RetiredNow, BaseSemanticActivationSlotState.Retired)]
    [InlineData(BaseSemanticActivationRetirementDisposition.AlreadyRetired, BaseSemanticActivationSlotState.Retired)]
    [InlineData(BaseSemanticActivationRetirementDisposition.AlreadyCompacted, BaseSemanticActivationSlotState.CompactedAbsent)]
    public void Semantic_retirement_receipt_round_trips_each_closed_disposition(
        BaseSemanticActivationRetirementDisposition disposition,
        BaseSemanticActivationSlotState state)
    {
        BaseSemanticActivationReceiptEvidence evidence = WithSemanticChecksum(SemanticEvidence() with
        {
            Operation = BaseSemanticActivationOperationKind.Retire,
            State = state,
            EnsureDisposition = null,
            RetirementDisposition = disposition,
            ActivationId = null,
        });

        BaseAtomicReceiptResult restored = BaseAtomicReceiptWire.From(ModuleReceipt(evidence)).Materialize();

        restored.ModuleMutation!.SemanticActivation!.RetirementDisposition.Should().Be(disposition);
        restored.ModuleMutation.SemanticActivation.State.Should().Be(state);
        restored.ModuleMutation.SemanticActivation.EnsureDisposition.Should().BeNull();
        restored.ModuleMutation.SemanticActivation.ActivationId.Should().BeNull();
    }

    private static BaseAtomicReceiptResult ModuleReceipt(BaseSemanticActivationReceiptEvidence evidence) => new()
    {
        Kind = BaseAtomicReceiptResultKind.ModuleMutation,
        Mutations = [],
        ModuleMutation = new BaseModuleMutationReceiptResult
        {
            OperationId = "auth.ensure-cleanup.v1",
            OperationVersion = 1,
            Disposition = BaseMutationRequestDisposition.Committed,
            Outcome = BaseModuleMutationOutcome.Committed,
            Generations = [],
            CanonicalResultBytes = [],
            SemanticActivation = evidence,
        },
    };

    private static BaseSemanticActivationReceiptEvidence SemanticEvidence() => WithSemanticChecksum(new()
    {
        Operation = BaseSemanticActivationOperationKind.Ensure,
        DefinitionId = "hpd.auth.cleanup.v1",
        DefinitionVersion = 1,
        DefinitionChecksum = Enumerable.Repeat((byte)1, 32).ToImmutableArray(),
        Key = BaseSemanticActivationKeyDigest.Create(Enumerable.Repeat((byte)2, 32).ToArray()),
        State = BaseSemanticActivationSlotState.Live,
        SlotGeneration = 1,
        EnsureDisposition = BaseSemanticActivationEnsureDisposition.Created,
        ActivationId = new string('a', 64),
        SlotChecksum = Enumerable.Repeat((byte)3, 32).ToImmutableArray(),
        JournalPosition = 1,
        CommitEvidenceChecksum = Enumerable.Repeat((byte)4, 32).ToImmutableArray(),
        Checksum = ImmutableArray<byte>.Empty,
    });

    private static BaseSemanticActivationReceiptEvidence WithSemanticChecksum(BaseSemanticActivationReceiptEvidence evidence)
        => evidence with { Checksum = BaseSemanticActivationEvidenceContract.ReceiptChecksum(evidence) };

    [Fact]
    public void Mixed_specialized_receipt_members_fail_closed()
    {
        BaseAtomicReceiptResult receipt = new()
        {
            Kind = BaseAtomicReceiptResultKind.RecordMutations,
            Mutations = [],
            ModuleMutation = new BaseModuleMutationReceiptResult
            {
                OperationId = "payments.apply.v1",
                OperationVersion = 1,
                Disposition = BaseMutationRequestDisposition.Committed,
                Outcome = BaseModuleMutationOutcome.Committed,
                Generations = [],
                CanonicalResultBytes = [],
            },
        };

        Action serialize = () => BaseAtomicReceiptWire.From(receipt);

        serialize.Should().Throw<InvalidOperationException>()
            .WithMessage("base.mutation.receipt.invalid");
    }
}
