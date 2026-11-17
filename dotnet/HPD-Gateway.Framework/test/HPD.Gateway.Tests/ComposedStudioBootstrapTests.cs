using HPD.AI.Platform;
using HPD.AI.Platform.Studio;
using HPD.Base;
using HPD.Base.Studio;
using HPD.Gateway.ControlPlane;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Gateway.Tests;

/// <summary>Proves the real shared Studio bootstrap composes first-party modules without leaking unsupported pages.</summary>
public sealed class ComposedStudioBootstrapTests
{
    /// <summary>Executes BASE bootstrap over the finalized BASE, Gateway, and Graph application graph.</summary>
    [Fact]
    public async Task Bootstrap_composes_executable_data_and_framework_client_pages()
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddHPDBase(ConfigureBase);
        HPDAIPlatformBuilder platform = services.AddHPDAIPlatform()
            .AddStudioAuthentication(static _ => new Authentication())
            .AddHPDBaseStudio(static _ => new PrincipalResolver());
        GatewayStudioComposition.AddGatewayStudioCore(platform);
        platform.AddGraphStudio();
        await using ServiceProvider provider = services.BuildServiceProvider();
        StudioApplicationGraph graph = provider.GetRequiredService<StudioApplicationGraphProvider>().GetRequiredGraph();
        StudioModuleRegistration baseModule = graph.Modules.Single(static value => value.Identity.ModuleId == "base");
        StudioFrameworkClientRegistration baseClient = baseModule.Clients.Single();
        DateTimeOffset now = DateTimeOffset.UtcNow;
        StudioSessionObservation session = StudioSessionObservation.Create(1, Digest(1), "control-plane", Digest(2),
            now, now.AddMinutes(5), Digest(3));
        StudioBootstrapSnapshot? snapshot = await provider.GetRequiredService<IStudioBootstrapRuntime>().CreateAsync(
            new StudioBootstrapInvocation(new DefaultHttpContext(), graph,
                StudioTransportAuthorization.Create(session, StudioTransportPurpose.Bootstrap, now.AddMinutes(1)),
                StudioBootstrapRequest.Create(StudioShellContract.Current.Checksum,
                    provider.GetRequiredService<StudioEditionAssetCatalogProvider>().GetRequiredChecksum(StudioShellContract.Current),
                    baseClient.StaticRuntimeAbiChecksum, "en-US", [StudioBrowserCapability.History])), CancellationToken.None);

        Assert.NotNull(snapshot);
        Assert.Equal(["base", "gateway", "graph"], snapshot.Modules.Select(static value => value.ModuleId));
        Assert.Contains(snapshot.Pages, static value => value.PageId == "base.overview");
        Assert.Contains(snapshot.Pages, static value => value.PageId == "base.data");
        Assert.Contains(snapshot.Pages, static value => value.PageId == "base.module.detail");
        Assert.Contains(snapshot.Pages, static value => value.PageId == "base.collection.records");
        StudioVisiblePage operations = Assert.Single(snapshot.Pages.Where(static value => value.PageId == "base.operations"));
        Assert.Equal(6, operations.Views.Length);
        Assert.Contains(operations.ObservationMethodIds,
            static value => value == "base.studio.view.base.operations.definitions.registeredReads.list");
        Assert.Equal(6, snapshot.Pages.Count(static value => value.ModuleId == "graph"));
        Assert.Equal(4, snapshot.Pages.Count(static value => value.ModuleId == "gateway"));
        Assert.Contains(snapshot.Clients, static value => value.ClientId == "graph.control-plane" && value.Operations.Length == 6);
        Assert.Contains(snapshot.Clients, static value => value.ClientId == "gateway.admin" && value.Operations.Length == 23);
        Assert.Contains(snapshot.Pages, static value => value.ModuleId == "base" && value.PageId == "base.security");
        Assert.Contains(snapshot.Pages, static value => value.ModuleId == "base" && value.PageId == "base.policy.detail");
        Assert.Contains(snapshot.Pages, static value => value.ModuleId == "base" && value.PageId == "base.grant.detail");
        Assert.Contains(snapshot.Pages, static value => value.ModuleId == "base" && value.PageId == "base.policy.explain");
        Assert.Contains(snapshot.Pages, static value => value.ModuleId == "base" && value.PageId == "base.infrastructure");
        Assert.Contains(snapshot.Pages, static value => value.ModuleId == "base" && value.PageId == "base.schema.detail");
        Assert.Contains(snapshot.Pages, static value => value.ModuleId == "base" && value.PageId == "base.migration.detail");
        Assert.Contains(snapshot.Pages, static value => value.ModuleId == "base" && value.PageId == "base.backup.detail");
        Assert.Contains(snapshot.Pages, static value => value.ModuleId == "base" && value.PageId == "base.restore.detail");
        Assert.Contains(snapshot.Pages, static value => value.ModuleId == "base" && value.PageId == "base.maintenance.detail");
        Assert.Contains(snapshot.Pages, static value => value.ModuleId == "base" && value.PageId == "base.store.detail");
        Assert.Contains(snapshot.Pages, static value => value.ModuleId == "base" && value.PageId == "base.provider.detail");
        Assert.Contains(snapshot.Pages, static value => value.ModuleId == "base" && value.PageId == "base.diagnostics");
        Assert.Contains(snapshot.Pages, static value => value.ModuleId == "base" && value.PageId == "base.health.detail");
        Assert.Contains(snapshot.Pages, static value => value.ModuleId == "base" && value.PageId == "base.diagnostic.detail");
        Assert.All(snapshot.Pages.Where(static value => value.ModuleId == "base"),
            static value => Assert.Equal(value.Views.Length, value.ObservationMethodIds.Length));
    }

    private static void ConfigureBase(HPDBaseBuilder builder)
    {
        builder.ConfigureSchema(static options => options.ApplicationId = "sample.application");
        builder.AddPolicyAuthority(new BasePolicyAuthorityDefinition
        {
            Id = "studio.allow", Version = 1, OwningModuleId = "base",
            EvaluatorContractId = "studio.allow", EvaluatorContractVersion = 1, CompositionOrder = 1,
        }, new AllowPolicy());
        foreach (string id in new[] { "base.studio.action.discover", "base.studio.action.execute", "base.studio.action.preview",
            "base.studio.bootstrap.read", "base.studio.diagnostics.inspect", "base.studio.invalidation.subscribe",
            "base.studio.receipt.discover", "base.studio.receipt.inspect", "base.studio.resource.discover",
            "base.studio.resource.inspect", "base.studio.resource.links", "base.studio.resource.search" })
            builder.AddStaticGrantAuthority(new BaseGrantAuthorityDefinition { Id = id, Version = 1, OwningModuleId = "base",
                SourceContractId = "base.studio.fixed-grant", SourceContractVersion = 1 }, new AccessGrant { Id = id,
                ApplicationId = "sample.application", ModuleId = "base", Audience = HPDBaseEndpointAudience.ControlPlane,
                Subject = new AccessSubject { Kind = AccessSubjectKind.User, Id = "operator" }, Action = id,
                Scope = new ResourceScope { Kind = ResourceScopeKind.Runtime }, Effect = GrantEffect.Allow });
    }

    private sealed class PrincipalResolver : IStudioPrincipalContextResolver
    {
        public ValueTask<PrincipalContext?> ResolveAsync(HttpContext httpContext, StudioSessionObservation session,
            CancellationToken cancellationToken) => ValueTask.FromResult<PrincipalContext?>(new PrincipalContext
            { AuthenticationState = PrincipalAuthenticationState.Authenticated, SubjectKind = AccessSubjectKind.User, SubjectId = "operator" });
        public ValueTask<BaseOwnedSubjectScopeEvidence?> ResolveScopeAsync(HttpContext httpContext, StudioSessionObservation session,
            CancellationToken cancellationToken) => ValueTask.FromResult<BaseOwnedSubjectScopeEvidence?>(new BaseOwnedSubjectScopeEvidence { Kind = BaseSubjectScopeKind.Global });
    }

    private sealed class Authentication : IStudioAuthenticationIntegration
    {
        public StudioAuthenticationDescriptor Descriptor { get; } = StudioAuthenticationDescriptor.Create(
            "studio.test-auth", 1, StudioAuthenticationKind.Bearer,
            "/auth/login", "/auth/callback", "/auth/logout", "/auth/session",
            ["https://studio.example/"], null, null, TimeSpan.FromHours(1), false, []);

        public ValueTask<StudioAuthenticationResult<StudioSessionObservation>> ObserveSessionAsync(
            HttpContext context, CancellationToken token)
            => ValueTask.FromResult(StudioAuthenticationResult<StudioSessionObservation>.Success(Session()));

        public ValueTask<StudioAuthenticationResult<StudioProtectedReturnTarget>> ProtectReturnTargetAsync(
            HttpContext context, string? target, CancellationToken token)
            => ValueTask.FromResult(StudioAuthenticationResult<StudioProtectedReturnTarget>.Success(
                new StudioProtectedReturnTarget(new byte[32])));

        public ValueTask BeginSignInAsync(HttpContext context, StudioProtectedReturnTarget target, CancellationToken token)
            => ValueTask.CompletedTask;
        public ValueTask CompleteCallbackAsync(HttpContext context, CancellationToken token) => ValueTask.CompletedTask;
        public ValueTask BeginSignOutAsync(HttpContext context, CancellationToken token) => ValueTask.CompletedTask;

        public ValueTask<StudioAuthenticationResult<StudioTransportAuthorization>> AuthorizeRequestAsync(
            HttpContext context, StudioTransportPurpose purpose, CancellationToken token)
        {
            StudioSessionObservation session = Session();
            return ValueTask.FromResult(StudioAuthenticationResult<StudioTransportAuthorization>.Success(
                StudioTransportAuthorization.Create(session, purpose, session.IssuedAtUtc.AddMinutes(1))));
        }

        public async ValueTask<StudioAuthenticationResult<StudioBrowserAuthorization>> AcquireBrowserAuthorizationAsync(
            HttpContext context, StudioTransportPurpose purpose, CancellationToken token)
        {
            StudioAuthenticationResult<StudioTransportAuthorization> result =
                await AuthorizeRequestAsync(context, purpose, token);
            return StudioAuthenticationResult<StudioBrowserAuthorization>.Success(
                StudioBrowserAuthorization.Create("X-HPD-Studio-Test", "opaque-test-authority", result.Value!));
        }

        public ValueTask<StudioAuthenticationResult<StudioFreshAuthenticationResult>> AcquireFreshAuthenticationAsync(
            HttpContext context, StudioFreshAuthenticationRequest request, CancellationToken token)
            => ValueTask.FromResult(StudioAuthenticationResult<StudioFreshAuthenticationResult>.Success(
                new StudioFreshAuthenticationResult.Unsupported()));

        public ValueTask<StudioAuthenticationResult<StudioFreshAuthenticationResult>> CompleteFreshAuthenticationAsync(
            HttpContext context, StudioFreshAuthenticationContinuation continuation, CancellationToken token)
            => ValueTask.FromResult(StudioAuthenticationResult<StudioFreshAuthenticationResult>.Success(
                new StudioFreshAuthenticationResult.Unsupported()));

        private static StudioSessionObservation Session()
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            return StudioSessionObservation.Create(1, Digest(1), "control-plane", Digest(2),
                now, now.AddMinutes(5), Digest(3));
        }
    }

    private sealed class AllowPolicy : IPolicyEvaluator
    {
        public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request,
            CancellationToken cancellationToken = default) => ValueTask.FromResult(PolicyDecision.Allow());
    }

    private static StudioSha256 Digest(byte value) => StudioSha256.FromDigest(Enumerable.Repeat(value, 32).Select(static x => (byte)x).ToArray());
}
