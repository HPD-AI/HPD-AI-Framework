namespace HPD.Base;

internal sealed class BaseSemanticActivationProviderHealthContributor(
    HPDBaseInstalledFeatures features,
    IRecordStoreRegistry stores,
    TimeProvider timeProvider) : IBaseHealthContributor, IBaseDiagnosticContributor
{
    public string Id => "hpd.base.semanticActivation.provider";

    public ValueTask<HealthDescriptor[]> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BaseSemanticActivationOperationalStatus status = Status();
        return ValueTask.FromResult<HealthDescriptor[]>([new()
        {
            Id = Id, Scope = HealthScope.Module, TargetRef = "base.semanticActivations",
            Status = status.Ready ? HealthStatus.Healthy : HealthStatus.Degraded,
            CheckedAt = timeProvider.GetUtcNow(), PublicSafe = false, Visibility = VisibilityLevel.Admin,
            Summary = status.Ready
                ? "Semantic activation publication authority is ready."
                : "Semantic activation publication authority is quarantined pending retained-work resolution.",
            Metrics =
            [
                new HealthMetric { Name = "activeOperations", Kind = HealthMetricValueKind.Number, NumberValue = status.ActiveOperations },
                new HealthMetric { Name = "retainedOperations", Kind = HealthMetricValueKind.Number, NumberValue = status.RetainedOperations },
                new HealthMetric { Name = "maximumRetainedOperations", Kind = HealthMetricValueKind.Number, NumberValue = status.MaximumRetainedOperations },
            ],
        }]);
    }

    public ValueTask<DiagnosticDescriptor[]> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BaseSemanticActivationOperationalStatus status = Status();
        return ValueTask.FromResult<DiagnosticDescriptor[]>([new()
        {
            Id = "hpd.base.semanticActivation.provider-lifetime",
            Code = status.Ready ? "base.semanticActivation.provider.ready" : BaseSemanticActivationErrorCodes.Quarantined,
            Severity = status.Ready ? DiagnosticSeverity.Info : DiagnosticSeverity.Warning,
            Message = status.Ready
                ? "Semantic activation provider admission is open."
                : "Bounded semantic activation provider work remains retained.",
            Category = DiagnosticCategory.Capability, Visibility = VisibilityLevel.Admin,
            EmittedAt = timeProvider.GetUtcNow(), RelatedFeatureIds = ["base.semanticActivations"],
        }]);
    }

    private BaseSemanticActivationOperationalStatus Status()
    {
        RecordStoreRegistration? registration = stores.GetRegistration(features.StoreReceipt.RecordStoreRegistrationId);
        return registration?.Store is IBaseSemanticActivationCapabilityProvider provider
            ? provider.SemanticActivationOperationalStatus
            : new BaseSemanticActivationOperationalStatus
            {
                Ready = false, Quarantined = true, ActiveOperations = 0, RetainedOperations = 0,
                MaximumRetainedOperations = features.StoreProvider.SemanticActivations.MaximumQuarantinedOperations,
            };
    }
}
