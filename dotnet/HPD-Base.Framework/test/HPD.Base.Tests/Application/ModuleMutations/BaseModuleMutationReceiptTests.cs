using System.Collections.Immutable;
using System.Text.Json;

namespace HPD.Base.Tests.Application.ModuleMutations;

public sealed class BaseModuleMutationReceiptTests
{
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
