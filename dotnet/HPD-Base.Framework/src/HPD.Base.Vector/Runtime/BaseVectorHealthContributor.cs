namespace HPD.Base;

internal sealed class BaseVectorHealthContributor(
    IBaseVectorAdministration administration,
    TimeProvider timeProvider) : IBaseHealthContributor, IBaseDiagnosticContributor
{
    public string Id => "hpd.base.vector";

    public async ValueTask<HealthDescriptor[]> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        OperationResult<BaseVectorIndexStatus[]> result = await administration.ListAsync(cancellationToken).ConfigureAwait(false);
        BaseVectorIndexStatus[] indexes = result.Value ?? [];
        HealthStatus status = !result.Status.IsSuccess()
            ? HealthStatus.Unhealthy
            : indexes.Any(static index => index.State is BaseVectorIndexState.RebuildRequired or BaseVectorIndexState.UnhealthyIndeterminate)
                ? HealthStatus.Unhealthy
                : indexes.Any(static index => index.State == BaseVectorIndexState.Building)
                    ? HealthStatus.Degraded
                    : HealthStatus.Healthy;

        return
        [
            new HealthDescriptor
            {
                Id = "hpd.base.vector.provider",
                Scope = HealthScope.Module,
                TargetRef = Id,
                Status = status,
                CheckedAt = timeProvider.GetUtcNow(),
                Summary = status == HealthStatus.Healthy ? "Vector indexes are ready." : "One or more vector indexes require operator attention.",
                PublicSafe = false,
                Visibility = VisibilityLevel.Admin,
                Metrics =
                [
                    Metric("indexCount", indexes.Length),
                    Metric("readyIndexCount", indexes.Count(static index => index.State == BaseVectorIndexState.Ready)),
                    Metric("buildingIndexCount", indexes.Count(static index => index.State == BaseVectorIndexState.Building)),
                    Metric("closedIndexCount", indexes.Count(static index => index.State is BaseVectorIndexState.RebuildRequired or BaseVectorIndexState.UnhealthyIndeterminate)),
                ],
            },
        ];
    }

    public async ValueTask<DiagnosticDescriptor[]> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        OperationResult<BaseVectorIndexStatus[]> result = await administration.ListAsync(cancellationToken).ConfigureAwait(false);
        BaseVectorIndexStatus[] indexes = result.Value ?? [];
        return
        [
            new DiagnosticDescriptor
            {
                Id = "hpd.base.vector.configuration",
                Code = result.Error?.Code ?? "base.vector.configuration.ready",
                Severity = result.Status.IsSuccess() ? DiagnosticSeverity.Info : DiagnosticSeverity.Error,
                Message = result.Status.IsSuccess()
                    ? $"Vector provider exposes {indexes.Length} bounded index descriptors."
                    : "Vector provider diagnostics are unavailable.",
                Category = DiagnosticCategory.Capability,
                Visibility = VisibilityLevel.Admin,
                EmittedAt = timeProvider.GetUtcNow(),
                RelatedFeatureIds = ["base.vector.query", "base.vector.consistency"],
            },
        ];
    }

    private static HealthMetric Metric(string name, double value) => new()
    {
        Name = name,
        Kind = HealthMetricValueKind.Number,
        NumberValue = value,
    };
}
