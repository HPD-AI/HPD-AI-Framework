namespace HPD.Base;

internal sealed class BaseSubjectLifecycleOperationalState
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

internal sealed class BaseSubjectLifecycleHealthContributor(
    BaseSubjectLifecycleOperationalState state,
    TimeProvider timeProvider) : IBaseHealthContributor, IBaseDiagnosticContributor
{
    public string Id => "hpd.base.subject-lifecycle";

    public ValueTask<HealthDescriptor[]> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        HealthStatus status = state.Quarantined == 0 ? HealthStatus.Healthy : HealthStatus.Degraded;
        return ValueTask.FromResult<HealthDescriptor[]>(
        [
            new HealthDescriptor
            {
                Id = "hpd.base.subject-lifecycle.provider",
                Scope = HealthScope.Module,
                TargetRef = Id,
                Status = status,
                CheckedAt = timeProvider.GetUtcNow(),
                Summary = status == HealthStatus.Healthy
                    ? "Subject lifecycle delivery is ready."
                    : "A subject lifecycle provider read remains quarantined.",
                PublicSafe = false,
                Visibility = VisibilityLevel.Admin,
                Metrics =
                [
                    Metric("activeReads", state.Active),
                    Metric("quarantinedReads", state.Quarantined),
                ],
            },
        ]);
    }

    public ValueTask<DiagnosticDescriptor[]> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool degraded = state.Quarantined != 0;
        return ValueTask.FromResult<DiagnosticDescriptor[]>(
        [
            new DiagnosticDescriptor
            {
                Id = "hpd.base.subject-lifecycle.provider-lifetime",
                Code = degraded ? BaseSubjectErrorCodes.Timeout : "base.subjectLifecycle.providerLifetime.ready",
                Severity = degraded ? DiagnosticSeverity.Warning : DiagnosticSeverity.Info,
                Message = degraded
                    ? "A bounded subject lifecycle provider read awaits late completion."
                    : "Subject lifecycle provider read ownership is reconciled.",
                Category = DiagnosticCategory.Capability,
                Visibility = VisibilityLevel.Admin,
                EmittedAt = timeProvider.GetUtcNow(),
                RelatedFeatureIds = ["base.subjectLifecycle.feed"],
            },
        ]);
    }

    private static HealthMetric Metric(string name, double value) => new()
    {
        Name = name,
        Kind = HealthMetricValueKind.Number,
        NumberValue = value,
    };
}
