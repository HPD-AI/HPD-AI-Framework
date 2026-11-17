using System.Net;
using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using HPD.AI.Platform.Studio;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Auth.Studio.Tests;

public sealed class HPDAuthStudioAuthenticationTests : IAsyncLifetime
{
    private WebApplication _app = null!;
    private HttpClient _client = null!;
    private readonly ChallengeState _challenge = new();

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options => options.Cookie.SecurePolicy = CookieSecurePolicy.None);
        builder.Services.AddSingleton(new HPDAuthStudioOptions
        {
            AllowedOrigins = ["https://studio.example"],
        });
        builder.Services.AddSingleton<IStudioAuthenticationIntegration>(services =>
            new HPDAuthStudioAuthenticationIntegration(services,
                services.GetRequiredService<HPDAuthStudioOptions>()));
        _app = builder.Build();
        _app.MapGet("/login", async context =>
        {
            bool mfa = context.Request.Query["mfa"] == "true";
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, "8f3ba59a-a536-4379-9fd9-c898ac3900c9"),
                new("session_id", "studio-session-1"),
                new("AspNet.Identity.SecurityStamp", "security-stamp-1"),
                new(ClaimTypes.Role, "studio-operator"),
                new("instance_id", Guid.Empty.ToString()),
            };
            if (mfa) claims.Add(new Claim("amr", "pwd mfa"));
            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,
                new ClaimsPrincipal(identity), new AuthenticationProperties
                {
                    IssuedUtc = now,
                    ExpiresUtc = now.AddHours(1),
                });
            context.Response.StatusCode = StatusCodes.Status204NoContent;
        });
        _app.MapGet("/session", async context =>
        {
            IStudioAuthenticationIntegration integration =
                context.RequestServices.GetRequiredService<IStudioAuthenticationIntegration>();
            StudioAuthenticationResult<StudioSessionObservation> result =
                await integration.ObserveSessionAsync(context, context.RequestAborted);
            context.Response.StatusCode = result.IsSuccess ? 200 : 401;
            await context.Response.WriteAsJsonAsync(new
            {
                ok = result.IsSuccess,
                failure = result.Failure?.ToString(),
            });
        });
        _app.MapPost("/authority", async context =>
        {
            IStudioAuthenticationIntegration integration =
                context.RequestServices.GetRequiredService<IStudioAuthenticationIntegration>();
            StudioAuthenticationResult<StudioBrowserAuthorization> result =
                await integration.AcquireBrowserAuthorizationAsync(context,
                    StudioTransportPurpose.CommandExecution, context.RequestAborted);
            if (!result.IsSuccess)
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { failure = result.Failure?.ToString() });
                return;
            }
            await context.Response.WriteAsJsonAsync(new
            {
                headerName = result.Value!.HeaderName,
                headerValue = result.Value.HeaderValue,
            });
        });
        _app.MapPost("/command", async context =>
        {
            IStudioAuthenticationIntegration integration =
                context.RequestServices.GetRequiredService<IStudioAuthenticationIntegration>();
            StudioAuthenticationResult<StudioTransportAuthorization> result =
                await integration.AuthorizeRequestAsync(context,
                    StudioTransportPurpose.CommandExecution, context.RequestAborted);
            context.Response.StatusCode = result.IsSuccess ? 204 : 403;
        });
        _app.MapPost("/fresh", async context =>
        {
            IStudioAuthenticationIntegration integration =
                context.RequestServices.GetRequiredService<IStudioAuthenticationIntegration>();
            StudioAuthenticationResult<StudioSessionObservation> session =
                await integration.ObserveSessionAsync(context, context.RequestAborted);
            if (!session.IsSuccess)
            {
                context.Response.StatusCode = 401;
                return;
            }
            DateTimeOffset now = DateTimeOffset.UtcNow;
            var request = new StudioFreshAuthenticationRequest
            {
                RequestIdentity = "fresh-request-1",
                CommandId = "agent.delete",
                Target = new StudioAgentResource("sample.application", "support-agent"),
                PreviewChecksum = StudioSha256.FromDigest(Enumerable.Repeat((byte)3, 32).ToArray()),
                PrincipalGeneration = session.Value!.PrincipalGeneration,
                SessionChecksum = session.Value.SessionChecksum,
                ProtectedScopeChecksum = session.Value.ProtectedScopeChecksum,
                RequiredAssurance = StudioFreshAuthenticationClass.MultiFactor,
                MaximumAuthenticationAge = TimeSpan.FromMinutes(5),
                IssuedAtUtc = now,
                ExpiresAtUtc = now.AddMinutes(5),
            };
            StudioAuthenticationResult<StudioFreshAuthenticationResult> result =
                await integration.AcquireFreshAuthenticationAsync(context, request, context.RequestAborted);
            if (!result.IsSuccess)
            {
                context.Response.StatusCode = 503;
                return;
            }
            if (result.Value is StudioFreshAuthenticationResult.Challenge challenge)
            {
                _challenge.Continuation = challenge.Continuation;
                await context.Response.WriteAsJsonAsync(new
                {
                    kind = "challenge",
                    target = challenge.BrowserAction.Target,
                });
            }
            else if (result.Value is StudioFreshAuthenticationResult.Satisfied)
                await context.Response.WriteAsJsonAsync(new { kind = "satisfied" });
            else
                await context.Response.WriteAsJsonAsync(new { kind = "unsupported" });
        });
        _app.MapPost("/fresh/complete", async context =>
        {
            if (_challenge.Continuation is null)
            {
                context.Response.StatusCode = 409;
                return;
            }
            IStudioAuthenticationIntegration integration =
                context.RequestServices.GetRequiredService<IStudioAuthenticationIntegration>();
            StudioAuthenticationResult<StudioFreshAuthenticationResult> result =
                await integration.CompleteFreshAuthenticationAsync(context,
                    _challenge.Continuation, context.RequestAborted);
            if (!result.IsSuccess)
            {
                context.Response.StatusCode = 401;
                await context.Response.WriteAsJsonAsync(new { failure = result.Failure?.ToString() });
                return;
            }
            await context.Response.WriteAsJsonAsync(new
            {
                kind = result.Value is StudioFreshAuthenticationResult.Satisfied ? "satisfied" : "other",
            });
        });
        await _app.StartAsync();
        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task Cookie_session_issues_purpose_bound_authority()
    {
        await LoginAsync();
        HttpResponseMessage session = await _client.GetAsync("/session");
        Assert.True(session.IsSuccessStatusCode, await session.Content.ReadAsStringAsync());
        using JsonDocument authority = JsonDocument.Parse(await (await _client.PostAsync("/authority", null))
            .Content.ReadAsStringAsync());
        string headerName = authority.RootElement.GetProperty("headerName").GetString()!;
        string headerValue = authority.RootElement.GetProperty("headerValue").GetString()!;

        using var rejected = new HttpRequestMessage(HttpMethod.Post, "/command");
        Assert.Equal(HttpStatusCode.Forbidden, (await _client.SendAsync(rejected)).StatusCode);
        using var accepted = new HttpRequestMessage(HttpMethod.Post, "/command");
        accepted.Headers.TryAddWithoutValidation(headerName, headerValue);
        Assert.Equal(HttpStatusCode.NoContent, (await _client.SendAsync(accepted)).StatusCode);
    }

    [Fact]
    public async Task Multi_factor_fresh_authentication_resumes_after_hpd_auth_step_up()
    {
        await LoginAsync();
        using JsonDocument challenge = JsonDocument.Parse(await (await _client.PostAsync("/fresh", null))
            .Content.ReadAsStringAsync());
        Assert.Equal("challenge", challenge.RootElement.GetProperty("kind").GetString());
        Assert.StartsWith("/auth/studio-fresh?continuation=",
            challenge.RootElement.GetProperty("target").GetString(), StringComparison.Ordinal);

        await LoginAsync(mfa: true);
        HttpResponseMessage completedResponse = await _client.PostAsync("/fresh/complete", null);
        string completedBody = await completedResponse.Content.ReadAsStringAsync();
        Assert.True(completedResponse.IsSuccessStatusCode, completedBody);
        using JsonDocument completed = JsonDocument.Parse(completedBody);
        Assert.Equal("satisfied", completed.RootElement.GetProperty("kind").GetString());
    }

    private async Task LoginAsync(bool mfa = false)
    {
        HttpResponseMessage response = await _client.GetAsync(mfa ? "/login?mfa=true" : "/login");
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        string setCookie = Assert.Single(response.Headers.GetValues("Set-Cookie"));
        _client.DefaultRequestHeaders.Remove("Cookie");
        _client.DefaultRequestHeaders.TryAddWithoutValidation("Cookie", setCookie.Split(';', 2)[0]);
    }

    private sealed class ChallengeState
    {
        public StudioFreshAuthenticationContinuation? Continuation { get; set; }
    }
}
