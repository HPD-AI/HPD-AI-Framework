using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using HPD.Base;

namespace HPD.Base.Auth.Tests.AspNetCore.Integration;

public sealed class AdminEndpointGatingTests
{
    [Fact]
    public async Task AdminMetadataEndpointUsesHPDAuthAdminPolicyBridge()
    {
        await using var app = await CreateAppAsync();
        var client = app.GetTestClient();

        var anonymous = await client.GetAsync("/base/admin/manifest");
        using var userRequest = new HttpRequestMessage(HttpMethod.Get, "/base/admin/manifest");
        userRequest.Headers.Add(TestAuthHandler.RoleHeaderName, "User");
        var user = await client.SendAsync(userRequest);
        using var adminRequest = new HttpRequestMessage(HttpMethod.Get, "/base/admin/manifest");
        adminRequest.Headers.Add(TestAuthHandler.RoleHeaderName, "Admin");
        var admin = await client.SendAsync(adminRequest);

        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        user.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        admin.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task AdminPolicyExplainEndpointUsesHPDAuthAdminPolicyBridgeAndServiceGate()
    {
        await using var app = await CreateAppAsync(options => options.MapAdminPolicyExplain = true);
        var client = app.GetTestClient();

        var anonymous = await client.PostAsJsonAsync("/base/admin/policy/explain", ExplainRequest());
        using var userRequest = new HttpRequestMessage(HttpMethod.Post, "/base/admin/policy/explain")
        {
            Content = JsonContent.Create(ExplainRequest())
        };
        userRequest.Headers.Add(TestAuthHandler.RoleHeaderName, "User");
        var user = await client.SendAsync(userRequest);
        using var adminRequest = new HttpRequestMessage(HttpMethod.Post, "/base/admin/policy/explain")
        {
            Content = JsonContent.Create(ExplainRequest())
        };
        adminRequest.Headers.Add(TestAuthHandler.RoleHeaderName, "Admin");
        var admin = await client.SendAsync(adminRequest);

        anonymous.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        user.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        admin.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    private static BasePolicyExplainRequest ExplainRequest() => new()
    {
        Operation = BasePolicyExplainOperation.Query,
        CollectionId = "items"
    };

    private static async Task<WebApplication> CreateAppAsync(Action<HPD.Base.AspNetCore.HPDBaseEndpointOptions>? configureEndpoints = null)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication(TestAuthHandler.SchemeName)
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>(TestAuthHandler.SchemeName, _ => { });
        builder.Services.AddAuthorization(options => options.AddHPDBaseHPDAuthAdminPolicy());
        builder.Services.AddSingleton<IPolicyEvaluator, AllowPolicyEvaluator>();
        builder.Services.AddHPDBaseHPDAuthAspNetCore(configureCore: options => options.RequireHPDAuthServices = false);
        builder.Services.AddHPDBaseRuntime()
            .AddHPDBaseAspNetCore()
            .AddHPDBaseInMemoryStore(options =>
            {
                options.StoreId = "primary";
                options.CollectionIds = ["items"];
                options.Collections = [Collection()];
            });

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.Services.GetRequiredService<IRecordStoreRegistry>().AddHPDBaseInMemoryStore(app.Services);
        await app.Services.GetRequiredService<IBaseDescriptorRegistry>().RebuildAsync();
        app.MapHPDBaseApi(configureEndpoints);
        await app.StartAsync();
        return app;
    }

    private static CollectionDefinition Collection() => new()
    {
        Id = "items",
        Name = "items",
        Kind = BaseCollectionKinds.Document,
        SchemaMode = SchemaMode.Loose,
        UnknownFields = UnknownFieldPolicy.Preserve
    };

    private sealed class AllowPolicyEvaluator : IPolicyEvaluator
    {
        public ValueTask<PolicyDecision> EvaluateAsync(
            PolicyEvaluationRequest request,
            CancellationToken cancellationToken = default)
        {
            _ = request;
            cancellationToken.ThrowIfCancellationRequested();
            return ValueTask.FromResult(new PolicyDecision
            {
                Effect = PolicyEffect.Allow,
                Outcome = PolicyOutcome.Allowed
            });
        }
    }

    private sealed class TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "Test";
        public const string RoleHeaderName = "x-test-role";

        public TestAuthHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue(RoleHeaderName, out var role))
                return Task.FromResult(AuthenticateResult.NoResult());

            var claims = new List<Claim>
            {
                new("sub", "user-1"),
                new(ClaimTypes.Role, role.ToString())
            };
            var identity = new ClaimsIdentity(claims, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }
    }
}
