using System.Buffers;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HPD.Base;

internal static class BaseCompoundReadAuthority
{
    internal static void Bind(IEnumerable<IBaseReadRegistration> registrations, BaseLogicalSchema schema)
    {
        foreach (IBaseReadRegistration registration in registrations)
        {
            BaseRelationalReadPlan plan = registration.Plan;
            if (plan.Topology == BaseRelationalReadTopology.Ordinary) continue;
            BaseRelationalCompoundCountBranch[] branches = plan.CompoundCountBranches.Select(branch => branch with
            {
                BranchChecksum = BranchChecksum(branch, BaseLogicalSchemaFactory.InstalledCollectionChecksum(schema, branch.Source.CollectionId)),
            }).ToArray();
            registration.BindPlan(plan with
            {
                Sources = branches.Select(static branch => branch.Source with { }).ToArray(),
                CompoundCountBranches = branches,
                CompoundChecksum = CompoundChecksum(branches, registration, plan),
            });
        }
    }

    internal static BaseSchemaAuthorityChecksum BranchChecksum(BaseRelationalCompoundCountBranch branch, byte[] collectionChecksum)
    {
        var writer = new ArrayBufferWriter<byte>();
        Raw(writer, "hpd.base.compound-count-branch.v1\0"u8);
        Text(writer, branch.Id); Text(writer, branch.Source.Id); Text(writer, branch.Source.CollectionId);
        byte[] predicate = branch.Predicate is null ? [] : JsonSerializer.SerializeToUtf8Bytes(
            branch.Predicate, HPDBaseRelationalJsonSerializerContext.Default.BaseRelationalPredicate);
        Bytes(writer, predicate); Text(writer, branch.Discriminator); Text(writer, branch.DiscriminatorOutputFieldId);
        Text(writer, branch.CountOutputFieldId); Raw(writer, collectionChecksum);
        return BaseSchemaAuthorityChecksum.Create(SHA256.HashData(writer.WrittenSpan));
    }

    internal static BaseSchemaAuthorityChecksum CompoundChecksum(
        BaseRelationalCompoundCountBranch[] branches, IBaseReadRegistration registration, BaseRelationalReadPlan plan)
    {
        var writer = new ArrayBufferWriter<byte>();
        Raw(writer, "hpd.base.compound-count-plan.v1\0"u8); Integer(writer, branches.Length);
        foreach (BaseRelationalCompoundCountBranch branch in branches) Raw(writer, branch.BranchChecksum.ToArray());
        Text(writer, registration.ParameterSerializerContractChecksum); Text(writer, registration.RowSerializerContractChecksum);
        Integer(writer, (int)plan.DependencyMode); Integer(writer, plan.Budgets.MaxResultRows);
        Integer(writer, plan.Budgets.MaxResultBytes); Integer(writer, plan.Budgets.MaxOperations);
        Integer(writer, plan.Budgets.MaxExecutionMilliseconds); Integer(writer, plan.Budgets.MaxCompoundBranches);
        Integer(writer, plan.Budgets.MaxCompoundOperations);
        return BaseSchemaAuthorityChecksum.Create(SHA256.HashData(writer.WrittenSpan));
    }

    private static void Text(IBufferWriter<byte> writer, string value) => Bytes(writer, Encoding.UTF8.GetBytes(value));
    private static void Bytes(IBufferWriter<byte> writer, ReadOnlySpan<byte> value) { Integer(writer, value.Length); Raw(writer, value); }
    private static void Integer(IBufferWriter<byte> writer, int value)
    { Span<byte> bytes = writer.GetSpan(4); BinaryPrimitives.WriteInt32BigEndian(bytes, value); writer.Advance(4); }
    private static void Raw(IBufferWriter<byte> writer, ReadOnlySpan<byte> value)
    { value.CopyTo(writer.GetSpan(value.Length)); writer.Advance(value.Length); }
}
