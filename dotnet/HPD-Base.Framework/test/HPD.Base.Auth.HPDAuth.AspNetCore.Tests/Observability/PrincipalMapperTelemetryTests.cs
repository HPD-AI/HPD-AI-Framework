using System.Collections.Concurrent;
using System.Diagnostics;
using HPD.Base.Auth.HPDAuth.AspNetCore.Observability;
using HPD.Base.Observability;
using HPD.Base.Tests.Observability;

namespace HPD.Base.Auth.HPDAuth.AspNetCore.Tests.Observability;

public sealed class PrincipalMapperTelemetryTests
{
    [Fact]
    public async Task PrincipalMappingAndEnrichmentTelemetryDoNotLeakClaimsRolesOrIdentityMarkers()
    {
        using var activities = new ActivityCollector(HPDBaseActivitySourceNames.HPDAuthAspNetCore);
        using var metrics = new MeterCollector(HPDBaseMeterNames.HPDAuthAspNetCore);
        var services = new ServiceCollection();
        services.AddScoped<ITenantContext>(_ => new TestTenantContext(Guid.Parse("11111111-1111-1111-1111-111111111111")));
        services.AddSingleton<IHPDAuthBaseHttpPrincipalEnricher, SecretPrincipalEnricher>();
        services.AddHPDBaseHPDAuthAspNetCore();
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("sub", "subject-secret"),
                new Claim("role", "role-secret"),
                new Claim("email", "email-secret"),
                new Claim("tenant", "tenant-secret"),
                new Claim("token", "token-secret")
            ], "HPD"))
        };

        var principal = await provider.GetRequiredService<IBaseHttpPrincipalMapper>().TryMapAsync(httpContext);

        principal.Should().NotBeNull();
        activities.Names.Should().Contain(HPDBaseTelemetrySpans.AuthPrincipalMap);
        activities.Names.Should().Contain(HPDBaseTelemetrySpans.AuthPrincipalEnrich);
        metrics.InstrumentNames.Should().Contain(HPDBaseTelemetryInstruments.AuthPrincipalMaps);
        metrics.InstrumentNames.Should().Contain(HPDBaseTelemetryInstruments.AuthPrincipalMapDuration);

        var forbidden = new[]
        {
            "subject-secret",
            "tenant-secret",
            "role-secret",
            "claim-secret",
            "token-secret",
            "email-secret",
            "display-secret",
            "credential-secret",
            "session-secret",
            "11111111-1111-1111-1111-111111111111"
        };
        activities.Stopped.Should().NotContain(activity => TagValues(activity).Any(value => forbidden.Any(marker => value.Contains(marker, StringComparison.Ordinal))));
    }

    private static string[] TagValues(Activity activity) =>
        activity.TagObjects.Select(tag => Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty).ToArray();

    private sealed class TestTenantContext : ITenantContext
    {
        public TestTenantContext(Guid instanceId)
        {
            InstanceId = instanceId;
        }

        public Guid InstanceId { get; }
    }

    private sealed class SecretPrincipalEnricher : IHPDAuthBaseHttpPrincipalEnricher
    {
        public ValueTask<PrincipalContext> EnrichAsync(
            HttpContext httpContext,
            PrincipalContext principal,
            CancellationToken cancellationToken = default)
        {
            _ = httpContext;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(principal with
            {
                DisplayName = "display-secret",
                SessionId = "session-secret",
                CredentialId = "credential-secret",
                Claims = [new ClaimValue { Type = "custom", Value = "claim-secret" }]
            });
        }
    }

}
