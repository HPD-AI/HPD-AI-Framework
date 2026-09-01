using HPD.AI.Platform.Studio;
using Xunit;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.AI.Platform.Tests;

public sealed class BaseStudioAuthenticationContractTests
{
    [Fact]
    public void Fresh_authority_is_exactly_bound_and_single_use()
    {
        DateTimeOffset issued = DateTimeOffset.UtcNow;
        BaseStudioAuthenticationDescriptor descriptor = BaseStudioAuthenticationDescriptor.Create(
            "studio.auth", 1, BaseStudioAuthenticationKind.Bearer, "/auth/login", "/auth/callback", "/auth/logout", "/auth/session",
            ["https://studio.example/"], null, null, TimeSpan.FromHours(1), false, [BaseStudioFreshAuthenticationClass.MultiFactor]);
        BaseStudioSessionObservation session = BaseStudioSessionObservation.Create(7, Digest(1), "control-plane", Digest(2), issued, issued.AddMinutes(10), descriptor.Checksum);
        var target = new BaseStudioApplicationResource("application");
        BaseStudioFreshAuthenticationRequest request = new()
        {
            RequestIdentity = "request-1", CommandId = "record.delete", Target = target, PreviewChecksum = Digest(3),
            PrincipalGeneration = session.PrincipalGeneration, SessionChecksum = session.SessionChecksum,
            ProtectedScopeChecksum = session.ProtectedScopeChecksum, RequiredAssurance = BaseStudioFreshAuthenticationClass.MultiFactor,
            MaximumAuthenticationAge = TimeSpan.FromMinutes(5), IssuedAtUtc = issued, ExpiresAtUtc = issued.AddMinutes(5)
        };
        BaseStudioFreshAuthenticationBinding binding = BaseStudioFreshAuthenticationBinding.Create(request, descriptor.IntegrationId, descriptor.Checksum, issued);
        BaseStudioFreshAuthenticationAuthority authority = BaseStudioFreshAuthenticationAuthority.Create(new string('A', 32), binding,
            issued.AddSeconds(1), BaseStudioFreshAuthenticationClass.MultiFactor, "key-1");
        var registry = new BaseStudioCommandAuthorityRegistry();

        BaseStudioAuthenticationEndpoints.RegisterFreshAuthority(registry, authority);
        Assert.True(BaseStudioAuthenticationEndpoints.TryConsumeFreshAuthority(registry, authority.ToString(), request.RequestIdentity,
            request.CommandId, target, request.PreviewChecksum, session, BaseStudioFreshAuthenticationClass.MultiFactor));
        Assert.False(BaseStudioAuthenticationEndpoints.TryConsumeFreshAuthority(new BaseStudioCommandAuthorityRegistry(), authority.ToString(), request.RequestIdentity,
            request.CommandId, target, request.PreviewChecksum, session, BaseStudioFreshAuthenticationClass.MultiFactor));
        Assert.False(BaseStudioAuthenticationEndpoints.TryConsumeFreshAuthority(registry, authority.ToString(), request.RequestIdentity,
            request.CommandId, target, request.PreviewChecksum, session, BaseStudioFreshAuthenticationClass.MultiFactor));
        BaseStudioAuthenticationEndpoints.RegisterFreshAuthority(registry, authority);
        Assert.False(BaseStudioAuthenticationEndpoints.TryConsumeFreshAuthority(registry, authority.ToString(), request.RequestIdentity,
            request.CommandId, target, request.PreviewChecksum, session, BaseStudioFreshAuthenticationClass.MultiFactor));
        BaseStudioAuthenticationEndpoints.RestoreFreshAuthorityBeforeInfluence(registry, authority.ToString());
        Assert.True(BaseStudioAuthenticationEndpoints.TryConsumeFreshAuthority(registry, authority.ToString(), request.RequestIdentity,
            request.CommandId, target, request.PreviewChecksum, session, BaseStudioFreshAuthenticationClass.MultiFactor));
        Assert.Throws<ArgumentException>(() => BaseStudioFreshAuthenticationAuthority.Create(new string('B', 32), binding,
            issued.AddSeconds(1), BaseStudioFreshAuthenticationClass.Password, "key-1"));
        Assert.NotEqual(binding.Checksum, BaseStudioFreshAuthenticationBinding.Create(request with { CommandId = "record.replace" },
            descriptor.IntegrationId, descriptor.Checksum, issued).Checksum);
    }

    [Fact]
    public void Fresh_binding_checksum_changes_for_every_substitutable_authority()
    {
        DateTimeOffset issued = DateTimeOffset.UtcNow;
        BaseStudioFreshAuthenticationRequest original = Request(issued);
        BaseStudioSha256 descriptor = Digest(9);
        BaseStudioSha256 expected = BaseStudioFreshAuthenticationBinding.Create(original, "studio.auth", descriptor, issued).Checksum;
        BaseStudioFreshAuthenticationRequest[] substitutions =
        [
            original with { RequestIdentity = "request-2" }, original with { CommandId = "record.replace" },
            original with { Target = new BaseStudioApplicationResource("other-app") }, original with { PreviewChecksum = Digest(4) },
            original with { PrincipalGeneration = 8 }, original with { SessionChecksum = Digest(5) }, original with { ProtectedScopeChecksum = Digest(6) },
            original with { RequiredAssurance = BaseStudioFreshAuthenticationClass.HardwareBound }, original with { MaximumAuthenticationAge = TimeSpan.FromMinutes(4) },
            original with { ExpiresAtUtc = issued.AddMinutes(4) }
        ];
        Assert.All(substitutions, value => Assert.False(BaseStudioSha256.FixedTimeEquals(expected,
            BaseStudioFreshAuthenticationBinding.Create(value, "studio.auth", descriptor, issued).Checksum)));
    }

    [Theory]
    [InlineData("https://studio.example/base/studio/auth/fresh/callback?continuation=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", true)]
    [InlineData("https://studio.example/evil/base/studio/auth/fresh/callback?continuation=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", false)]
    [InlineData("https://studio.example/base/studio/auth/fresh/callback?continuation=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA&extra=1", false)]
    [InlineData("https://studio.example/base/studio/auth/fresh/callback?continuation=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA#fragment", false)]
    [InlineData("https://other.example/base/studio/auth/fresh/callback?continuation=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", false)]
    [InlineData("https://user@studio.example/base/studio/auth/fresh/callback?continuation=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA", false)]
    public void Fresh_challenge_target_is_exact_same_origin_callback(string target, bool expected)
    {
        var context = new DefaultHttpContext(); context.Request.Scheme = "https"; context.Request.Host = new HostString("studio.example");
        Assert.Equal(expected, BaseStudioAuthenticationEndpoints.ChallengeTargetMatches(context, target, new string('A', 32)));
    }

    [Fact]
    public void Pending_completion_releases_only_the_completed_poll_and_terminal_completion_is_cached()
    {
        DateTimeOffset issued = DateTimeOffset.UtcNow;
        BaseStudioAuthenticationDescriptor descriptor = BaseStudioAuthenticationDescriptor.Create(
            "studio.auth", 1, BaseStudioAuthenticationKind.Bearer, "/auth/login", "/auth/callback", "/auth/logout", "/auth/session",
            ["https://studio.example/"], null, null, TimeSpan.FromHours(1), false, [BaseStudioFreshAuthenticationClass.MultiFactor]);
        BaseStudioFreshAuthenticationBinding binding = BaseStudioFreshAuthenticationBinding.Create(Request(issued),
            descriptor.IntegrationId, descriptor.Checksum, issued);
        BaseStudioFreshAuthenticationContinuation continuation = BaseStudioFreshAuthenticationContinuation.Create(
            new string('A', 32), binding, "return-1", "key-1");
        BaseStudioFreshAuthenticationBrowserAction action = BaseStudioFreshAuthenticationBrowserAction.Create(
            BaseStudioFreshAuthenticationBrowserActionKind.Redirect,
            "https://studio.example/base/studio/auth/fresh/callback?continuation=" + new string('A', 32));
        var state = new FreshChallengeState(continuation, action)
        {
            CompletionOperation = Task.FromResult(BaseStudioAuthenticationResult<BaseStudioFreshAuthenticationResult>.Success(
                new BaseStudioFreshAuthenticationResult.Challenge(continuation, action)))
        };
        var pending = BaseStudioAuthenticationResult<BaseStudioFreshAuthenticationResult>.Success(
            new BaseStudioFreshAuthenticationResult.Challenge(continuation, action));

        Assert.True(BaseStudioAuthenticationEndpoints.AcceptCompletionResult(state, pending, descriptor));
        Assert.Null(state.CompletionOperation);
        Assert.Null(state.TerminalResult);

        BaseStudioFreshAuthenticationAuthority authority = BaseStudioFreshAuthenticationAuthority.Create(
            new string('B', 32), binding, issued.AddSeconds(1), BaseStudioFreshAuthenticationClass.MultiFactor, "key-1");
        var satisfied = BaseStudioAuthenticationResult<BaseStudioFreshAuthenticationResult>.Success(
            new BaseStudioFreshAuthenticationResult.Satisfied(authority));
        Assert.True(BaseStudioAuthenticationEndpoints.AcceptCompletionResult(state, satisfied, descriptor));
        Assert.Same(satisfied, state.TerminalResult);
    }

    private static BaseStudioFreshAuthenticationRequest Request(DateTimeOffset issued) => new()
    {
        RequestIdentity = "request-1", CommandId = "record.delete", Target = new BaseStudioApplicationResource("application"),
        PreviewChecksum = Digest(3), PrincipalGeneration = 7, SessionChecksum = Digest(1), ProtectedScopeChecksum = Digest(2),
        RequiredAssurance = BaseStudioFreshAuthenticationClass.MultiFactor, MaximumAuthenticationAge = TimeSpan.FromMinutes(5), IssuedAtUtc = issued, ExpiresAtUtc = issued.AddMinutes(5)
    };

    private static BaseStudioSha256 Digest(byte value) => BaseStudioSha256.FromDigest(Enumerable.Repeat(value, 32).ToArray());
    [Fact]
    public void Cookie_descriptor_requires_exact_antiforgery_authority()
    {
        BaseStudioAuthenticationDescriptor value = BaseStudioAuthenticationDescriptor.Create(
            "studio.auth", 1, BaseStudioAuthenticationKind.CookieBff,
            "/auth/login", "/auth/callback", "/auth/logout", "/auth/session",
            ["https://studio.example/"], "X-HPD-CSRF", "studio.csrf", TimeSpan.FromHours(8), false, []);

        Assert.Equal(BaseStudioAuthenticationKind.CookieBff, value.Kind);
        Assert.Empty(value.SupportedFreshAuthentication);
        Assert.Throws<ArgumentException>(() => BaseStudioAuthenticationDescriptor.Create(
            "studio.auth", 1, BaseStudioAuthenticationKind.CookieBff,
            "/auth/login", "/auth/callback", "/auth/logout", "/auth/session",
            ["https://studio.example/"], null, null, TimeSpan.FromHours(8), false, []));
    }

    [Fact]
    public void Bearer_descriptor_forbids_antiforgery_and_noncanonical_origins()
    {
        Assert.NotNull(BaseStudioAuthenticationDescriptor.Create(
            "studio.auth", 1, BaseStudioAuthenticationKind.Bearer,
            "/auth/login", "/auth/callback", "/auth/logout", "/auth/session",
            ["https://studio.example/"], null, null, TimeSpan.FromHours(1), true, []));
        Assert.Throws<ArgumentException>(() => BaseStudioAuthenticationDescriptor.Create(
            "studio.auth", 1, BaseStudioAuthenticationKind.Bearer,
            "/auth/login", "/auth/callback", "/auth/logout", "/auth/session",
            ["http://studio.example/"], null, null, TimeSpan.FromHours(1), true, []));
        Assert.Throws<ArgumentException>(() => BaseStudioAuthenticationDescriptor.Create(
            "studio.auth", 1, BaseStudioAuthenticationKind.Bearer,
            "/auth/login", "/auth/callback", "/auth/logout", "/auth/session",
            ["https://studio.example/"], "X-HPD-CSRF", "studio.csrf", TimeSpan.FromHours(1), true, []));
        Assert.Throws<ArgumentException>(() => BaseStudioAuthenticationDescriptor.Create(
            "studio.auth", 1, BaseStudioAuthenticationKind.Bearer,
            "/auth/login", "/auth/callback", "/auth/logout", "/auth/session",
            ["https://studio.example/"], null, null, TimeSpan.FromHours(1), true,
            [BaseStudioFreshAuthenticationClass.MultiFactor, BaseStudioFreshAuthenticationClass.Password]));
    }

    [Fact]
    public void Platform_maps_the_fixed_authentication_inventory()
    {
        WebApplicationBuilder host = WebApplication.CreateBuilder();
        host.Services.AddHPDAIPlatform()
            .AddStudioModule<HostingTestStudioContribution>()
            .AddStudioAuthentication(static _ => new FakeIntegration());
        host.Services.AddSingleton<IBaseStudioBootstrapRuntime, HostingTestBootstrapRuntime>();
        WebApplication app = host.Build();
        app.MapHPDAIPlatform();

        string[] names = ((Microsoft.AspNetCore.Routing.IEndpointRouteBuilder)app).DataSources.SelectMany(static source => source.Endpoints)
            .Select(static endpoint => endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.IEndpointNameMetadata>()?.EndpointName ?? string.Empty).ToArray();
        Assert.Contains("BaseStudioAuthenticationLogin", names);
        Assert.Contains("BaseStudioAuthenticationCallback", names);
        Assert.Contains("BaseStudioAuthenticationLogout", names);
        Assert.Contains("BaseStudioAuthenticationSession", names);
        Assert.Contains("BaseStudioFreshAuthentication", names);
        Assert.Contains("BaseStudioFreshAuthenticationComplete", names);
    }

    private sealed class FakeIntegration : IBaseStudioAuthenticationIntegration
    {
        public BaseStudioAuthenticationDescriptor Descriptor { get; } = BaseStudioAuthenticationDescriptor.Create(
            "studio.auth", 1, BaseStudioAuthenticationKind.Bearer, "/auth/login", "/auth/callback", "/auth/logout", "/auth/session",
            ["https://studio.example/"], null, null, TimeSpan.FromHours(1), false, []);
        public ValueTask<BaseStudioAuthenticationResult<BaseStudioSessionObservation>> ObserveSessionAsync(HttpContext context, CancellationToken token)
            => ValueTask.FromResult(BaseStudioAuthenticationResult<BaseStudioSessionObservation>.Failed(BaseStudioAuthenticationFailure.AuthenticationRequired));
        public ValueTask<BaseStudioAuthenticationResult<BaseStudioProtectedReturnTarget>> ProtectReturnTargetAsync(HttpContext context, string? target, CancellationToken token)
            => ValueTask.FromResult(BaseStudioAuthenticationResult<BaseStudioProtectedReturnTarget>.Success(new BaseStudioProtectedReturnTarget(new byte[32])));
        public ValueTask BeginSignInAsync(HttpContext context, BaseStudioProtectedReturnTarget target, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask CompleteCallbackAsync(HttpContext context, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask BeginSignOutAsync(HttpContext context, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask<BaseStudioAuthenticationResult<BaseStudioTransportAuthorization>> AuthorizeRequestAsync(HttpContext context, BaseStudioTransportPurpose purpose, CancellationToken token)
            => ValueTask.FromResult(BaseStudioAuthenticationResult<BaseStudioTransportAuthorization>.Failed(BaseStudioAuthenticationFailure.AuthenticationRequired));
        public ValueTask<BaseStudioAuthenticationResult<BaseStudioBrowserAuthorization>> AcquireBrowserAuthorizationAsync(
            HttpContext context, BaseStudioTransportPurpose purpose, CancellationToken token)
            => ValueTask.FromResult(BaseStudioAuthenticationResult<BaseStudioBrowserAuthorization>.Failed(BaseStudioAuthenticationFailure.AuthenticationRequired));
        public ValueTask<BaseStudioAuthenticationResult<BaseStudioFreshAuthenticationResult>> AcquireFreshAuthenticationAsync(
            HttpContext context, BaseStudioFreshAuthenticationRequest request, CancellationToken token)
            => ValueTask.FromResult(BaseStudioAuthenticationResult<BaseStudioFreshAuthenticationResult>.Failed(BaseStudioAuthenticationFailure.AuthenticationRequired));
        public ValueTask<BaseStudioAuthenticationResult<BaseStudioFreshAuthenticationResult>> CompleteFreshAuthenticationAsync(
            HttpContext context, BaseStudioFreshAuthenticationContinuation continuation, CancellationToken token)
            => ValueTask.FromResult(BaseStudioAuthenticationResult<BaseStudioFreshAuthenticationResult>.Failed(BaseStudioAuthenticationFailure.AuthenticationRequired));
    }
}
