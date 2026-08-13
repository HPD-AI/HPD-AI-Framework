namespace HPD.Base;

internal sealed class BaseSubjectControlOperationalState
{
    private int _ready;
    private int _degraded;
    private long _quarantined;

    internal bool Ready => Volatile.Read(ref _ready) != 0;
    internal bool Degraded => Volatile.Read(ref _degraded) != 0;
    internal long Quarantined => Interlocked.Read(ref _quarantined);
    internal bool AdmitsLiveState => Ready && !Degraded;
    internal bool AdmitsRotation => Ready && !Degraded && Quarantined == 0;

    internal void MarkReady()
    {
        Volatile.Write(ref _degraded, 0);
        Volatile.Write(ref _ready, 1);
    }

    internal void MarkDegraded()
    {
        Volatile.Write(ref _degraded, 1);
        Volatile.Write(ref _ready, 0);
    }

    internal void Quarantine()
    {
        MarkDegraded();
        Interlocked.Increment(ref _quarantined);
    }

    internal void ReleaseQuarantine() => Interlocked.Decrement(ref _quarantined);
}

internal sealed class BaseSubjectControlHealthContributor(
    BaseSubjectControlOperationalState state,
    TimeProvider timeProvider) : IBaseHealthContributor, IBaseDiagnosticContributor
{
    public string Id => "hpd.base.subject-control";

    public ValueTask<HealthDescriptor[]> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        HealthStatus status = state.AdmitLiveStatus();
        return ValueTask.FromResult<HealthDescriptor[]>(
        [
            new HealthDescriptor
            {
                Id = Id,
                Scope = HealthScope.Runtime,
                TargetRef = "base.subject-control",
                Status = status,
                CheckedAt = timeProvider.GetUtcNow(),
                Summary = status == HealthStatus.Healthy
                    ? "Exported-subject control state is reconciled."
                    : "Exported-subject control reconciliation requires recovery.",
                PublicSafe = false,
                Visibility = VisibilityLevel.Admin,
                Metrics =
                [
                    Metric("ready", state.Ready ? 1 : 0),
                    Metric("degraded", state.Degraded ? 1 : 0),
                    Metric("quarantinedCallbacks", state.Quarantined),
                ],
            },
        ]);
    }

    public ValueTask<DiagnosticDescriptor[]> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<DiagnosticDescriptor[]>(
        [
            new DiagnosticDescriptor
            {
                Id = "hpd.base.subject-control.reconciliation",
                Code = state.AdmitLiveStatus() == HealthStatus.Healthy
                    ? "base.subject.control.ready"
                    : BaseSubjectErrorCodes.ValidationUnavailable,
                Severity = state.AdmitLiveStatus() == HealthStatus.Healthy
                    ? DiagnosticSeverity.Info
                    : DiagnosticSeverity.Error,
                Message = state.AdmitLiveStatus() == HealthStatus.Healthy
                    ? "Exported-subject control publications are reconciled."
                    : "Exported-subject control publication reconciliation is unavailable.",
                Category = DiagnosticCategory.Capability,
                Visibility = VisibilityLevel.Admin,
                EmittedAt = timeProvider.GetUtcNow(),
                RelatedFeatureIds = ["base.subject.validation", "base.realtime.v2"],
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

internal static class BaseSubjectControlOperationalStateExtensions
{
    internal static HealthStatus AdmitLiveStatus(this BaseSubjectControlOperationalState state) =>
        state.AdmitsLiveState ? HealthStatus.Healthy : state.Degraded ? HealthStatus.Degraded : HealthStatus.Unhealthy;
}
