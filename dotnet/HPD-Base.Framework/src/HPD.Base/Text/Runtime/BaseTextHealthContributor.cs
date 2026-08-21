namespace HPD.Base;

internal sealed class BaseTextHealthContributor(IBaseTextAdministration administration, TimeProvider timeProvider, BaseTextOperationalState operationalState) : IBaseHealthContributor, IBaseDiagnosticContributor
{
    public string Id => "hpd.base.text";
    public async ValueTask<HealthDescriptor[]> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        OperationResult<BaseTextIndexStatus[]> result = await administration.ListAsync(cancellationToken).ConfigureAwait(false); BaseTextIndexStatus[] indexes = result.Value ?? [];
        HealthStatus status = !result.Status.IsSuccess() || indexes.Any(static index => index.State is BaseTextIndexState.RebuildRequired or BaseTextIndexState.UnhealthyIndeterminate) ? HealthStatus.Unhealthy : indexes.Any(static index => index.State == BaseTextIndexState.Building) || operationalState.Quarantined > 0 ? HealthStatus.Degraded : HealthStatus.Healthy;
        return [new HealthDescriptor { Id = "hpd.base.text.provider", Scope = HealthScope.Module, TargetRef = Id, Status = status, CheckedAt = timeProvider.GetUtcNow(), Summary = status == HealthStatus.Healthy ? "Text indexes are ready." : "One or more text indexes require operator attention.", PublicSafe = false, Visibility = VisibilityLevel.Admin, Metrics = [Metric("indexCount", indexes.Length), Metric("readyIndexCount", indexes.Count(static index => index.State == BaseTextIndexState.Ready)), Metric("activeOperations", operationalState.Active), Metric("quarantinedOperations", operationalState.Quarantined)] }];
    }
    public async ValueTask<DiagnosticDescriptor[]> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        OperationResult<BaseTextIndexStatus[]> result = await administration.ListAsync(cancellationToken).ConfigureAwait(false);
        return [new DiagnosticDescriptor { Id = "hpd.base.text.configuration", Code = result.Error?.Code ?? "base.text.configuration.ready", Severity = result.Status.IsSuccess() ? DiagnosticSeverity.Info : DiagnosticSeverity.Error, Message = result.Status.IsSuccess() ? $"Text provider exposes {result.Value?.Length ?? 0} bounded index descriptors." : "Text provider diagnostics are unavailable.", Category = DiagnosticCategory.Capability, Visibility = VisibilityLevel.Admin, EmittedAt = timeProvider.GetUtcNow(), RelatedFeatureIds = ["base.text.query", "base.text.rebuild"] }];
    }
    private static HealthMetric Metric(string name, double value) => new() { Name = name, Kind = HealthMetricValueKind.Number, NumberValue = value };
}
