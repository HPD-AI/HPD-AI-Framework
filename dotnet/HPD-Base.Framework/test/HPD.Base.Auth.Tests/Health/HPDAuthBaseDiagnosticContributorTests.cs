using HPD.Base;

namespace HPD.Base.Auth.Tests.Health;

public sealed class HPDBaseAuthDiagnosticContributorTests
{
    [Fact]
    public async Task EmitsAdminDiagnosticWhenHPDAuthServicesAreRequiredButNotDetected()
    {
        using var provider = Services().BuildServiceProvider();
        var contributor = provider.GetServices<IBaseDiagnosticContributor>()
            .OfType<HPDBaseAuthDiagnosticContributor>()
            .Single();

        var diagnostics = await contributor.GetDiagnosticsAsync();

        diagnostics.Should().Contain(diagnostic =>
            diagnostic.Id == HPDBaseAuthDiagnosticIds.MissingAuthServices
            && diagnostic.Visibility == VisibilityLevel.Admin
            && diagnostic.Severity == DiagnosticSeverity.Info);
    }

    [Fact]
    public async Task SuppressesDiagnosticWhenClaimOnlyModeIsExplicit()
    {
        using var provider = Services(options => options.RequireHPDAuthServices = false).BuildServiceProvider();
        var contributor = provider.GetServices<IBaseDiagnosticContributor>()
            .OfType<HPDBaseAuthDiagnosticContributor>()
            .Single();

        var diagnostics = await contributor.GetDiagnosticsAsync();

        diagnostics.Should().NotContain(diagnostic => diagnostic.Id == HPDBaseAuthDiagnosticIds.MissingAuthServices);
    }

    [Fact]
    public async Task SuppressesDiagnosticWhenHostIntegrationIsDetected()
    {
        var services = Services();
        services.AddSingleton<IHPDBaseAuthHostIntegrationStatus>(new DetectedHostIntegrationStatus());
        using var provider = services.BuildServiceProvider();
        var contributor = provider.GetServices<IBaseDiagnosticContributor>()
            .OfType<HPDBaseAuthDiagnosticContributor>()
            .Single();

        var diagnostics = await contributor.GetDiagnosticsAsync();

        diagnostics.Should().NotContain(diagnostic => diagnostic.Id == HPDBaseAuthDiagnosticIds.MissingAuthServices);
    }

    [Fact]
    public async Task EmitsNoGrantProviderDiagnosticWhenNoAuthorizationSourceIsConfigured()
    {
        using var provider = Services(options => options.RequireHPDAuthServices = false).BuildServiceProvider();
        var contributor = provider.GetServices<IBaseDiagnosticContributor>()
            .OfType<HPDBaseAuthDiagnosticContributor>()
            .Single();

        var diagnostics = await contributor.GetDiagnosticsAsync();

        diagnostics.Should().ContainSingle(diagnostic => diagnostic.Id == HPDBaseAuthDiagnosticIds.NoGrantProvider);
    }

    private static ServiceCollection Services(Action<HPDBaseAuthOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHPDBaseAuthServices(configure);
        return services;
    }

    private sealed class DetectedHostIntegrationStatus : IHPDBaseAuthHostIntegrationStatus
    {
        public bool HPDAuthServicesDetected => true;

        public string Source => "test";

        public string[] MissingRequiredServiceNames => [];
    }
}
