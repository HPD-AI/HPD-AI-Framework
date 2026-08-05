namespace HPD.Base;

internal sealed class BaseApplicationHealthContributor(IHPDBaseApplication application) : IBaseHealthContributor
{
    /// <summary>Gets the ID.</summary>
    public string Id => "base.application.readiness";

    /// <summary>Executes the get health async operation.</summary>
    public ValueTask<HealthDescriptor[]> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BaseApplicationReadiness readiness = application.CurrentReadiness;
        HealthStatus status = readiness.State == BaseApplicationReadinessState.Ready
            ? HealthStatus.Healthy
            : HealthStatus.Unhealthy;
        return ValueTask.FromResult<HealthDescriptor[]>(
        [
            new HealthDescriptor
            {
                Id = Id,
                Scope = HealthScope.Runtime,
                TargetRef = "base.application",
                Status = status,
                CheckedAt = DateTimeOffset.UtcNow,
                Summary = readiness.State == BaseApplicationReadinessState.Ready
                    ? "HPD.BASE required schema assets are ready."
                    : "HPD.BASE required schema assets are not ready.",
                PublicSafe = true,
                Visibility = VisibilityLevel.Public,
                Metrics =
                [
                    new HealthMetric { Name = "readinessState", Kind = HealthMetricValueKind.Text, TextValue = readiness.State.ToString() },
                    new HealthMetric { Name = "schemaGeneration", Kind = HealthMetricValueKind.Number, NumberValue = readiness.SchemaGeneration }
                ]
            }
        ]);
    }
}
