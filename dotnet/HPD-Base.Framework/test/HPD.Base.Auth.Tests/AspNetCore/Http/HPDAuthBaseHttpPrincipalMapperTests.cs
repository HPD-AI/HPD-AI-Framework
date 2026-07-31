namespace HPD.Base.Auth.Tests.AspNetCore.Http;

public sealed class HPDAuthBaseHttpPrincipalMapperTests
{
    [Fact]
    public async Task MapsClaimsPrincipalAndUsesTenantContextFallback()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddScoped<ITenantContext>(_ => new TestTenantContext(Guid.Parse("11111111-1111-1111-1111-111111111111")));
        services.AddHPDBaseHPDAuthAspNetCore();
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("sub", "user-1"),
                new Claim("role", "Developer")
            ], "HPD"))
        };
        var mapper = provider.GetRequiredService<IBaseHttpPrincipalMapper>();

        var principal = await mapper.TryMapAsync(httpContext);

        principal.Should().NotBeNull();
        principal!.SubjectId.Should().Be("user-1");
        principal.CurrentTenantId.Should().Be("11111111-1111-1111-1111-111111111111");
        principal.Subjects.Should().Contain(subject => subject.Kind == AccessSubjectKind.Tenant);
    }

    [Fact]
    public async Task MapsAnonymousRequestWithoutTenantOrClaims()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddHPDBaseHPDAuthAspNetCore();
        await using var provider = services.BuildServiceProvider();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = provider,
            User = new ClaimsPrincipal(new ClaimsIdentity())
        };

        var principal = await provider.GetRequiredService<IBaseHttpPrincipalMapper>().TryMapAsync(httpContext);

        principal.Should().NotBeNull();
        principal!.AuthenticationState.Should().Be(PrincipalAuthenticationState.Anonymous);
        principal.Subjects.Should().ContainSingle(subject => subject.Kind == AccessSubjectKind.Anonymous);
    }

    [Fact]
    public async Task TenantContextFallbackCanBeDisabled()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddScoped<ITenantContext>(_ => new TestTenantContext(Guid.Parse("11111111-1111-1111-1111-111111111111")));
        services.AddHPDBaseHPDAuthAspNetCore(options => options.UseTenantContextFallback = false);
        await using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("sub", "user-1")
            ], "HPD"))
        };

        var principal = await provider.GetRequiredService<IBaseHttpPrincipalMapper>().TryMapAsync(httpContext);

        principal.Should().NotBeNull();
        principal!.CurrentTenantId.Should().BeNull();
    }

    [Fact]
    public async Task CustomEnricherCanAddSafePrincipalFacts()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton<IHPDAuthBaseHttpPrincipalEnricher, TestPrincipalEnricher>();
        services.AddHPDBaseHPDAuthAspNetCore();
        await using var provider = services.BuildServiceProvider();
        var httpContext = new DefaultHttpContext
        {
            RequestServices = provider,
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim("sub", "user-1")
            ], "HPD"))
        };

        var principal = await provider.GetRequiredService<IBaseHttpPrincipalMapper>().TryMapAsync(httpContext);

        principal.Should().NotBeNull();
        principal!.DisplayName.Should().Be("Enriched User");
        principal.Claims.Should().Contain(claim => claim.Type == "hpd.auth.test.enriched" && claim.Value == "true");
    }

    private sealed class TestTenantContext : ITenantContext
    {
        public TestTenantContext(Guid instanceId)
        {
            InstanceId = instanceId;
        }

        public Guid InstanceId { get; }
    }

    private sealed class TestPrincipalEnricher : IHPDAuthBaseHttpPrincipalEnricher
    {
        public ValueTask<PrincipalContext> EnrichAsync(
            HttpContext httpContext,
            PrincipalContext principal,
            CancellationToken cancellationToken = default)
        {
            _ = httpContext;
            cancellationToken.ThrowIfCancellationRequested();
            var claims = (principal.Claims ?? [])
                .Concat([new ClaimValue { Type = "hpd.auth.test.enriched", Value = "true" }])
                .ToArray();

            return ValueTask.FromResult(principal with
            {
                DisplayName = "Enriched User",
                Claims = claims
            });
        }
    }
}
