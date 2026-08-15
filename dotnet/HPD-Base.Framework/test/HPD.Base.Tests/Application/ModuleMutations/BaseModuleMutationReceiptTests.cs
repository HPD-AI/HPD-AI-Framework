using System.Collections.Immutable;
using System.Text.Json;

namespace HPD.Base.Tests.Application.ModuleMutations;

public sealed class BaseModuleMutationReceiptTests
{
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
