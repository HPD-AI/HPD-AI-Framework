using HPD.Base.Health;
using HPD.Base.Runtime.Health;

namespace HPD.Base.Auth.HPDAuth.Tests.Health;

public sealed class HPDAuthBaseDiagnosticContributorTests
{
    [Fact]
    public async Task EmitsAdminDiagnosticWhenHPDAuthServicesAreRequiredButNotDetected()
    {
        using var provider = Services().BuildServiceProvider();
        var contributor = provider.GetServices<IBaseDiagnosticContributor>()
            .OfType<HPDAuthBaseDiagnosticContributor>()
            .Single();

        var diagnostics = await contributor.GetDiagnosticsAsync();

        diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == HPDAuthBaseDiagnosticIds.MissingAuthServices
            && diagnostic.Visibility == VisibilityLevel.Admin
            && diagnostic.Severity == DiagnosticSeverity.Info);
    }

    [Fact]
    public async Task SuppressesDiagnosticWhenClaimOnlyModeIsExplicit()
    {
        using var provider = Services(options => options.RequireHPDAuthServices = false).BuildServiceProvider();
        var contributor = provider.GetServices<IBaseDiagnosticContributor>()
            .OfType<HPDAuthBaseDiagnosticContributor>()
            .Single();

        var diagnostics = await contributor.GetDiagnosticsAsync();

        diagnostics.Should().NotContain(diagnostic => diagnostic.Id == HPDAuthBaseDiagnosticIds.MissingAuthServices);
    }

    [Fact]
    public async Task SuppressesDiagnosticWhenHostIntegrationIsDetected()
    {
        var services = Services();
        services.AddSingleton<IHPDAuthBaseHostIntegrationStatus>(new DetectedHostIntegrationStatus());
        using var provider = services.BuildServiceProvider();
        var contributor = provider.GetServices<IBaseDiagnosticContributor>()
            .OfType<HPDAuthBaseDiagnosticContributor>()
            .Single();

        var diagnostics = await contributor.GetDiagnosticsAsync();

        diagnostics.Should().NotContain(diagnostic => diagnostic.Id == HPDAuthBaseDiagnosticIds.MissingAuthServices);
    }

    [Fact]
    public async Task EmitsNoGrantProviderDiagnosticWhenNoAuthorizationSourceIsConfigured()
    {
        using var provider = Services(options => options.RequireHPDAuthServices = false).BuildServiceProvider();
        var contributor = provider.GetServices<IBaseDiagnosticContributor>()
            .OfType<HPDAuthBaseDiagnosticContributor>()
            .Single();

        var diagnostics = await contributor.GetDiagnosticsAsync();

        diagnostics.Should().ContainSingle(diagnostic => diagnostic.Id == HPDAuthBaseDiagnosticIds.NoGrantProvider);
    }

    private static ServiceCollection Services(Action<HPDBaseHPDAuthOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddHPDBaseHPDAuth(configure);
        return services;
    }

    private sealed class DetectedHostIntegrationStatus : IHPDAuthBaseHostIntegrationStatus
    {
        public bool HPDAuthServicesDetected => true;

        public string Source => "test";

        public string[] MissingRequiredServiceNames => [];
    }
}
