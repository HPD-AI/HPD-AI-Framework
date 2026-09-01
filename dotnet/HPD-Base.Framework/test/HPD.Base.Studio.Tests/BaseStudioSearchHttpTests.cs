using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using HPD.AI.Platform;
using HPD.AI.Platform.Studio;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Base.Studio.Tests;

/// <summary>Exercises Search through the authenticated, lease-pinned Studio HTTP dispatcher.</summary>
public sealed class BaseStudioSearchHttpTests
{
    /// <summary>Proves Inspect mode does not enumerate command endpoints even with a valid bootstrap lease.</summary>
    [Fact]
    public async Task Inspect_bootstrap_blocks_search_command_dispatch_with_404()
    {
        await using WebApplication app = Build(BaseStudioMode.Inspect);
        Bootstrap lease = await BootstrapAsync(app);
        Assert.DoesNotContain(lease.Body.GetProperty("commands").EnumerateArray(),
            static value => value.GetProperty("commandId").GetString() == "vectorIndex.rebuild");

        Assert.DoesNotContain(((IEndpointRouteBuilder)app).DataSources.SelectMany(static source => source.Endpoints).OfType<RouteEndpoint>(),
            static endpoint => endpoint.RoutePattern.RawText == "/studio/base/studio/commands/vectorIndex.rebuild/preview");
    }

    /// <summary>Proves malformed and substituted Search inputs fail at the real L41/lease dispatcher boundary.</summary>
    [Theory]
    [MemberData(nameof(HostileQueryBodies))]
    public async Task Search_query_http_dispatch_rejects_hostile_canonical_inputs(string body)
    {
        await using WebApplication app = Build(BaseStudioMode.Operate);
        Bootstrap lease = await BootstrapAsync(app);
        Assert.Contains(lease.Body.GetProperty("contractMap").GetProperty("methods").EnumerateArray(),
            static value => value.GetProperty("registeredMethodId").GetString() == "base.studio.view.base.search.query.results.list");

        Response response = await PostAsync(app, "/studio/base/studio/views/base.search.query.results.list",
            "base.studio.view.base.search.query.results.list", lease.Snapshot, body);

        Assert.Equal(StatusCodes.Status400BadRequest, response.Status);
    }

    /// <summary>Gets hostile vectors that are structurally close but violate closed Search authority.</summary>
    public static IEnumerable<object[]> HostileQueryBodies()
    {
        string resource = ResourceJson(new BaseStudioVectorIndexResource("sample.application", "documents", "vector.documents", 1));
        yield return [Request(resource, "{\"components\":[0,\"NaN\"],\"dimensions\":2,\"kind\":\"vector\"}", "null", "[]", 10, "null")];
        yield return [Request(resource, "{\"components\":[0,\"Infinity\"],\"dimensions\":2,\"kind\":\"vector\"}", "null", "[]", 10, "null")];
        yield return [Request(resource, "{\"components\":[0],\"dimensions\":2,\"kind\":\"vector\"}", "null", "[]", 10, "null")];
        yield return [Request(resource, "{\"kind\":\"invented\",\"text\":\"x\"}", "null", "[]", 10, "null")];
        yield return [Request(resource, "{\"kind\":\"term\",\"text\":\"x\"}", "{\"kind\":\"equal\",\"field\":\"tenant\"}", "[]", 10, "null")];
        yield return [Request(resource, "{\"kind\":\"term\",\"text\":\"x\"}", "null", "[{\"direction\":\"sideways\",\"field\":\"tenant\",\"nulls\":\"first\"}]", 10, "null")];
        yield return [Request(resource, "{\"kind\":\"term\",\"text\":\"x\"}", "null", "[]", 501, "null")];
        yield return [Request(resource, "{\"kind\":\"term\",\"text\":\"x\"}", "null", "[]", 10, "\"substituted-cursor\"")];
    }

    private static string Request(string resource, string query, string filter, string order, int take, string after)
        => $"{{\"after\":{after},\"filter\":{filter},\"order\":{order},\"query\":{query},\"resource\":{resource},\"take\":{take}}}";

    private static string ResourceJson(BaseStudioVectorIndexResource value) => JsonSerializer.Serialize(new
    {
        applicationId = value.ApplicationId, authorityChecksum = Hex(value.AuthorityChecksum), collectionId = value.CollectionId,
        indexId = value.IndexId, indexVersion = value.IndexVersion, kind = "vectorIndex",
    });

    private static WebApplication Build(BaseStudioMode mode)
    {
        WebApplicationBuilder host = WebApplication.CreateBuilder(); host.Services.AddLogging();
        host.Services.AddHPDBase(builder =>
        {
            builder.ConfigureSchema(static options => options.ApplicationId = "sample.application");
            builder.ConfigureInMemoryStore(static _ => { }).AddCollection(SearchHttpDocument.Collection);
            builder.AddPolicyAuthority(new BasePolicyAuthorityDefinition { Id = "search.http.policy", Version = 1, OwningModuleId = "tests",
                EvaluatorContractId = "search.http.allow", EvaluatorContractVersion = 1, CompositionOrder = 0 }, new AllowPolicy());
            foreach (string operation in new[] { "base.studio.action.discover", "base.studio.action.execute", "base.studio.action.preview",
                "base.studio.bootstrap.read", "base.studio.diagnostics.inspect", "base.studio.invalidation.subscribe",
                "base.studio.receipt.discover", "base.studio.receipt.inspect", "base.studio.resource.discover",
                "base.studio.resource.inspect", "base.studio.resource.links", "base.studio.resource.search" })
                builder.AddStaticGrantAuthority(new BaseGrantAuthorityDefinition { Id = operation, Version = 1, OwningModuleId = "base", SourceContractId = "base.studio.fixed-grant", SourceContractVersion = 1 },
                    new AccessGrant { Id = operation, ApplicationId = "sample.application", ModuleId = "base", Audience = HPDBaseEndpointAudience.ControlPlane,
                        Subject = new AccessSubject { Kind = AccessSubjectKind.User, Id = "operator" }, Action = operation,
                        Scope = new ResourceScope { Kind = ResourceScopeKind.Runtime } });
        });
        host.Services.AddSingleton(static services => Assert.IsAssignableFrom<IBaseStudioDynamicStoreAuthoritySource>(services.GetRequiredService<IAtomicRecordStore>()));
        host.Services.AddHPDAIPlatform().AddStudioAuthentication(static _ => new Authentication())
            .AddBaseStudio(static _ => new PrincipalResolver(), options => options.Mode = mode);
        WebApplication app = host.Build(); app.MapHPDAIPlatform(); return app;
    }

    private static async Task<Bootstrap> BootstrapAsync(WebApplication app)
    {
        Response shell = await InvokeAsync(app, Endpoint(app, "/studio/control/shell"), "{}", null, null);
        using JsonDocument descriptor = JsonDocument.Parse(shell.Body);
        JsonElement root = descriptor.RootElement;
        string body = JsonSerializer.Serialize(new { shellContractChecksum = root.GetProperty("shellContractChecksum").GetString(),
            editionAssetGraphChecksum = root.GetProperty("editionAssetGraphChecksum").GetString(), runtimeClientChecksum = root.GetProperty("runtimeClientChecksum").GetString(),
            locale = "en-US", clientCapabilities = new[] { 1, 2 } });
        Response response = await InvokeAsync(app, Endpoint(app, "/studio/control/bootstrap"), body, null, null);
        Assert.Equal(StatusCodes.Status200OK, response.Status);
        JsonDocument document = JsonDocument.Parse(response.Body); JsonElement owned = document.RootElement.Clone(); document.Dispose();
        return new Bootstrap(owned, owned.GetProperty("snapshotChecksum").GetString()!);
    }

    private static Task<Response> PostAsync(WebApplication app, string route, string method, string snapshot, string body)
        => InvokeAsync(app, Endpoint(app, route), body, method, snapshot);

    private static RouteEndpoint Endpoint(WebApplication app, string route) => Assert.IsType<RouteEndpoint>(((IEndpointRouteBuilder)app).DataSources
        .SelectMany(static source => source.Endpoints).Single(value => value is RouteEndpoint endpoint && endpoint.RoutePattern.RawText == route));

    private static async Task<Response> InvokeAsync(WebApplication app, RouteEndpoint endpoint, string body, string? method, string? snapshot)
    {
        var context = new DefaultHttpContext { RequestServices = app.Services };
        context.Request.ContentType = "application/json"; byte[] bytes = Encoding.UTF8.GetBytes(body);
        context.Request.Body = new MemoryStream(bytes); context.Request.ContentLength = bytes.Length; context.Response.Body = new MemoryStream();
        if (method is not null) context.Request.Headers["X-HPD-Studio-Method"] = method;
        if (snapshot is not null) context.Request.Headers["X-HPD-Studio-Snapshot"] = snapshot;
        await endpoint.RequestDelegate!(context);
        return new Response(context.Response.StatusCode, Encoding.UTF8.GetString(((MemoryStream)context.Response.Body).ToArray()));
    }

    private static string Hex(BaseStudioSha256 value) => Convert.ToHexString(value.ToArray()).ToLowerInvariant();
    private sealed record Bootstrap(JsonElement Body, string Snapshot);
    private sealed record Response(int Status, string Body);

    private sealed class PrincipalResolver : IBaseStudioPrincipalContextResolver
    {
        public ValueTask<PrincipalContext?> ResolveAsync(HttpContext context, BaseStudioSessionObservation session, CancellationToken token) => ValueTask.FromResult<PrincipalContext?>(new() { AuthenticationState = PrincipalAuthenticationState.Authenticated, SubjectKind = AccessSubjectKind.User, SubjectId = "operator" });
        public ValueTask<BaseOwnedSubjectScopeEvidence?> ResolveScopeAsync(HttpContext context, BaseStudioSessionObservation session, CancellationToken token) => ValueTask.FromResult<BaseOwnedSubjectScopeEvidence?>(null);
    }

    private sealed class AllowPolicy : IPolicyEvaluator
    {
        public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken token = default)
            => ValueTask.FromResult(PolicyDecision.Allow());
    }

    private sealed class Authentication : IBaseStudioAuthenticationIntegration
    {
        public BaseStudioAuthenticationDescriptor Descriptor { get; } = BaseStudioAuthenticationDescriptor.Create("tests.auth", 1, BaseStudioAuthenticationKind.Bearer, "/auth/login", "/auth/callback", "/auth/logout", "/auth/session", ["https://studio.example/"], null, null, TimeSpan.FromHours(1), false, [BaseStudioFreshAuthenticationClass.MultiFactor]);
        public ValueTask<BaseStudioAuthenticationResult<BaseStudioSessionObservation>> ObserveSessionAsync(HttpContext context, CancellationToken token) => ValueTask.FromResult(BaseStudioAuthenticationResult<BaseStudioSessionObservation>.Success(Session()));
        public ValueTask<BaseStudioAuthenticationResult<BaseStudioProtectedReturnTarget>> ProtectReturnTargetAsync(HttpContext context, string? target, CancellationToken token) => ValueTask.FromResult(BaseStudioAuthenticationResult<BaseStudioProtectedReturnTarget>.Failed(BaseStudioAuthenticationFailure.IntegrationUnavailable));
        public ValueTask BeginSignInAsync(HttpContext context, BaseStudioProtectedReturnTarget target, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask CompleteCallbackAsync(HttpContext context, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask BeginSignOutAsync(HttpContext context, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask<BaseStudioAuthenticationResult<BaseStudioTransportAuthorization>> AuthorizeRequestAsync(HttpContext context, BaseStudioTransportPurpose purpose, CancellationToken token) { BaseStudioSessionObservation session = Session(); return ValueTask.FromResult(BaseStudioAuthenticationResult<BaseStudioTransportAuthorization>.Success(BaseStudioTransportAuthorization.Create(session, purpose, session.IssuedAtUtc.AddMinutes(1)))); }
        public async ValueTask<BaseStudioAuthenticationResult<BaseStudioBrowserAuthorization>> AcquireBrowserAuthorizationAsync(HttpContext context, BaseStudioTransportPurpose purpose, CancellationToken token) { var result = await AuthorizeRequestAsync(context, purpose, token); return BaseStudioAuthenticationResult<BaseStudioBrowserAuthorization>.Success(BaseStudioBrowserAuthorization.Create("X-HPD-Test", "authority", result.Value!)); }
        public ValueTask<BaseStudioAuthenticationResult<BaseStudioFreshAuthenticationResult>> AcquireFreshAuthenticationAsync(HttpContext context, BaseStudioFreshAuthenticationRequest request, CancellationToken token) => ValueTask.FromResult(BaseStudioAuthenticationResult<BaseStudioFreshAuthenticationResult>.Success(new BaseStudioFreshAuthenticationResult.Unsupported()));
        public ValueTask<BaseStudioAuthenticationResult<BaseStudioFreshAuthenticationResult>> CompleteFreshAuthenticationAsync(HttpContext context, BaseStudioFreshAuthenticationContinuation continuation, CancellationToken token) => ValueTask.FromResult(BaseStudioAuthenticationResult<BaseStudioFreshAuthenticationResult>.Success(new BaseStudioFreshAuthenticationResult.Unsupported()));
        private BaseStudioSessionObservation Session() { DateTimeOffset now = DateTimeOffset.UtcNow; return BaseStudioSessionObservation.Create(1, BaseStudioSha256.FromDigest(new byte[32]), "control-plane", BaseStudioSha256.FromDigest(Enumerable.Repeat((byte)1, 32).ToArray()), now, now.AddMinutes(5), Descriptor.Checksum); }
    }
}

[BaseCollection("documents", typeof(SearchHttpJsonContext))]
[BaseTextIndex("text.documents", Fields = [nameof(SearchHttpDocument.Title)], Weights = [1])]
[BaseVectorIndex("vector.documents", nameof(SearchHttpDocument.Embedding), VectorSpace = "search.http.v1", Dimensions = 2)]
public partial record SearchHttpDocument
{
    [BaseField("search.http.title")] public required string Title { get; init; }
    [BaseField("search.http.embedding", Operators = BaseFieldOperator.None)] public required BaseVector Embedding { get; init; }
}

[JsonSerializable(typeof(SearchHttpDocument))]
public partial class SearchHttpJsonContext : JsonSerializerContext;
