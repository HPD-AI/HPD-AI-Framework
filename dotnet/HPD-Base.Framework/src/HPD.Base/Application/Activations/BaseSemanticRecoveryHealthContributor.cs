namespace HPD.Base;

internal sealed class BaseSemanticRecoveryHealthContributor(
    BaseSemanticRecoveryAuthorityRegistry registry,
    TimeProvider timeProvider) : IBaseHealthContributor, IBaseDiagnosticContributor
{
    public string Id => "hpd.base.semanticRecovery";

    public ValueTask<HealthDescriptor[]> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool quarantined = registry.Selections.Keys.Any(registry.IsQuarantined);
        return ValueTask.FromResult<HealthDescriptor[]>([new()
        {
            Id = "hpd.base.semanticRecovery.external", Scope = HealthScope.Module, TargetRef = Id,
            Status = quarantined ? HealthStatus.Degraded : HealthStatus.Healthy,
            CheckedAt = timeProvider.GetUtcNow(), PublicSafe = false, Visibility = VisibilityLevel.Admin,
            Summary = quarantined
                ? "External semantic-recovery work remains quarantined."
                : "External semantic-recovery authority is ready.",
        }]);
    }

    public ValueTask<DiagnosticDescriptor[]> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        bool quarantined = registry.Selections.Keys.Any(registry.IsQuarantined);
        return ValueTask.FromResult<DiagnosticDescriptor[]>([new()
        {
            Id = "hpd.base.semanticRecovery.external-lifetime",
            Code = quarantined ? "base.semanticActivation.externalPublicationQuarantined" : "base.semanticActivation.externalPublication.ready",
            Severity = quarantined ? DiagnosticSeverity.Warning : DiagnosticSeverity.Info,
            Message = quarantined
                ? "Bounded external semantic-recovery work awaits explicit recovery."
                : "External semantic-recovery ownership is reconciled.",
            Category = DiagnosticCategory.Capability, Visibility = VisibilityLevel.Admin,
            EmittedAt = timeProvider.GetUtcNow(), RelatedFeatureIds = ["base.semanticActivations"],
        }]);
    }
}
