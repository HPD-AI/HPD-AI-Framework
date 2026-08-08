namespace HPD.Base.Auth.Tests.Principal;

public sealed class HPDBaseAuthSubjectProjectorTests
{
    [Fact]
    public void AnonymousPrincipalMapsToAnonymousSubject()
    {
        using var provider = Services().BuildServiceProvider();
        var mapper = provider.GetRequiredService<HPDBaseAuthSubjectProjector>();

        var mapped = mapper.Map(new ClaimsPrincipal(new ClaimsIdentity()));

        mapped.AuthenticationState.Should().Be(PrincipalAuthenticationState.Anonymous);
        mapped.SubjectKind.Should().Be(AccessSubjectKind.Anonymous);
        mapped.Subjects.Should().ContainSingle(subject => subject.Kind == AccessSubjectKind.Anonymous);
        mapped.AuthSource.Should().Be(HPDBaseAuthSources.Auth);
    }

    [Fact]
    public void AuthenticatedPrincipalMapsUserTenantRolesAdminAndRedactsSensitiveClaims()
    {
        using var provider = Services(options =>
        {
            options.AdminRoleNames = ["Admin"];
            options.CopiedClaimTypes = ["subscription_tier"];
        }).BuildServiceProvider();
        var mapper = provider.GetRequiredService<HPDBaseAuthSubjectProjector>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", "user-1"),
            new Claim("name", "Ada"),
            new Claim("role", "Admin"),
            new Claim("role", "Developer"),
            new Claim("instance_id", "tenant-1"),
            new Claim("sid", "session-1"),
            new Claim("refresh_token", "secret-value"),
            new Claim("subscription_tier", "enterprise")
        ], "HPD"));

        var mapped = mapper.Map(principal);

        mapped.AuthenticationState.Should().Be(PrincipalAuthenticationState.Admin);
        mapped.SubjectKind.Should().Be(AccessSubjectKind.Admin);
        mapped.SubjectId.Should().Be("user-1");
        mapped.DisplayName.Should().Be("Ada");
        mapped.CurrentTenantId.Should().Be("tenant-1");
        mapped.SessionId.Should().Be("session-1");
        mapped.Roles.Should().Contain(["Admin", "Developer"]);
        mapped.Subjects.Should().Contain(subject => subject.Kind == AccessSubjectKind.User && subject.Id == "user-1");
        mapped.Subjects.Should().Contain(subject => subject.Kind == AccessSubjectKind.Tenant && subject.Id == "tenant-1");
        mapped.Subjects.Should().Contain(subject => subject.Kind == AccessSubjectKind.Admin);
        mapped.Claims.Should().NotContain(claim => claim.Type == "refresh_token");
        mapped.Claims.Should().Contain(claim => claim.Type == "subscription_tier" && claim.Value == "enterprise");
    }

    [Fact]
    public void ServicePrincipalClaimMapsServiceSubject()
    {
        using var provider = Services().BuildServiceProvider();
        var mapper = provider.GetRequiredService<HPDBaseAuthSubjectProjector>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("client_id", "svc-1"),
            new Claim("instance_id", "tenant-1")
        ], "HPD"));

        var mapped = mapper.Map(principal);

        mapped.AuthenticationState.Should().Be(PrincipalAuthenticationState.Service);
        mapped.SubjectKind.Should().Be(AccessSubjectKind.ServicePrincipal);
        mapped.Subjects.Should().Contain(subject =>
            subject.Kind == AccessSubjectKind.ServicePrincipal
            && subject.Id == "svc-1"
            && subject.TenantId == "tenant-1");
    }

    [Fact]
    public void TenantFallbackIsUsedWhenTenantClaimIsAbsent()
    {
        using var provider = Services().BuildServiceProvider();
        var mapper = provider.GetRequiredService<HPDBaseAuthSubjectProjector>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", "user-1")
        ], "HPD"));

        var mapped = mapper.Map(principal, tenantIdFallback: "tenant-fallback");

        mapped.CurrentTenantId.Should().Be("tenant-fallback");
        mapped.Subjects.Should().Contain(subject => subject.Kind == AccessSubjectKind.Tenant && subject.Id == "tenant-fallback");
    }

    [Fact]
    public void CredentialIdMapsOnlyFromConfiguredSafeClaim()
    {
        using var provider = Services(options => options.CredentialIdClaimType = "credential_id").BuildServiceProvider();
        var mapper = provider.GetRequiredService<HPDBaseAuthSubjectProjector>();
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("sub", "user-1"),
            new Claim("credential_id", "cred-1")
        ], "HPD"));

        var mapped = mapper.Map(principal);

        mapped.CredentialId.Should().Be("cred-1");
        mapped.Claims.Should().BeNullOrEmpty();
    }

    private static ServiceCollection Services(Action<HPDBaseAuthOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddHPDBaseAuthServices(configure);
        return services;
    }
}
