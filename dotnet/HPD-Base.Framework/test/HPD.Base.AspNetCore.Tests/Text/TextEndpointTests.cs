using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using HPD.Base.AspNetCore;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace HPD.Base.AspNetCore.Tests.Text;

public sealed class TextEndpointTests
{
    [Fact]
    public async Task Text_route_executes_closed_query_and_rejects_unknown_members()
    {
        await using WebApplication app = await CreateAsync(); HttpClient client = app.GetTestClient(); client.DefaultRequestHeaders.Add("X-Test-Subject", "text-user");
        using var valid = new StringContent("""{"indexId":"http.text.content","query":{"kind":"term","value":"portable"},"take":4,"consistency":"current"}""", Encoding.UTF8, "application/json");
        HttpResponseMessage response = await client.PostAsync("/base/text/http_text/http.text.content/query", valid); string responseBody = await response.Content.ReadAsStringAsync(); response.StatusCode.Should().Be(HttpStatusCode.OK, responseBody); using JsonDocument json = JsonDocument.Parse(responseBody); json.RootElement.GetProperty("matches").GetArrayLength().Should().Be(1);
        using var invalid = new StringContent("""{"indexId":"http.text.content","query":{"kind":"term","value":"portable","nativeSyntax":"*"},"take":4,"consistency":"current"}""", Encoding.UTF8, "application/json");
        (await client.PostAsync("/base/text/http_text/http.text.content/query", invalid)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Client_generation_omits_text_index_without_exact_current_grant()
    {
        await using WebApplication app = await CreateAsync(); HttpClient client = app.GetTestClient(); client.DefaultRequestHeaders.Add("X-Test-Subject", "other-user"); HttpResponseMessage response = await client.GetAsync("/base/client-generation"); response.StatusCode.Should().Be(HttpStatusCode.OK); string body = await response.Content.ReadAsStringAsync(); body.Should().Contain("\"textIndexes\":[]").And.NotContain("http.text.content");
    }

    private static async Task<WebApplication> CreateAsync()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" }); builder.WebHost.UseTestServer(); builder.Services.AddAuthorization(options => options.AddPolicy("application", policy => policy.RequireAssertion(static _ => true)));
        builder.Services.AddHPDBase(baseBuilder =>
        {
            baseBuilder.AddPolicyAuthority<AllowPolicy>(new() { Id = "http.text.policy", Version = 1, OwningModuleId = "tests", EvaluatorContractId = "http.text.policy.v1", EvaluatorContractVersion = 1, CompositionOrder = 0 });
            baseBuilder.AddStaticGrantAuthority(new() { Id = BaseTextGrants.Query, Version = 1, OwningModuleId = "tests", SourceContractId = "http.text.grants", SourceContractVersion = 1 }, new() { Id = BaseTextGrants.Query, ApplicationId = "hpd.base.application", Audience = HPDBaseEndpointAudience.Application, Subject = new() { Kind = AccessSubjectKind.User, Id = "text-user" }, Action = BaseTextGrants.Query, Scope = new() { Kind = ResourceScopeKind.TextIndex, CollectionId = "http_text", TextIndexId = "http.text.content" } });
            baseBuilder.AddCollection(HttpTextDocument.Collection);
        });
        builder.Services.AddHPDBaseAspNetCore(); builder.Services.Replace(ServiceDescriptor.Singleton<IBaseHttpPrincipalMapper, HeaderPrincipalMapper>()); WebApplication app = builder.Build(); app.UseAuthorization(); RouteGroupBuilder group = app.MapHPDBaseApplicationApi(new() { AuthorizationPolicy = "application", MapRecords = true, MapClientGeneration = true }); group.MapHPDBaseTextApplicationApi(); await app.StartAsync(); (await app.Services.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
        BaseCollectionSession<HttpTextDocument> collection = app.Services.GetRequiredService<IBaseSessionFactory>().For(new() { AuthenticationState = PrincipalAuthenticationState.Admin }).Collection(HttpTextDocument.Collection); (await collection.CreateAsync(new("one"), new() { Title = "Portable search", State = "published" })).RequireValue(); return app;
    }
    private sealed class HeaderPrincipalMapper : IBaseHttpPrincipalMapper
    {
        public ValueTask<PrincipalContext> MapAsync(Microsoft.AspNetCore.Http.HttpContext context, HPDBaseEndpointDescriptor endpoint, CancellationToken cancellationToken = default) { string id = context.Request.Headers["X-Test-Subject"].ToString(); return ValueTask.FromResult(new PrincipalContext { AuthenticationState = PrincipalAuthenticationState.Authenticated, SubjectKind = AccessSubjectKind.User, SubjectId = id }); }
    }
}

[BaseCollection("http_text", typeof(HttpTextJsonContext))]
[BaseTextIndex("http.text.content", Fields = [nameof(Title)], Weights = [4], FilterFields = [nameof(State)])]
public partial record HttpTextDocument { [BaseField("http.text.title")] public required string Title { get; init; } [BaseField("http.text.state")] public required string State { get; init; } }
[JsonSerializable(typeof(HttpTextDocument))] public partial class HttpTextJsonContext : JsonSerializerContext;
public sealed class AllowPolicy : IPolicyEvaluator { public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default) => ValueTask.FromResult(PolicyDecision.Allow()); }
