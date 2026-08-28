namespace HPD.Base;

internal sealed class BaseActivationOperationalState
{
    private long _active;
    private long _quarantined;
    private long _activeHandlers;
    private long _quarantinedHandlers;
    internal long Active => Interlocked.Read(ref _active);
    internal long Quarantined => Interlocked.Read(ref _quarantined);
    internal long ActiveHandlers => Interlocked.Read(ref _activeHandlers);
    internal long QuarantinedHandlers => Interlocked.Read(ref _quarantinedHandlers);
    internal void Enter() => Interlocked.Increment(ref _active);
    internal void Complete() => Interlocked.Decrement(ref _active);
    internal void Quarantine() { Interlocked.Decrement(ref _active); Interlocked.Increment(ref _quarantined); }
    internal void ReleaseQuarantine() => Interlocked.Decrement(ref _quarantined);
    internal void QuarantineContractViolation() => Interlocked.Increment(ref _quarantined);
    internal void EnterHandler() => Interlocked.Increment(ref _activeHandlers);
    internal void CompleteHandler() => Interlocked.Decrement(ref _activeHandlers);
    internal void QuarantineHandler() { Interlocked.Decrement(ref _activeHandlers); Interlocked.Increment(ref _quarantinedHandlers); }
    internal void ReleaseHandlerQuarantine() => Interlocked.Decrement(ref _quarantinedHandlers);
}

internal sealed class BaseActivationHealthContributor(
    BaseActivationOperationalState state,
    TimeProvider timeProvider) : IBaseHealthContributor, IBaseDiagnosticContributor
{
    public string Id => "hpd.base.activations";

    public ValueTask<HealthDescriptor[]> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        HealthStatus status = state.Quarantined == 0 && state.QuarantinedHandlers == 0
            ? HealthStatus.Healthy : HealthStatus.Degraded;
        return ValueTask.FromResult<HealthDescriptor[]>([new()
        {
            Id = "hpd.base.activations.provider", Scope = HealthScope.Module, TargetRef = Id,
            Status = status, CheckedAt = timeProvider.GetUtcNow(), PublicSafe = false,
            Visibility = VisibilityLevel.Admin,
            Summary = status == HealthStatus.Healthy
                ? "Durable activation authority is ready."
                : "Activation provider or handler work remains quarantined.",
            Metrics =
            [
                new HealthMetric { Name = "activeOperations", Kind = HealthMetricValueKind.Number, NumberValue = state.Active },
                new HealthMetric { Name = "quarantinedOperations", Kind = HealthMetricValueKind.Number, NumberValue = state.Quarantined },
                new HealthMetric { Name = "activeHandlers", Kind = HealthMetricValueKind.Number, NumberValue = state.ActiveHandlers },
                new HealthMetric { Name = "quarantinedHandlers", Kind = HealthMetricValueKind.Number, NumberValue = state.QuarantinedHandlers },
            ],
        }]);
    }

    public ValueTask<DiagnosticDescriptor[]> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool degraded = state.Quarantined != 0 || state.QuarantinedHandlers != 0;
        return ValueTask.FromResult<DiagnosticDescriptor[]>([new()
        {
            Id = "hpd.base.activations.provider-lifetime",
            Code = degraded ? "base.activation.quarantined" : "base.activation.providerLifetime.ready",
            Severity = degraded ? DiagnosticSeverity.Warning : DiagnosticSeverity.Info,
            Message = degraded
                ? "Activation provider authority is quarantined or bounded work awaits late completion."
                : "Activation provider ownership is reconciled.",
            Category = DiagnosticCategory.Capability, Visibility = VisibilityLevel.Admin,
            EmittedAt = timeProvider.GetUtcNow(), RelatedFeatureIds = ["base.activations"],
        }]);
    }
}
