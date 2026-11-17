using HPD.AI.Platform.Studio;
using Xunit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.AI.Platform.Tests;

public sealed class StudioAuthenticationContractTests
{
    [Fact]
    public void Fresh_authority_is_exactly_bound_and_single_use()
    {
        DateTimeOffset issued = DateTimeOffset.UtcNow;
        StudioAuthenticationDescriptor descriptor = StudioAuthenticationDescriptor.Create(
            "studio.auth", 1, StudioAuthenticationKind.Bearer, "/auth/login", "/auth/callback", "/auth/logout", "/auth/session",
            ["https://studio.example/"], null, null, TimeSpan.FromHours(1), false, [StudioFreshAuthenticationClass.MultiFactor]);
        StudioSessionObservation session = StudioSessionObservation.Create(7, Digest(1), "control-plane", Digest(2), issued, issued.AddMinutes(10), descriptor.Checksum);
        var target = new StudioApplicationResource("application");
        StudioFreshAuthenticationRequest request = new()
        {
            RequestIdentity = "request-1", CommandId = "record.delete", Target = target, PreviewChecksum = Digest(3),
            PrincipalGeneration = session.PrincipalGeneration, SessionChecksum = session.SessionChecksum,
            ProtectedScopeChecksum = session.ProtectedScopeChecksum, RequiredAssurance = StudioFreshAuthenticationClass.MultiFactor,
            MaximumAuthenticationAge = TimeSpan.FromMinutes(5), IssuedAtUtc = issued, ExpiresAtUtc = issued.AddMinutes(5)
        };
        StudioFreshAuthenticationBinding binding = StudioFreshAuthenticationBinding.Create(request, descriptor.IntegrationId, descriptor.Checksum, issued);
        StudioFreshAuthenticationAuthority authority = StudioFreshAuthenticationAuthority.Create(new string('A', 32), binding,
            issued.AddSeconds(1), StudioFreshAuthenticationClass.MultiFactor, "key-1");
        var registry = new StudioCommandAuthorityRegistry();

        StudioAuthenticationEndpoints.RegisterFreshAuthority(registry, authority);
        Assert.True(StudioAuthenticationEndpoints.TryConsumeFreshAuthority(registry, authority.ToString(), request.RequestIdentity,
            request.CommandId, target, request.PreviewChecksum, session, StudioFreshAuthenticationClass.MultiFactor));
        Assert.False(StudioAuthenticationEndpoints.TryConsumeFreshAuthority(new StudioCommandAuthorityRegistry(), authority.ToString(), request.RequestIdentity,
            request.CommandId, target, request.PreviewChecksum, session, StudioFreshAuthenticationClass.MultiFactor));
        Assert.False(StudioAuthenticationEndpoints.TryConsumeFreshAuthority(registry, authority.ToString(), request.RequestIdentity,
            request.CommandId, target, request.PreviewChecksum, session, StudioFreshAuthenticationClass.MultiFactor));
        StudioAuthenticationEndpoints.RegisterFreshAuthority(registry, authority);
        Assert.False(StudioAuthenticationEndpoints.TryConsumeFreshAuthority(registry, authority.ToString(), request.RequestIdentity,
            request.CommandId, target, request.PreviewChecksum, session, StudioFreshAuthenticationClass.MultiFactor));
        StudioAuthenticationEndpoints.RestoreFreshAuthorityBeforeInfluence(registry, authority.ToString());
        Assert.True(StudioAuthenticationEndpoints.TryConsumeFreshAuthority(registry, authority.ToString(), request.RequestIdentity,
            request.CommandId, target, request.PreviewChecksum, session, StudioFreshAuthenticationClass.MultiFactor));
        Assert.Throws<ArgumentException>(() => StudioFreshAuthenticationAuthority.Create(new string('B', 32), binding,
            issued.AddSeconds(1), StudioFreshAuthenticationClass.Password, "key-1"));
        Assert.NotEqual(binding.Checksum, StudioFreshAuthenticationBinding.Create(request with { CommandId = "record.replace" },
            descriptor.IntegrationId, descriptor.Checksum, issued).Checksum);
    }

    [Fact]
    public void Fresh_binding_checksum_changes_for_every_substitutable_authority()
    {
        DateTimeOffset issued = DateTimeOffset.UtcNow;
        StudioFreshAuthenticationRequest original = Request(issued);
        StudioSha256 descriptor = Digest(9);
        StudioSha256 expected = StudioFreshAuthenticationBinding.Create(original, "studio.auth", descriptor, issued).Checksum;
        StudioFreshAuthenticationRequest[] substitutions =
        [
            original with { RequestIdentity = "request-2" }, original with { CommandId = "record.replace" },
            original with { Target = new StudioApplicationResource("other-app") }, original with { PreviewChecksum = Digest(4) },
            original with { PrincipalGeneration = 8 }, original with { SessionChecksum = Digest(5) }, original with { ProtectedScopeChecksum = Digest(6) },
            original with { RequiredAssurance = StudioFreshAuthenticationClass.HardwareBound }, original with { MaximumAuthenticationAge = TimeSpan.FromMinutes(4) },
            original with { ExpiresAtUtc = issued.AddMinutes(4) }
        ];
        Assert.All(substitutions, value => Assert.False(StudioSha256.FixedTimeEquals(expected,
            StudioFreshAuthenticationBinding.Create(value, "studio.auth", descriptor, issued).Checksum)));
    }

    [Theory]
    [InlineData("https://studio.example/base/studio/auth/fresh/callback?continuation=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", true)]
    [InlineData("https://studio.example/integration-owned/step-up?continuation=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", true)]
    [InlineData("https://studio.example/base/studio/auth/fresh/callback?continuation=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA&extra=1", false)]
    [InlineData("https://studio.example/base/studio/auth/fresh/callback?continuation=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA#fragment", false)]
    [InlineData("https://studio.example/auth/studio-fresh?continuation=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", true)]
    [InlineData("https://other.example/base/studio/auth/fresh/callback?continuation=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", false)]
    [InlineData("https://user@studio.example/base/studio/auth/fresh/callback?continuation=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", false)]
    public void Fresh_challenge_target_is_same_origin_and_bound_to_the_exact_continuation(string target, bool expected)
    {
        var context = new DefaultHttpContext(); context.Request.Scheme = "https"; context.Request.Host = new HostString("studio.example");
        Assert.Equal(expected, StudioAuthenticationEndpoints.ChallengeTargetMatches(context, target, new string('A', 32)));
    }

    [Fact]
    public void Pending_completion_releases_only_the_completed_poll_and_terminal_completion_is_cached()
    {
        DateTimeOffset issued = DateTimeOffset.UtcNow;
        StudioAuthenticationDescriptor descriptor = StudioAuthenticationDescriptor.Create(
            "studio.auth", 1, StudioAuthenticationKind.Bearer, "/auth/login", "/auth/callback", "/auth/logout", "/auth/session",
            ["https://studio.example/"], null, null, TimeSpan.FromHours(1), false, [StudioFreshAuthenticationClass.MultiFactor]);
        StudioFreshAuthenticationBinding binding = StudioFreshAuthenticationBinding.Create(Request(issued),
            descriptor.IntegrationId, descriptor.Checksum, issued);
        StudioFreshAuthenticationContinuation continuation = StudioFreshAuthenticationContinuation.Create(
            new string('A', 32), binding, "return-1", "key-1");
        StudioFreshAuthenticationBrowserAction action = StudioFreshAuthenticationBrowserAction.Create(
            StudioFreshAuthenticationBrowserActionKind.Redirect,
            "https://studio.example/base/studio/auth/fresh/callback?continuation=" + new string('A', 32));
        var state = new FreshChallengeState(continuation, action)
        {
            CompletionOperation = Task.FromResult(StudioAuthenticationResult<StudioFreshAuthenticationResult>.Success(
                new StudioFreshAuthenticationResult.Challenge(continuation, action)))
        };
        var pending = StudioAuthenticationResult<StudioFreshAuthenticationResult>.Success(
            new StudioFreshAuthenticationResult.Challenge(continuation, action));

        Assert.True(StudioAuthenticationEndpoints.AcceptCompletionResult(state, pending, descriptor));
        Assert.Null(state.CompletionOperation);
        Assert.Null(state.TerminalResult);

        StudioFreshAuthenticationAuthority authority = StudioFreshAuthenticationAuthority.Create(
            new string('B', 32), binding, issued.AddSeconds(1), StudioFreshAuthenticationClass.MultiFactor, "key-1");
        var satisfied = StudioAuthenticationResult<StudioFreshAuthenticationResult>.Success(
            new StudioFreshAuthenticationResult.Satisfied(authority));
        Assert.True(StudioAuthenticationEndpoints.AcceptCompletionResult(state, satisfied, descriptor));
        Assert.Same(satisfied, state.TerminalResult);
    }

    private static StudioFreshAuthenticationRequest Request(DateTimeOffset issued) => new()
    {
        RequestIdentity = "request-1", CommandId = "record.delete", Target = new StudioApplicationResource("application"),
        PreviewChecksum = Digest(3), PrincipalGeneration = 7, SessionChecksum = Digest(1), ProtectedScopeChecksum = Digest(2),
        RequiredAssurance = StudioFreshAuthenticationClass.MultiFactor, MaximumAuthenticationAge = TimeSpan.FromMinutes(5), IssuedAtUtc = issued, ExpiresAtUtc = issued.AddMinutes(5)
    };

    private static StudioSha256 Digest(byte value) => StudioSha256.FromDigest(Enumerable.Repeat(value, 32).ToArray());
    [Fact]
    public void Cookie_descriptor_requires_exact_antiforgery_authority()
    {
        StudioAuthenticationDescriptor value = StudioAuthenticationDescriptor.Create(
            "studio.auth", 1, StudioAuthenticationKind.CookieBff,
            "/auth/login", "/auth/callback", "/auth/logout", "/auth/session",
            ["https://studio.example/"], "X-HPD-CSRF", "studio.csrf", TimeSpan.FromHours(8), false, []);

        Assert.Equal(StudioAuthenticationKind.CookieBff, value.Kind);
        Assert.Empty(value.SupportedFreshAuthentication);
        Assert.Throws<ArgumentException>(() => StudioAuthenticationDescriptor.Create(
            "studio.auth", 1, StudioAuthenticationKind.CookieBff,
            "/auth/login", "/auth/callback", "/auth/logout", "/auth/session",
            ["https://studio.example/"], null, null, TimeSpan.FromHours(8), false, []));
    }

    [Fact]
    public void Bearer_descriptor_forbids_antiforgery_and_noncanonical_origins()
    {
        Assert.NotNull(StudioAuthenticationDescriptor.Create(
            "studio.auth", 1, StudioAuthenticationKind.Bearer,
            "/auth/login", "/auth/callback", "/auth/logout", "/auth/session",
            ["https://studio.example/"], null, null, TimeSpan.FromHours(1), true, []));
        Assert.Throws<ArgumentException>(() => StudioAuthenticationDescriptor.Create(
            "studio.auth", 1, StudioAuthenticationKind.Bearer,
            "/auth/login", "/auth/callback", "/auth/logout", "/auth/session",
            ["http://studio.example/"], null, null, TimeSpan.FromHours(1), true, []));
        Assert.Throws<ArgumentException>(() => StudioAuthenticationDescriptor.Create(
            "studio.auth", 1, StudioAuthenticationKind.Bearer,
            "/auth/login", "/auth/callback", "/auth/logout", "/auth/session",
            ["https://studio.example/"], "X-HPD-CSRF", "studio.csrf", TimeSpan.FromHours(1), true, []));
        Assert.Throws<ArgumentException>(() => StudioAuthenticationDescriptor.Create(
            "studio.auth", 1, StudioAuthenticationKind.Bearer,
            "/auth/login", "/auth/callback", "/auth/logout", "/auth/session",
            ["https://studio.example/"], null, null, TimeSpan.FromHours(1), true,
            [StudioFreshAuthenticationClass.MultiFactor, StudioFreshAuthenticationClass.Password]));
    }

    [Fact]
    public void Platform_maps_the_fixed_authentication_inventory()
    {
        WebApplicationBuilder host = WebApplication.CreateBuilder();
        host.Services.AddHPDAIPlatform()
            .AddStudioModule<HostingTestStudioContribution>()
            .AddStudioAuthentication(static _ => new FakeIntegration());
        host.Services.AddSingleton<IStudioBootstrapRuntime, HostingTestBootstrapRuntime>();
        WebApplication app = host.Build();
        app.MapHPDAIPlatform();

        string[] names = ((Microsoft.AspNetCore.Routing.IEndpointRouteBuilder)app).DataSources.SelectMany(static source => source.Endpoints)
            .Select(static endpoint => endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.IEndpointNameMetadata>()?.EndpointName ?? string.Empty).ToArray();
        Assert.Contains("StudioAuthenticationLogin", names);
        Assert.Contains("StudioAuthenticationCallback", names);
        Assert.Contains("StudioAuthenticationLogout", names);
        Assert.Contains("StudioAuthenticationSession", names);
        Assert.Contains("StudioFreshAuthentication", names);
        Assert.Contains("StudioFreshAuthenticationComplete", names);
    }

    private sealed class FakeIntegration : IStudioAuthenticationIntegration
    {
        public StudioAuthenticationDescriptor Descriptor { get; } = StudioAuthenticationDescriptor.Create(
            "studio.auth", 1, StudioAuthenticationKind.Bearer, "/auth/login", "/auth/callback", "/auth/logout", "/auth/session",
            ["https://studio.example/"], null, null, TimeSpan.FromHours(1), false, []);
        public ValueTask<StudioAuthenticationResult<StudioSessionObservation>> ObserveSessionAsync(HttpContext context, CancellationToken token)
            => ValueTask.FromResult(StudioAuthenticationResult<StudioSessionObservation>.Failed(StudioAuthenticationFailure.AuthenticationRequired));
        public ValueTask<StudioAuthenticationResult<StudioProtectedReturnTarget>> ProtectReturnTargetAsync(HttpContext context, string? target, CancellationToken token)
            => ValueTask.FromResult(StudioAuthenticationResult<StudioProtectedReturnTarget>.Success(new StudioProtectedReturnTarget(new byte[32])));
        public ValueTask BeginSignInAsync(HttpContext context, StudioProtectedReturnTarget target, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask CompleteCallbackAsync(HttpContext context, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask BeginSignOutAsync(HttpContext context, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask<StudioAuthenticationResult<StudioTransportAuthorization>> AuthorizeRequestAsync(HttpContext context, StudioTransportPurpose purpose, CancellationToken token)
            => ValueTask.FromResult(StudioAuthenticationResult<StudioTransportAuthorization>.Failed(StudioAuthenticationFailure.AuthenticationRequired));
        public ValueTask<StudioAuthenticationResult<StudioBrowserAuthorization>> AcquireBrowserAuthorizationAsync(
            HttpContext context, StudioTransportPurpose purpose, CancellationToken token)
            => ValueTask.FromResult(StudioAuthenticationResult<StudioBrowserAuthorization>.Failed(StudioAuthenticationFailure.AuthenticationRequired));
        public ValueTask<StudioAuthenticationResult<StudioFreshAuthenticationResult>> AcquireFreshAuthenticationAsync(
            HttpContext context, StudioFreshAuthenticationRequest request, CancellationToken token)
            => ValueTask.FromResult(StudioAuthenticationResult<StudioFreshAuthenticationResult>.Failed(StudioAuthenticationFailure.AuthenticationRequired));
        public ValueTask<StudioAuthenticationResult<StudioFreshAuthenticationResult>> CompleteFreshAuthenticationAsync(
            HttpContext context, StudioFreshAuthenticationContinuation continuation, CancellationToken token)
            => ValueTask.FromResult(StudioAuthenticationResult<StudioFreshAuthenticationResult>.Failed(StudioAuthenticationFailure.AuthenticationRequired));
    }
}
