using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Auth.Base.ConsumerProof;

internal static class ReadProofGrants
{
    internal const string Json = "proof.json.read.execute";
    internal const string Count = "proof.count.read.execute";
}

[BaseCollection("proof.json-items", typeof(ReadProofJsonContext))]
internal sealed partial record ProofJsonItem
{
    [BaseField("payload", MaximumCanonicalJsonBytes = 128, JsonShape = BaseJsonShape.Object,
        MaximumJsonDepth = 4, MaximumJsonArrayItems = 8, MaximumJsonObjectProperties = 8,
        MaximumJsonTotalNodes = 16, MaximumJsonTotalStringUtf8Bytes = 64,
        MaximumJsonTotalNameUtf8Bytes = 64)]
    public required BaseCanonicalJson Payload { get; init; }
}

[BaseRead("proof.json.read", typeof(ReadProofJsonContext), RequiredGrantId = ReadProofGrants.Json)]
internal sealed partial record ProofJsonRead
{
    [BaseReadParameter("proof.json.parameter")]
    public required BaseCanonicalJson Payload { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("proof.json.result")]
        public required BaseCanonicalJson Payload { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<ProofJsonRead, Row> read)
    {
        read.From(ProofJsonItem.Collection, "item", out BaseReadSource<ProofJsonItem> item)
            .BindCanonicalJsonParameter(Parameters.Payload, ProofJsonItem.Fields.Payload)
            .Where(item.Field(ProofJsonItem.Fields.Payload).Equal(read.Parameter(Parameters.Payload)))
            .Project(Row.Fields.Payload, item.Field(ProofJsonItem.Fields.Payload));
    }
}

[BaseCollection("proof.count-alpha", typeof(ReadProofJsonContext))]
internal sealed partial record ProofCountAlpha
{
    [BaseField("proof.alpha.enabled")] public required bool Enabled { get; init; }
}

[BaseCollection("proof.count-beta", typeof(ReadProofJsonContext))]
internal sealed partial record ProofCountBeta
{
    [BaseField("proof.beta.enabled")] public required bool Enabled { get; init; }
}

[BaseRead("proof.count.summary", typeof(ReadProofJsonContext), RequiredGrantId = ReadProofGrants.Count)]
internal sealed partial record ProofCountSummary
{
    [BaseReadParameter("proof.count.enabled")] public required bool Enabled { get; init; }

    public sealed partial record Row
    {
        [BaseReadField("proof.count.kind")] public required string Kind { get; init; }
        [BaseReadField("proof.count.value")] public required long Count { get; init; }
    }

    public static void Configure(BaseReadDefinitionBuilder<ProofCountSummary, Row> read) => read
        .CountBranch("alpha-branch", Row.Fields.Kind, "alpha", ProofCountAlpha.Collection, Row.Fields.Count,
            branch => branch.Where(branch.Field(ProofCountAlpha.Fields.Enabled)
                .Equal(branch.Parameter(Parameters.Enabled))))
        .CountBranch("beta-branch", Row.Fields.Kind, "beta", ProofCountBeta.Collection, Row.Fields.Count,
            branch => branch.Where(branch.Field(ProofCountBeta.Fields.Enabled)
                .Equal(branch.Parameter(Parameters.Enabled))))
        .CompoundLimits(4_096, 32, 2_000, 2, 16);
}

[JsonSerializable(typeof(ProofJsonItem))]
[JsonSerializable(typeof(ProofJsonRead))]
[JsonSerializable(typeof(ProofJsonRead.Row), TypeInfoPropertyName = "ProofJsonReadRow")]
[JsonSerializable(typeof(ProofCountAlpha))]
[JsonSerializable(typeof(ProofCountBeta))]
[JsonSerializable(typeof(ProofCountSummary))]
[JsonSerializable(typeof(ProofCountSummary.Row), TypeInfoPropertyName = "ProofCountSummaryRow")]
internal sealed partial class ReadProofJsonContext : JsonSerializerContext;
