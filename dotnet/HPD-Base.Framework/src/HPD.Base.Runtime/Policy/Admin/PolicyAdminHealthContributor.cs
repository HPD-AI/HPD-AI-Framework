using HPD.Base.Health;
using HPD.Base.Runtime.Health;
using HPD.Base.Policy;

namespace HPD.Base.Runtime.Policy.Admin;

internal sealed class PolicyAdminHealthContributor : IBaseHealthContributor, IBaseDiagnosticContributor
{
    private readonly IBasePolicyExplainService _explainService;
    private readonly IEnumerable<IPolicyEvaluator> _evaluators;
    private readonly TimeProvider _timeProvider;

    public PolicyAdminHealthContributor(
        IBasePolicyExplainService explainService,
        IEnumerable<IPolicyEvaluator> evaluators,
        TimeProvider timeProvider)
    {
        _explainService = explainService;
        _evaluators = evaluators;
        _timeProvider = timeProvider;
    }

    public string Id => "hpd.base.policy.admin";

    public ValueTask<HealthDescriptor[]> GetHealthAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = _explainService;
        return ValueTask.FromResult<HealthDescriptor[]>(
        [
            new HealthDescriptor
            {
                Id = "hpd.base.policy.admin.registration",
                Scope = HealthScope.Module,
                TargetRef = "hpd.base.policy.admin",
                Status = HealthStatus.Healthy,
                CheckedAt = _timeProvider.GetUtcNow(),
                Summary = "Admin policy explain services are registered.",
                PublicSafe = false,
                Visibility = VisibilityLevel.Admin
            }
        ]);
    }

    public ValueTask<DiagnosticDescriptor[]> GetDiagnosticsAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var now = _timeProvider.GetUtcNow();
        var diagnostics = new List<DiagnosticDescriptor>
        {
            Diagnostic("hpd.base.policy.admin.redactionStrictMode", DiagnosticSeverity.Info, "Admin policy explain redaction is in strict mode.", now),
            Diagnostic("hpd.base.policy.admin.writeCheckRuntimeUnsupported", DiagnosticSeverity.Info, "Policy write checks fail closed when their shape is not runtime-evaluable.", now),
            Diagnostic("hpd.base.policy.admin.httpRouteNotMapped", DiagnosticSeverity.Info, "Admin policy explain HTTP route mapping is optional and may be disabled by the host.", now),
            Diagnostic("hpd.base.policy.admin.serviceGateMisconfigured", DiagnosticSeverity.Info, "Admin policy explain enforces a service-level admin gate.", now)
        };

        if (!_evaluators.Any())
        {
            diagnostics.Add(Diagnostic("hpd.base.policy.admin.noPolicyEvaluator", DiagnosticSeverity.Warning, "No policy evaluator is registered; policy explain will fail closed.", now));
        }

        return ValueTask.FromResult(diagnostics.ToArray());
    }

    private static DiagnosticDescriptor Diagnostic(
        string id,
        DiagnosticSeverity severity,
        string message,
        DateTimeOffset emittedAt) => new()
        {
            Id = id,
            Code = id,
            Severity = severity,
            Message = message,
            Category = DiagnosticCategory.Policy,
            Visibility = VisibilityLevel.Admin,
            EmittedAt = emittedAt
        };
}
