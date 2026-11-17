using System.Security.Claims;
using System.Text.Json;
using HPD.AI.Platform.Studio;
using HPD.Agent.Hosting.Data;
using HPD.Agent.Hosting.Lifecycle;
using HPD.Agent.Serialization;
using HPD.Agent.Validation;
using HPD.Base;
using HPD.Base.Studio;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace HPD.Agent.Studio.Tests;

public sealed class AgentStudioLifecycleCommandTests
{
    private const string ApplicationId = "studio.tests";

    [Fact]
    public void Visual_creation_defaults_are_a_valid_AgentConfig()
    {
        const string json = """
        {
          "name": "New Agent",
          "systemInstructions": "You are a helpful assistant.",
          "maxAgenticIterations": 10,
          "maxSubAgentDepth": 4,
          "continuationExtensionAmount": 3,
          "allowUserQuestions": false,
          "includeReasoningInModelHistory": false,
          "coalesceDeltas": false,
          "clients": { "transport": 0 },
          "defaultSubAgentForkOptions": { "policy": 0, "descendantPolicy": 0 },
          "collapsing": {
            "enabled": true,
            "collapseClientTools": false,
            "maxFunctionNamesInDescription": 10,
            "persistSystemPromptInjections": false,
            "enableErrorRecovery": true,
            "recoveryHistoryMode": 1,
            "hideToolHarnessInteractionsWithinTurn": true
          },
          "skills": { "instructionDelivery": 0 },
          "toolOutputs": {
            "enabled": true,
            "maxInlineResultBytes": 65536,
            "failureMode": 0,
            "contentRetrievalEnabled": true,
            "retrievalLimits": {
              "maxListResultsPerCall": 50,
              "maxReadBytesPerCall": 65536,
              "maxReadBytesPerTurn": 262144,
              "maxReadCallsPerTurn": 8,
              "maxListCallsPerTurn": 8
            }
          },
          "shutdown": {
            "gracefulDrainTimeout": "00:00:30",
            "cancellationDrainTimeout": "00:00:10",
            "remoteOperations": 0,
            "leaseLeaks": 0
          },
          "operationRetention": {
            "terminalRetention": "30.00:00:00",
            "providerDeduplicationRetention": "90.00:00:00",
            "maximumTerminalOperationsPerThread": 10000
          }
        }
        """;
        AgentConfig config = Assert.IsType<AgentConfig>(HpdAgentConfigSerializer.Deserialize(json));
        Assert.Empty(AgentConfigValidator.Validate(config));
    }

    [Fact]
    public async Task Agent_creation_reviews_the_canonical_config_and_creates_the_definition()
    {
        var definitions = new DefinitionLifecycle();
        LifecycleHarness harness = Harness(definitions: definitions);
        StudioCanonicalJson preview = await PreviewAgentAsync(harness);

        await ExecuteAgentAsync(harness, preview);

        Assert.True(definitions.Exists);
        Assert.Equal("created-agent", definitions.AgentId);
        Assert.Equal("Created Agent", definitions.Name);
    }

    [Fact]
    public async Task Agent_creation_preview_rejects_an_existing_definition()
    {
        var definitions = new DefinitionLifecycle { Exists = true };
        LifecycleHarness harness = Harness(definitions: definitions);

        StudioCanonicalJson? result = await harness.CreationProducer.PreviewAsync(
            harness.Invocation("agent.studio.command.agent.create.preview", AgentPreviewRequest(harness, "{}")),
            CancellationToken.None);

        Assert.Null(result);
    }

    [Fact]
    public async Task Agent_creation_rejects_wrong_acknowledgement_evidence()
    {
        var definitions = new DefinitionLifecycle();
        LifecycleHarness harness = Harness(definitions: definitions);
        StudioCanonicalJson preview = await PreviewAgentAsync(harness);
        StudioCanonicalJson request = AgentExecuteRequest(
            harness, preview, PreviewChecksum(preview), purposeId: "confirm.wrong");

        await Assert.ThrowsAsync<StudioCommandFailedBeforeInfluenceException>(() =>
            harness.CreationProducer.ExecuteAsync(
                harness.Invocation("agent.studio.command.agent.create.execute", request),
                CancellationToken.None).AsTask());

        Assert.False(definitions.Exists);
    }

    [Fact]
    public async Task Session_deletion_requires_the_exact_review_and_removes_the_session()
    {
        var sessionStore = new SessionLifecycle();
        LifecycleHarness harness = Harness(sessions: sessionStore);
        StudioCanonicalJson preview = await PreviewSessionAsync(harness);
        string checksum = PreviewChecksum(preview);

        await ExecuteSessionAsync(harness, preview, checksum);

        Assert.Equal(1, sessionStore.DeleteCalls);
        Assert.False(sessionStore.Exists);
    }

    [Fact]
    public async Task Preview_rejects_a_stale_resource_authority()
    {
        LifecycleHarness harness = Harness(sessions: new SessionLifecycle());
        StudioCanonicalJson request = SessionPreviewRequest(
            new string('0', 64), harness.Authority.Checksum);

        StudioCanonicalJson? result = await harness.PreviewAsync(
            "agent.studio.command.agent.session.delete.preview", request);

        Assert.Null(result);
    }

    [Fact]
    public async Task Expired_review_cannot_delete_a_session()
    {
        var sessionStore = new SessionLifecycle();
        LifecycleHarness harness = Harness(sessions: sessionStore);
        StudioCanonicalJson preview = await PreviewSessionAsync(harness);
        harness.Time.Advance(TimeSpan.FromMinutes(6));

        await Assert.ThrowsAsync<StudioCommandFailedBeforeInfluenceException>(() =>
            ExecuteSessionAsync(harness, preview, PreviewChecksum(preview)));

        Assert.Equal(0, sessionStore.DeleteCalls);
        Assert.True(sessionStore.Exists);
    }

    [Fact]
    public async Task Missing_fresh_authentication_fails_before_session_influence()
    {
        var sessionStore = new SessionLifecycle();
        LifecycleHarness harness = Harness(sessions: sessionStore);
        StudioCanonicalJson preview = await PreviewSessionAsync(harness);
        StudioCanonicalJson request = SessionExecuteRequest(
            preview, PreviewChecksum(preview), harness.Authority.Checksum, freshAuthentication: null);

        await Assert.ThrowsAsync<StudioCommandFailedBeforeInfluenceException>(() =>
            harness.ExecuteAsync("agent.studio.command.agent.session.delete.execute", request));

        Assert.Equal(0, sessionStore.DeleteCalls);
    }

    [Theory]
    [InlineData("confirm.wrong", "impact.session.delete", false)]
    [InlineData("confirm.session.delete", "impact.wrong", false)]
    [InlineData("confirm.session.delete", "impact.session.delete", true)]
    public async Task Session_deletion_rejects_wrong_acknowledgement_evidence(
        string purposeId, string impactId, bool addExtraMember)
    {
        var sessionStore = new SessionLifecycle();
        LifecycleHarness harness = Harness(sessions: sessionStore);
        StudioCanonicalJson preview = await PreviewSessionAsync(harness);
        StudioCanonicalJson request = SessionExecuteRequest(
            preview, PreviewChecksum(preview), harness.Authority.Checksum,
            purposeId: purposeId, impactId: impactId, addExtraAcknowledgementMember: addExtraMember);

        await Assert.ThrowsAsync<StudioCommandFailedBeforeInfluenceException>(() =>
            harness.ExecuteAsync("agent.studio.command.agent.session.delete.execute", request));

        Assert.Equal(0, sessionStore.DeleteCalls);
    }

    [Fact]
    public async Task Non_recursive_thread_deletion_fails_when_children_exist()
    {
        var threads = new ThreadLifecycle(hasChildren: true);
        LifecycleHarness harness = Harness(threads: threads);
        StudioCanonicalJson preview = await PreviewThreadAsync(harness, recursive: false);

        await Assert.ThrowsAsync<StudioCommandFailedBeforeInfluenceException>(() =>
            ExecuteThreadAsync(harness, preview, recursive: false));

        Assert.True(threads.Exists);
        Assert.Equal(1, threads.DeleteCalls);
        Assert.False(threads.LastRecursive);
    }

    [Fact]
    public async Task Recursive_thread_deletion_removes_the_subtree_and_the_thread_disappears()
    {
        var threads = new ThreadLifecycle(hasChildren: true, recursiveEnabled: true);
        LifecycleHarness harness = Harness(threads: threads);
        StudioCanonicalJson preview = await PreviewThreadAsync(harness, recursive: true);

        await ExecuteThreadAsync(harness, preview, recursive: true);

        Assert.Equal(1, threads.DeleteCalls);
        Assert.True(threads.LastRecursive);
        Assert.False(threads.Exists);
    }

    [Fact]
    public async Task Lifecycle_service_exception_is_indeterminate()
    {
        var sessions = new SessionLifecycle { ThrowOnDelete = true };
        LifecycleHarness harness = Harness(sessions: sessions);
        StudioCanonicalJson preview = await PreviewSessionAsync(harness);

        await Assert.ThrowsAsync<StudioCommandIndeterminateException>(() =>
            ExecuteSessionAsync(harness, preview, PreviewChecksum(preview)));
    }

    private static async Task<StudioCanonicalJson> PreviewSessionAsync(LifecycleHarness harness)
    {
        StudioCanonicalJson? result = await harness.PreviewAsync(
            "agent.studio.command.agent.session.delete.preview",
            SessionPreviewRequest(harness.SessionTargetChecksum, harness.Authority.Checksum));
        StudioCanonicalJson preview = Assert.IsType<StudioCanonicalJson>(result);
        StudioEndpointContract endpoint = harness.Runtime.Endpoints.Single(value =>
            value.EndpointId == "agent.studio.command.agent.session.delete.preview");
        StudioL41JsonValidator.Require(preview, endpoint.ResultNodeId, harness.Runtime.Types);
        return preview;
    }

    private static async Task<StudioCanonicalJson> PreviewAgentAsync(LifecycleHarness harness)
    {
        StudioCanonicalJson? result = await harness.CreationProducer.PreviewAsync(
            harness.Invocation("agent.studio.command.agent.create.preview",
                AgentPreviewRequest(harness, AgentConfigJson)),
            CancellationToken.None);
        StudioCanonicalJson preview = Assert.IsType<StudioCanonicalJson>(result);
        StudioEndpointContract endpoint = harness.Runtime.Endpoints.Single(value =>
            value.EndpointId == "agent.studio.command.agent.create.preview");
        StudioL41JsonValidator.Require(preview, endpoint.ResultNodeId, harness.Runtime.Types);
        return preview;
    }

    private static async Task ExecuteAgentAsync(
        LifecycleHarness harness, StudioCanonicalJson preview)
    {
        StudioCanonicalJson request = AgentExecuteRequest(
            harness, preview, PreviewChecksum(preview));
        StudioCanonicalJson? result = await harness.CreationProducer.ExecuteAsync(
            harness.Invocation("agent.studio.command.agent.create.execute", request),
            CancellationToken.None);
        StudioCanonicalJson value = Assert.IsType<StudioCanonicalJson>(result);
        StudioEndpointContract endpoint = harness.Runtime.Endpoints.Single(candidate =>
            candidate.EndpointId == "agent.studio.command.agent.create.execute");
        StudioL41JsonValidator.Require(value, endpoint.ResultNodeId, harness.Runtime.Types);
    }

    private static async Task<StudioCanonicalJson> PreviewThreadAsync(
        LifecycleHarness harness, bool recursive)
    {
        StudioCanonicalJson? result = await harness.PreviewAsync(
            "agent.studio.command.agent.thread.delete.preview",
            ThreadPreviewRequest(harness.ThreadTargetChecksum, harness.Authority.Checksum, recursive));
        StudioCanonicalJson preview = Assert.IsType<StudioCanonicalJson>(result);
        StudioEndpointContract endpoint = harness.Runtime.Endpoints.Single(value =>
            value.EndpointId == "agent.studio.command.agent.thread.delete.preview");
        StudioL41JsonValidator.Require(preview, endpoint.ResultNodeId, harness.Runtime.Types);
        return preview;
    }

    private static async Task ExecuteSessionAsync(
        LifecycleHarness harness, StudioCanonicalJson preview, string previewChecksum)
    {
        StudioCanonicalJson request = SessionExecuteRequest(
            preview, previewChecksum, harness.Authority.Checksum);
        StudioCanonicalJson? result = await harness.ExecuteAsync(
            "agent.studio.command.agent.session.delete.execute", request);
        Assert.NotNull(result);
    }

    private static async Task ExecuteThreadAsync(
        LifecycleHarness harness, StudioCanonicalJson preview, bool recursive)
    {
        StudioCanonicalJson request = ThreadExecuteRequest(
            preview, PreviewChecksum(preview), harness.Authority.Checksum, recursive);
        StudioCanonicalJson? result = await harness.ExecuteAsync(
            "agent.studio.command.agent.thread.delete.execute", request);
        Assert.NotNull(result);
    }

    private static StudioCanonicalJson SessionPreviewRequest(
        string targetChecksum, StudioSha256 responseAuthority)
    {
        var target = new
        {
            applicationId = ApplicationId,
            authorityChecksum = targetChecksum,
            kind = "agentSession",
            sessionId = SessionLifecycle.SessionId,
        };
        return Json(new
        {
            commandId = "agent.session.delete",
            input = new { },
            pageId = "session.detail",
            responseAuthorityChecksum = Hex(responseAuthority),
            target,
        });
    }

    private const string AgentConfigJson =
        "{\"name\":\"Created Agent\",\"systemInstructions\":\"Created from Studio.\",\"maxAgenticIterations\":6,\"maxSubAgentDepth\":2}";

    private static StudioCanonicalJson AgentPreviewRequest(
        LifecycleHarness harness, string configJson)
    {
        var target = new
        {
            applicationId = ApplicationId,
            authorityChecksum = Hex(new StudioApplicationResource(ApplicationId).AuthorityChecksum),
            kind = "application",
        };
        return Json(new
        {
            commandId = "agent.create",
            input = new
            {
                agentId = DefinitionLifecycle.DefinitionId,
                configJson,
            },
            pageId = "agent.creation",
            responseAuthorityChecksum = Hex(harness.Authority.Checksum),
            target,
        });
    }

    private static StudioCanonicalJson AgentExecuteRequest(
        LifecycleHarness harness, StudioCanonicalJson preview, string previewChecksum,
        string purposeId = AgentStudioCreationContracts.ConfirmationId)
    {
        using JsonDocument document = JsonDocument.Parse(preview.ToArray());
        var target = new
        {
            applicationId = ApplicationId,
            authorityChecksum = Hex(new StudioApplicationResource(ApplicationId).AuthorityChecksum),
            kind = "application",
        };
        return Json(new
        {
            acknowledgements = new[]
            {
                new
                {
                    purposeId,
                    impactId = AgentStudioCreationContracts.ImpactId,
                    previewChecksum,
                },
            },
            commandId = "agent.create",
            freshAuthentication = (string?)null,
            pageId = "agent.creation",
            preview = document.RootElement.Clone(),
            requestIdentity = "create-agent-1",
            responseAuthorityChecksum = Hex(harness.Authority.Checksum),
            target,
        });
    }

    private static StudioCanonicalJson ThreadPreviewRequest(
        string targetChecksum, StudioSha256 responseAuthority, bool recursive)
    {
        var target = new
        {
            applicationId = ApplicationId,
            authorityChecksum = targetChecksum,
            kind = "agentThread",
            sessionId = SessionLifecycle.SessionId,
            threadId = ThreadLifecycle.ThreadId,
        };
        return Json(new
        {
            commandId = "agent.thread.delete",
            input = new { recursive },
            pageId = "thread.detail",
            responseAuthorityChecksum = Hex(responseAuthority),
            target,
        });
    }

    private static StudioCanonicalJson SessionExecuteRequest(
        StudioCanonicalJson preview, string previewChecksum, StudioSha256 responseAuthority,
        string purposeId = "confirm.session.delete", string impactId = "impact.session.delete",
        bool addExtraAcknowledgementMember = false, string? freshAuthentication = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")
    {
        using JsonDocument document = JsonDocument.Parse(preview.ToArray());
        var target = new
        {
            applicationId = ApplicationId,
            authorityChecksum = Hex(new StudioAgentSessionResource(ApplicationId, SessionLifecycle.SessionId).AuthorityChecksum),
            kind = "agentSession",
            sessionId = SessionLifecycle.SessionId,
        };
        var acknowledgement = new Dictionary<string, object?>
        {
            ["purposeId"] = purposeId,
            ["impactId"] = impactId,
            ["previewChecksum"] = previewChecksum,
        };
        if (addExtraAcknowledgementMember) acknowledgement["unexpected"] = true;
        return Json(new Dictionary<string, object?>
        {
            ["acknowledgements"] = new[] { acknowledgement },
            ["commandId"] = "agent.session.delete",
            ["freshAuthentication"] = freshAuthentication,
            ["pageId"] = "session.detail",
            ["preview"] = document.RootElement.Clone(),
            ["requestIdentity"] = "delete-session-1",
            ["responseAuthorityChecksum"] = Hex(responseAuthority),
            ["target"] = target,
        });
    }

    private static StudioCanonicalJson ThreadExecuteRequest(
        StudioCanonicalJson preview, string previewChecksum, StudioSha256 responseAuthority,
        bool recursive)
    {
        using JsonDocument document = JsonDocument.Parse(preview.ToArray());
        var target = new
        {
            applicationId = ApplicationId,
            authorityChecksum = Hex(new StudioAgentThreadResource(
                ApplicationId, SessionLifecycle.SessionId, ThreadLifecycle.ThreadId).AuthorityChecksum),
            kind = "agentThread",
            sessionId = SessionLifecycle.SessionId,
            threadId = ThreadLifecycle.ThreadId,
        };
        return Json(new
        {
            acknowledgements = new[]
            {
                new
                {
                    purposeId = "confirm.thread.delete",
                    impactId = "impact.thread.delete",
                    previewChecksum,
                },
            },
            commandId = "agent.thread.delete",
            freshAuthentication = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa",
            pageId = "thread.detail",
            preview = document.RootElement.Clone(),
            requestIdentity = "delete-thread-1",
            responseAuthorityChecksum = Hex(responseAuthority),
            target,
        });
    }

    private static string PreviewChecksum(StudioCanonicalJson preview)
    {
        using JsonDocument document = JsonDocument.Parse(preview.ToArray());
        return document.RootElement.GetProperty("previewChecksum").GetString()!;
    }

    private static StudioCanonicalJson Json(object value) =>
        StudioCanonicalJson.Create(JsonSerializer.SerializeToUtf8Bytes(value), 1_048_576);

    private static string Hex(StudioSha256 value) =>
        Convert.ToHexString(value.ToArray()).ToLowerInvariant();

    private static LifecycleHarness Harness(
        SessionLifecycle? sessions = null, ThreadLifecycle? threads = null,
        DefinitionLifecycle? definitions = null)
    {
        sessions ??= new SessionLifecycle();
        threads ??= new ThreadLifecycle();
        definitions ??= new DefinitionLifecycle();
        var services = new ServiceCollection();
        services.AddSingleton<TimeProvider>(new ManualTimeProvider(
            new DateTimeOffset(2026, 11, 16, 12, 0, 0, TimeSpan.Zero)));
        services.AddSingleton<IAuthorizationService, AllowAuthorization>();
        services.AddSingleton<IAgentSessionService>(sessions);
        services.AddSingleton<IAgentThreadService>(threads);
        services.AddSingleton<IAgentDefinitionService>(definitions);
        ServiceProvider provider = services.BuildServiceProvider();

        HPDBaseStudioAuthoritySnapshot authoritySnapshot = BaseAuthority();
        StudioModuleRegistration module = AgentStudioModuleRegistry.Create(authoritySnapshot);
        StudioApplicationGraph graph = StudioApplicationGraph.Create(ApplicationId, 1,
            [
                module,
                HPDBaseStudioModuleRegistry.Create(authoritySnapshot),
            ]);
        StudioModuleRuntimeContribution runtime =
            new AgentStudioRuntimeContributionFactory(provider, new NoopInspection()).Create(module);
        var http = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, "studio.tester")], "test")),
        };
        DateTimeOffset issued = provider.GetRequiredService<TimeProvider>().GetUtcNow();
        StudioSessionObservation session = StudioSessionObservation.Create(
            1, Digest(1), "control-plane", Digest(2), issued, issued.AddHours(1), Digest(3));
        StudioTransportAuthorization transport = StudioTransportAuthorization.Create(
            session, StudioTransportPurpose.CommandPreview, issued.AddMinutes(5));
        StudioBootstrapRequest bootstrapRequest = StudioBootstrapRequest.Create(
            Digest(4), Digest(5), Digest(6), "en-US", [StudioBrowserCapability.History]);
        StudioResponseAuthority authority = StudioResponseAuthority.Create(
            1, session.SessionChecksum, session.ProtectedScopeChecksum, graph.Generation, graph.Checksum,
            1, Digest(7), 1, Digest(8), [], issued, session.ExpiresAtUtc, [issued.AddMinutes(5)]);
        var bootstrap = new StudioBootstrapInvocation(http, graph, transport, bootstrapRequest);
        return new LifecycleHarness(provider, bootstrap, authority,
            new AgentStudioLifecycleCommandProducer(
                provider, AgentStudioExperimentContracts.DeleteSession),
            new AgentStudioLifecycleCommandProducer(
                provider, AgentStudioExperimentContracts.DeleteThread),
            new AgentStudioCreationCommandProducer(provider),
            (ManualTimeProvider)provider.GetRequiredService<TimeProvider>(),
            Hex(new StudioAgentSessionResource(ApplicationId, SessionLifecycle.SessionId).AuthorityChecksum),
            Hex(new StudioAgentThreadResource(
                ApplicationId, SessionLifecycle.SessionId, ThreadLifecycle.ThreadId).AuthorityChecksum),
            runtime);
    }

    private static HPDBaseStudioAuthoritySnapshot BaseAuthority()
    {
        var services = new ServiceCollection();
        services.AddHPDBase(builder =>
        {
            builder.ConfigureSchema(options => options.ApplicationId = ApplicationId);
            foreach (string id in new[]
                     {
                         "base.studio.action.discover", "base.studio.action.execute",
                         "base.studio.action.preview", "base.studio.bootstrap.read",
                         "base.studio.diagnostics.inspect", "base.studio.invalidation.subscribe",
                         "base.studio.receipt.discover", "base.studio.receipt.inspect",
                         "base.studio.resource.discover", "base.studio.resource.inspect",
                         "base.studio.resource.links", "base.studio.resource.search",
                     })
            {
                builder.AddStaticGrantAuthority(
                    new BaseGrantAuthorityDefinition
                    {
                        Id = id,
                        Version = 1,
                        OwningModuleId = "base",
                        SourceContractId = "base.studio.fixed-grant",
                        SourceContractVersion = 1,
                    },
                    new AccessGrant
                    {
                        Id = id,
                        ApplicationId = ApplicationId,
                        ModuleId = "base",
                        Audience = HPDBaseEndpointAudience.ControlPlane,
                        Subject = new AccessSubject { Kind = AccessSubjectKind.User, Id = "operator" },
                        Action = id,
                        Scope = new ResourceScope { Kind = ResourceScopeKind.Runtime },
                    });
            }
        });
        using ServiceProvider provider = services.BuildServiceProvider();
        return provider.GetRequiredService<HPDBaseStudioAuthoritySnapshot>();
    }

    private static StudioSha256 Digest(byte seed) =>
        StudioSha256.FromDigest(Enumerable.Repeat(seed, 32).ToArray());

    private sealed record LifecycleHarness(
        ServiceProvider Services,
        StudioBootstrapInvocation Bootstrap,
        StudioResponseAuthority Authority,
        AgentStudioLifecycleCommandProducer SessionProducer,
        AgentStudioLifecycleCommandProducer ThreadProducer,
        AgentStudioCreationCommandProducer CreationProducer,
        ManualTimeProvider Time,
        string SessionTargetChecksum,
        string ThreadTargetChecksum,
        StudioModuleRuntimeContribution Runtime)
    {
        public StudioProducerInvocation Invocation(string methodId, StudioCanonicalJson request) =>
            new(Bootstrap, Authority, methodId, request);

        public async Task<StudioCanonicalJson?> PreviewAsync(
            string methodId, StudioCanonicalJson request) =>
            await Producer(methodId).PreviewAsync(Invocation(methodId, request), CancellationToken.None);

        public async Task<StudioCanonicalJson?> ExecuteAsync(
            string methodId, StudioCanonicalJson request) =>
            await Producer(methodId).ExecuteAsync(Invocation(methodId, request), CancellationToken.None);

        private AgentStudioLifecycleCommandProducer Producer(string methodId) =>
            methodId.Contains("thread", StringComparison.Ordinal) ? ThreadProducer : SessionProducer;
    }

    private sealed class NoopInspection : IAgentStudioInspectionAuthority
    {
        public ValueTask<StudioFrameworkSurfaceResponse?> ObserveAsync(
            string operationId, string relativePath, string query, string applicationId,
            ClaimsPrincipal principal, CancellationToken cancellationToken) =>
            ValueTask.FromResult<StudioFrameworkSurfaceResponse?>(null);

        public ValueTask<StudioFrameworkSurfaceResponse?> PreflightAsync(
            string operationId, string relativePath, ReadOnlyMemory<byte> requestBody,
            string applicationId, CancellationToken cancellationToken) =>
            ValueTask.FromResult<StudioFrameworkSurfaceResponse?>(null);

        public ValueTask<bool> ExistsAsync(
            StudioResourceIdentity resource, CancellationToken cancellationToken) =>
            ValueTask.FromResult(false);
    }

    private sealed class ManualTimeProvider(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now = _now.Add(duration);
    }

    private sealed class AllowAuthorization : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements) =>
            Task.FromResult(AuthorizationResult.Success());

        public Task<AuthorizationResult> AuthorizeAsync(
            ClaimsPrincipal user, object? resource, string policyName) =>
            Task.FromResult(AuthorizationResult.Success());
    }

    private sealed class SessionLifecycle : IAgentSessionService
    {
        public const string SessionId = "session-1";
        public int DeleteCalls { get; private set; }
        public bool Exists { get; private set; } = true;
        public bool ThrowOnDelete { get; init; }

        public Task<SessionDto> CreateSessionAsync(
            CreateSessionRequest? request = null, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<SessionDto>> SearchSessionsAsync(
            SearchSessionsRequest? request = null, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<SessionDto>>([]);

        public Task<SessionDto?> GetSessionAsync(
            string sessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult<SessionDto?>(Exists && sessionId == SessionId
                ? new SessionDto(SessionId, DateTime.UtcNow, DateTime.UtcNow, null)
                : null);

        public Task<SessionDto?> UpdateSessionAsync(
            string sessionId, UpdateSessionRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<bool> DeleteSessionAsync(
            string sessionId, CancellationToken cancellationToken = default)
        {
            DeleteCalls++;
            if (ThrowOnDelete) throw new InvalidOperationException("simulated storage failure");
            if (!Exists || sessionId != SessionId) return Task.FromResult(false);
            Exists = false;
            return Task.FromResult(true);
        }
    }

    private sealed class DefinitionLifecycle : IAgentDefinitionService
    {
        public const string DefinitionId = "created-agent";
        public bool Exists { get; set; }
        public string AgentId { get; private set; } = "";
        public string Name { get; private set; } = "";

        public Task<AgentServiceResult<StoredAgentDto>> CreateAgentAsync(
            CreateAgentRequest request, CancellationToken cancellationToken = default)
        {
            if (Exists) return Task.FromResult(AgentServiceResult<StoredAgentDto>.Conflict);
            Exists = true;
            AgentId = request.AgentId;
            Name = request.Config.Name;
            DateTime now = DateTime.UtcNow;
            return Task.FromResult(AgentServiceResult<StoredAgentDto>.Success(
                new StoredAgentDto(request.AgentId, request.Config.Name, request.Config, now, now, null)));
        }

        public Task<IReadOnlyList<AgentSummaryDto>> ListAgentsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AgentSummaryDto>>([]);

        public Task<StoredAgentDto?> GetAgentAsync(
            string agentId, CancellationToken cancellationToken = default)
        {
            if (!Exists || agentId != DefinitionId) return Task.FromResult<StoredAgentDto?>(null);
            DateTime now = DateTime.UtcNow;
            return Task.FromResult<StoredAgentDto?>(new StoredAgentDto(
                DefinitionId, Name, new AgentConfig { Name = Name }, now, now, null));
        }

        public Task<AgentServiceResult<StoredAgentDto>> UpdateAgentAsync(
            string agentId, UpdateAgentRequest request, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentServiceResult> DeleteAgentAsync(
            string agentId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private sealed class ThreadLifecycle : IAgentThreadService
    {
        private readonly bool _hasChildren;
        private readonly bool _recursiveEnabled;

        public ThreadLifecycle(bool hasChildren = false, bool recursiveEnabled = false)
        {
            _hasChildren = hasChildren;
            _recursiveEnabled = recursiveEnabled;
        }

        public const string ThreadId = "thread-1";
        public int DeleteCalls { get; private set; }
        public bool LastRecursive { get; private set; }
        public bool Exists { get; private set; } = true;

        public Task<AgentServiceResult<IReadOnlyList<ThreadDto>>> ListThreadsAsync(
            string sessionId, CancellationToken cancellationToken = default) =>
            Task.FromResult(AgentServiceResult<IReadOnlyList<ThreadDto>>.Success([Thread()]));

        public Task<AgentServiceResult<ThreadGraphDto>> GetThreadGraphAsync(
            string sessionId, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentServiceResult<IReadOnlyList<SubAgentDto>>> ListSubAgentsAsync(
            string sessionId, string threadId, CancellationToken cancellationToken = default) =>
            Task.FromResult(AgentServiceResult<IReadOnlyList<SubAgentDto>>.Success([]));

        public Task<AgentServiceResult<ThreadDto>> GetThreadAsync(
            string sessionId, string threadId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Exists && sessionId == SessionLifecycle.SessionId && threadId == ThreadId
                ? AgentServiceResult<ThreadDto>.Success(Thread())
                : AgentServiceResult<ThreadDto>.NotFound);

        public Task<AgentServiceResult<ThreadDto>> CreateThreadAsync(
            string agentId, string sessionId, CreateThreadRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentServiceResult<ThreadForkResultDto>> ForkThreadAsync(
            string agentId, string sessionId, string threadId, ForkThreadRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentServiceResult<ThreadDto>> UpdateThreadAsync(
            string sessionId, string threadId, UpdateThreadRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<AgentServiceResult> DeleteThreadAsync(
            string sessionId, string threadId, bool recursive = false,
            CancellationToken cancellationToken = default)
        {
            DeleteCalls++;
            LastRecursive = recursive;
            if (!Exists || sessionId != SessionLifecycle.SessionId || threadId != ThreadId)
                return Task.FromResult(AgentServiceResult.NotFound);
            if (_hasChildren && !recursive)
                return Task.FromResult(AgentServiceResult.Validation("HasChildren", "children exist"));
            if (_hasChildren && !_recursiveEnabled)
                return Task.FromResult(AgentServiceResult.Validation(
                    "RecursiveDeleteDisabled", "recursive deletion is disabled"));
            Exists = false;
            return Task.FromResult(AgentServiceResult.Success);
        }

        private ThreadDto Thread() => new(
            ThreadId, SessionLifecycle.SessionId, "agent-1", "Thread 1", null, null, null, null,
            DateTime.UtcNow, DateTime.UtcNow, 0, null, null, _hasChildren ? 1 : 0);
    }
}
