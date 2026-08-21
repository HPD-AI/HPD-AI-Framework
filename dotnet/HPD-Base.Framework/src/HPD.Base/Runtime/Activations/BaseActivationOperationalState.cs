namespace HPD.Base;

internal sealed class BaseActivationOperationalState
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

internal sealed class BaseActivationHealthContributor(
    BaseActivationOperationalState state,
    TimeProvider timeProvider) : IBaseHealthContributor, IBaseDiagnosticContributor
{
    public string Id => "hpd.base.activations";

    public ValueTask<HealthDescriptor[]> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        HealthStatus status = state.Quarantined == 0 ? HealthStatus.Healthy : HealthStatus.Degraded;
        return ValueTask.FromResult<HealthDescriptor[]>([new()
        {
            Id = "hpd.base.activations.provider", Scope = HealthScope.Module, TargetRef = Id,
            Status = status, CheckedAt = timeProvider.GetUtcNow(), PublicSafe = false,
            Visibility = VisibilityLevel.Admin,
            Summary = status == HealthStatus.Healthy
                ? "Durable activation authority is ready."
                : "Activation provider work remains quarantined.",
            Metrics =
            [
                new HealthMetric { Name = "activeOperations", Kind = HealthMetricValueKind.Number, NumberValue = state.Active },
                new HealthMetric { Name = "quarantinedOperations", Kind = HealthMetricValueKind.Number, NumberValue = state.Quarantined },
            ],
        }]);
    }

    public ValueTask<DiagnosticDescriptor[]> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool degraded = state.Quarantined != 0;
        return ValueTask.FromResult<DiagnosticDescriptor[]>([new()
        {
            Id = "hpd.base.activations.provider-lifetime",
            Code = degraded ? "base.activation.quarantined" : "base.activation.providerLifetime.ready",
            Severity = degraded ? DiagnosticSeverity.Warning : DiagnosticSeverity.Info,
            Message = degraded
                ? "Bounded activation provider work awaits late completion."
                : "Activation provider ownership is reconciled.",
            Category = DiagnosticCategory.Capability, Visibility = VisibilityLevel.Admin,
            EmittedAt = timeProvider.GetUtcNow(), RelatedFeatureIds = ["base.activations"],
        }]);
    }
}
