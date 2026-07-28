using HPD.Base.Auth.HPDAuth.AspNetCore.Health;
using HPD.Base.Tests.Observability;

namespace HPD.Base.Auth.HPDAuth.AspNetCore.Tests.Observability;

public sealed class HPDAuthAspNetCoreLoggingTests
{
    [Fact]
    public void HostStatusActivationEmitsOneBoundedIntegrationWarning()
    {
        using var collector = new LogCollector();
        var services = Services(collector);
        using var provider = services.BuildServiceProvider();

        var status = provider.GetRequiredService<IEnumerable<IHPDAuthBaseHostIntegrationStatus>>().Single();

        status.HPDAuthServicesDetected.Should().BeFalse();
        AssertContract(collector.Records.Should().ContainSingle().Subject, 6500);
        AssertSafe(
            collector,
            "UserManager<ApplicationUser>",
            "SignInManager<ApplicationUser>",
            nameof(IAuditLogger),
            nameof(ISessionManager));
    }

    [Fact]
    public async Task EnricherFailureHasOneAggregateOwnerAndLeaksNoIdentity()
    {
        using var collector = new LogCollector();
        var services = Services(collector);
        services.AddSingleton<IHPDAuthBaseHttpPrincipalEnricher, ThrowingEnricher>();
        await using var provider = services.BuildServiceProvider();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = provider,
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("sub", "private-subject-id"),
                new Claim("role", "private-role"),
                new Claim("token", "private-token")
            ], "HPD"))
        };

        var action = async () =>
            await provider.GetRequiredService<IBaseHttpPrincipalMapper>().TryMapAsync(httpContext);

        await action.Should().ThrowAsync<InvalidOperationException>();
        AssertContract(collector.Records.Should().ContainSingle().Subject, 6502);
        AssertSafe(collector, "private-subject-id", "private-role", "private-token", ThrowingEnricher.Secret);
    }

    [Fact]
    public async Task SuccessfulMappingAndEnrichmentEmitNoLogs()
    {
        using var collector = new LogCollector();
        var services = Services(collector);
        services.AddSingleton<IHPDAuthBaseHttpPrincipalEnricher, PassingEnricher>();
        await using var provider = services.BuildServiceProvider();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = provider,
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "private-subject-id")], "HPD"))
        };

        var principal = await provider.GetRequiredService<IBaseHttpPrincipalMapper>().TryMapAsync(httpContext);

        principal.Should().NotBeNull();
        collector.Records.Should().BeEmpty();
    }

    private static ServiceCollection Services(LogCollector collector)
    {
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.Trace).AddProvider(collector));
        services.AddHPDBaseHPDAuthAspNetCore(configureCore: options => options.RequireHPDAuthServices = false);
        return services;
    }

    private static void AssertContract(CapturedLogRecord record, int eventId)
    {
        var contract = HPDBaseLogEventRegistry.Active.Single(candidate =>
            candidate.Owner == "HPD.Base.Auth.HPDAuth.AspNetCore" && candidate.Id == eventId);
        record.EventId.Id.Should().Be(contract.Id);
        record.EventId.Name.Should().Be(contract.Name);
        record.Level.Should().Be(contract.Level);
        record.OriginalFormat.Should().Be(contract.Template);
        record.State.Where(property => property.Key != "{OriginalFormat}")
            .Select(property => property.Key)
            .Should().Equal(contract.Properties);
    }

    private static void AssertSafe(LogCollector collector, params string[] markers)
    {
        LogSafetyInspector.AssertSafe(collector.Records, markers);
        LogSafetyInspector.AssertNoExceptions(collector.Records);
        LogSafetyInspector.AssertNoScopes(collector.Records);
    }

    private sealed class ThrowingEnricher : IHPDAuthBaseHttpPrincipalEnricher
    {
        public const string Secret = "private-enricher-exception-message";

        public ValueTask<PrincipalContext> EnrichAsync(
            HttpContext httpContext,
            PrincipalContext principal,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException(Secret);
    }

    private sealed class PassingEnricher : IHPDAuthBaseHttpPrincipalEnricher
    {
        public ValueTask<PrincipalContext> EnrichAsync(
            HttpContext httpContext,
            PrincipalContext principal,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(principal);
    }
}
