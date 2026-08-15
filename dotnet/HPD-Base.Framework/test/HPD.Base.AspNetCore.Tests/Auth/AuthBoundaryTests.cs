using System.Security.Claims;
using HPD.Base.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HPD.Base.AspNetCore.Tests.Auth;

public sealed class AuthBoundaryTests
{
    [Fact]
    public async Task AnonymousPrincipalMapsConservatively()
    {
        await using var app = await TestBaseApp.CreateAsync();
        var principal = await app.Services.GetRequiredService<IBaseHttpPrincipalContextFactory>()
            .CreateAsync(Context(HPDBaseEndpointAudience.Public));

        principal.AuthenticationState.Should().Be(PrincipalAuthenticationState.Anonymous);
        principal.SubjectKind.Should().Be(AccessSubjectKind.Anonymous);
    }

    [Fact]
    public async Task CustomPrincipalMapperCanOverrideDefault()
    {
        await using var app = await TestBaseApp.CreateAsync(configureServices: services =>
            services.Replace(ServiceDescriptor.Singleton<IBaseHttpPrincipalMapper, FixedPrincipalMapper>()));

        var principal = await app.Services.GetRequiredService<IBaseHttpPrincipalContextFactory>()
            .CreateAsync(Context(HPDBaseEndpointAudience.Application));

        principal.SubjectId.Should().Be("mapped");
        principal.AuthSource.Should().Be("test");
    }

    [Fact]
    public async Task GenericMapperRejectsConflictingSubjectAndRoleOverflow()
    {
        await using var app = await TestBaseApp.CreateAsync(options => options.Auth.MaxRoles = 1);
        var factory = app.Services.GetRequiredService<IBaseHttpPrincipalContextFactory>();
        var subjectConflict = Context(HPDBaseEndpointAudience.Application);
        subjectConflict.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "one"), new Claim("sub", "two")], "test"));
        var roleOverflow = Context(HPDBaseEndpointAudience.Application);
        roleOverflow.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("role", "one"), new Claim("role", "two")], "test"));

        await ((Func<Task>)(async () => await factory.CreateAsync(subjectConflict))).Should().ThrowAsync<InvalidOperationException>();
        await ((Func<Task>)(async () => await factory.CreateAsync(roleOverflow))).Should().ThrowAsync<InvalidOperationException>();
    }

    [Fact]
    public async Task OperationContextCopiesSafeRequestMetadata()
    {
        await using var app = await TestBaseApp.CreateAsync(options =>
        {
            options.RequestContext.IncludeIpAddress = true;
            options.RequestContext.IncludeUserAgent = true;
        });

        var httpContext = new DefaultHttpContext();
        httpContext.SetEndpoint(Endpoint(HPDBaseEndpointAudience.Application));
        httpContext.Request.Method = "GET";
        httpContext.Request.Headers["X-Correlation-ID"] = "corr";
        httpContext.Request.Headers["X-HPD-Client"] = "client";
        httpContext.Request.Headers["X-HPD-Client-Version"] = "1";
        httpContext.Request.Headers.UserAgent = "agent";
        httpContext.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "u1"), new Claim("tenant_id", "t1")], "test"));

        var principal = await app.Services.GetRequiredService<IBaseHttpPrincipalContextFactory>().CreateAsync(httpContext);
        var operation = app.Services.GetRequiredService<IBaseHttpOperationContextFactory>().Create(httpContext, principal, BaseOperationKind.Get, "items", "id");

        operation.TenantId.Should().Be("t1");
        operation.CorrelationId.Should().Be("corr");
        operation.Request!.ClientName.Should().Be("client");
        operation.Request.ClientVersion.Should().Be("1");
        operation.Request.UserAgent.Should().Be("agent");
        operation.Request.Redacted.Should().BeTrue();
    }

    private static DefaultHttpContext Context(HPDBaseEndpointAudience audience)
    {
        var context = new DefaultHttpContext();
        context.SetEndpoint(Endpoint(audience));
        return context;
    }

    private static Endpoint Endpoint(HPDBaseEndpointAudience audience) => new(
        static _ => Task.CompletedTask,
        new EndpointMetadataCollection(new HPDBaseEndpointDescriptor
        {
            EndpointId = "base.test",
            Audience = audience,
            Operation = HPDBaseEndpointOperation.RecordRead,
            Capability = audience == HPDBaseEndpointAudience.Public ? null : HPDBaseCapabilities.RecordsRead
        }),
        "base.test");

    private sealed class FixedPrincipalMapper : IBaseHttpPrincipalMapper
    {
        public ValueTask<PrincipalContext> MapAsync(HttpContext httpContext, HPDBaseEndpointDescriptor endpoint, CancellationToken cancellationToken = default)
        {
            _ = httpContext;
            _ = endpoint;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new PrincipalContext
            {
                AuthenticationState = PrincipalAuthenticationState.Authenticated,
                SubjectId = "mapped",
                SubjectKind = AccessSubjectKind.User,
                AuthSource = "test"
            });
        }
    }
}
