namespace HPD.Base;

internal sealed class BaseSubjectRetirementOperationalState
{
    private long _active;
    private long _quarantined;
    internal long Active => Interlocked.Read(ref _active);
    internal long Quarantined => Interlocked.Read(ref _quarantined);
    internal void Enter() => Interlocked.Increment(ref _active);
    internal void Complete() => Interlocked.Decrement(ref _active);
    internal void Quarantine() { Interlocked.Decrement(ref _active); Interlocked.Increment(ref _quarantined); }
    internal void ReleaseQuarantine() => Interlocked.Decrement(ref _quarantined);
}

internal sealed class BaseSubjectRetirementHealthContributor(
    BaseSubjectRetirementOperationalState state,
    TimeProvider timeProvider) : IBaseHealthContributor, IBaseDiagnosticContributor
{
    public string Id => "hpd.base.subject-retirement";
    public ValueTask<HealthDescriptor[]> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        HealthStatus status = state.Quarantined == 0 ? HealthStatus.Healthy : HealthStatus.Degraded;
        return ValueTask.FromResult<HealthDescriptor[]>([new()
        {
            Id = "hpd.base.subject-retirement.provider", Scope = HealthScope.Module, TargetRef = Id,
            Status = status, CheckedAt = timeProvider.GetUtcNow(), PublicSafe = false, Visibility = VisibilityLevel.Admin,
            Summary = status == HealthStatus.Healthy ? "Subject retirement is ready." : "Subject retirement provider work remains quarantined.",
            Metrics = [Metric("activeOperations", state.Active), Metric("quarantinedOperations", state.Quarantined)],
        }]);
    }
    public ValueTask<DiagnosticDescriptor[]> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested(); bool degraded = state.Quarantined != 0;
        return ValueTask.FromResult<DiagnosticDescriptor[]>([new()
        {
            Id = "hpd.base.subject-retirement.provider-lifetime",
            Code = degraded ? BaseSubjectRetirementErrorCodes.Timeout : "base.subjectRetirement.providerLifetime.ready",
            Severity = degraded ? DiagnosticSeverity.Warning : DiagnosticSeverity.Info,
            Message = degraded ? "Bounded subject retirement work awaits late completion." : "Subject retirement provider ownership is reconciled.",
            Category = DiagnosticCategory.Capability, Visibility = VisibilityLevel.Admin, EmittedAt = timeProvider.GetUtcNow(),
            RelatedFeatureIds = ["base.subjectRetirement"],
        }]);
    }
    private static HealthMetric Metric(string name, double value) => new() { Name = name, Kind = HealthMetricValueKind.Number, NumberValue = value };
}
