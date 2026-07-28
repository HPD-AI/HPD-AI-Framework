namespace HPD.Base.Auth.HPDAuth.AspNetCore.Tests.DependencyInjection;

public sealed class AddHPDBaseHPDAuthAspNetCoreTests
{
    [Fact]
    public void RegistersCorePolicyEvaluatorAndHttpPrincipalMapper()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBaseHPDAuthAspNetCore();
        using var provider = services.BuildServiceProvider();

        provider.GetServices<IPolicyEvaluator>().Should().ContainSingle(evaluator => evaluator is HPDAuthBasePolicyEvaluator);
        provider.GetServices<IBaseHttpPrincipalMapper>().Should().ContainSingle(mapper => mapper is HPDAuthBaseHttpPrincipalMapper);
        provider.GetServices<IHPDAuthBaseHostIntegrationStatus>().Should().ContainSingle();
    }

    [Fact]
    public async Task BridgeWithoutHPDAuthServicesReportsMissingAuthServicesDiagnostic()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBaseHPDAuthAspNetCore();
        using var provider = services.BuildServiceProvider();

        var status = provider.GetServices<IHPDAuthBaseHostIntegrationStatus>().Single();
        var diagnostics = await provider.GetServices<IBaseDiagnosticContributor>().Single().GetDiagnosticsAsync();

        status.HPDAuthServicesDetected.Should().BeFalse();
        status.MissingRequiredServiceNames.Should().Contain([
            nameof(ITenantContext),
            "UserManager<ApplicationUser>",
            "SignInManager<ApplicationUser>",
            nameof(IAuditLogger),
            nameof(ISessionManager),
            nameof(IRefreshTokenStore)
        ]);
        diagnostics.Should().ContainSingle(diagnostic =>
            diagnostic.Id == HPDAuthBaseDiagnosticIds.MissingAuthServices
            && diagnostic.Message.Contains(nameof(ITenantContext), StringComparison.Ordinal));
    }

    [Fact]
    public async Task BaselineHPDAuthServicesSuppressMissingAuthServicesDiagnostic()
    {
        var services = new ServiceCollection().AddLogging();
        AddBaselineHPDAuthServiceDescriptors(services);
        services.AddHPDBaseHPDAuthAspNetCore();
        using var provider = services.BuildServiceProvider();

        var status = provider.GetServices<IHPDAuthBaseHostIntegrationStatus>().Single();
        var diagnostics = await provider.GetServices<IBaseDiagnosticContributor>().Single().GetDiagnosticsAsync();

        status.HPDAuthServicesDetected.Should().BeTrue();
        status.MissingRequiredServiceNames.Should().BeEmpty();
        diagnostics.Should().NotContain(diagnostic => diagnostic.Id == HPDAuthBaseDiagnosticIds.MissingAuthServices);
    }

    [Fact]
    public void AdminPolicyBridgeUsesConfiguredRole()
    {
        var options = new AuthorizationOptions();

        options.AddHPDBaseHPDAuthAdminPolicy(adminRoleName: "Owner");

        var policy = options.GetPolicy(HPDBasePolicies.Admin);
        policy.Should().NotBeNull();
        policy!.Requirements
            .OfType<RolesAuthorizationRequirement>()
            .Should()
            .Contain(requirement => requirement.AllowedRoles.Contains("Owner"));
    }

    private static void AddBaselineHPDAuthServiceDescriptors(IServiceCollection services)
    {
        services.AddScoped<ITenantContext, TestTenantContext>();
        AddThrowingDescriptor<UserManager<ApplicationUser>>(services);
        AddThrowingDescriptor<SignInManager<ApplicationUser>>(services);
        AddThrowingDescriptor<IAuditLogger>(services);
        AddThrowingDescriptor<ISessionManager>(services);
        AddThrowingDescriptor<IRefreshTokenStore>(services);
    }

    private static void AddThrowingDescriptor<TService>(IServiceCollection services)
        where TService : class
    {
        services.AddScoped<TService>(_ => throw new InvalidOperationException("This test only probes service registration."));
    }

    private sealed class TestTenantContext : ITenantContext
    {
        public Guid InstanceId => Guid.Empty;
    }
}
