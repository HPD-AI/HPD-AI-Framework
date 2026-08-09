using System.Text.Json.Serialization;

namespace HPD.Base.Testing;

[BaseCollection("hpd.base.vector.certification.records", typeof(BaseVectorCertificationJsonContext))]
[BaseVectorIndex("hpd.base.vector.certification.cosine", nameof(Embedding), VectorSpace = "hpd.base.vector.certification.space", Dimensions = 2, Function = BaseVectorFunction.CosineSimilarity, FilterFields = [nameof(Tenant), nameof(Active), nameof(Priority), nameof(Optional)])]
[BaseVectorIndex("hpd.base.vector.certification.euclidean", nameof(Embedding), VectorSpace = "hpd.base.vector.certification.space", Dimensions = 2, Function = BaseVectorFunction.EuclideanDistance, FilterFields = [nameof(Tenant), nameof(Active), nameof(Priority), nameof(Optional)])]
[BaseVectorIndex("hpd.base.vector.certification.dot", nameof(Embedding), VectorSpace = "hpd.base.vector.certification.space", Dimensions = 2, Function = BaseVectorFunction.DotProductSimilarity, FilterFields = [nameof(Tenant), nameof(Active), nameof(Priority), nameof(Optional)])]
internal sealed partial record BaseVectorCertificationSchemaRecord
{
    [BaseField("hpd.base.vector.certification.tenant")] public required string Tenant { get; init; }
    [BaseField("hpd.base.vector.certification.active")] public required bool Active { get; init; }
    [BaseField("hpd.base.vector.certification.priority")] public required long Priority { get; init; }
    [BaseField("hpd.base.vector.certification.optional")] public string? Optional { get; init; }
    [BaseField("hpd.base.vector.certification.secret")] public string? Secret { get; init; }
    [BaseField("hpd.base.vector.certification.vector", Operators = BaseFieldOperator.None)] public BaseVector? Embedding { get; init; }
}

[JsonSerializable(typeof(BaseVectorCertificationSchemaRecord))]
internal sealed partial class BaseVectorCertificationJsonContext : JsonSerializerContext;

internal sealed class BaseVectorCertificationPolicy : IPolicyEvaluator
{
    internal int RestrictedVectorQueries;

    public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (request.Operation.Operation == BaseOperationKind.VectorQuery && string.Equals(request.Principal.SubjectId, "certification-restricted", StringComparison.Ordinal))
        {
            Interlocked.Increment(ref RestrictedVectorQueries);
            return ValueTask.FromResult(PolicyDecision.Allow()
                .WithRecordFilter(new FilterExpression
                {
                    Kind = FilterNodeKind.Compare,
                    Field = "hpd.base.vector.certification.tenant",
                    Operator = FilterOperator.Equal,
                    Value = new QueryValue { Kind = QueryValueKind.String, String = "tenant-a" },
                })
                .WithReadMask(new FieldMask
                {
                    Mode = FieldMaskMode.Exclude,
                    Exclude = ["hpd.base.vector.certification.secret"],
                }));
        }
        return ValueTask.FromResult(PolicyDecision.Allow());
    }
}
