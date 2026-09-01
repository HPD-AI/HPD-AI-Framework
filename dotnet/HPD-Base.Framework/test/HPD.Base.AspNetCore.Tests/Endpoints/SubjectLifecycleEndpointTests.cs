using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;

namespace HPD.Base.AspNetCore.Tests.Endpoints;

public sealed class SubjectLifecycleEndpointTests
{
    [Fact]
    public async Task Realtime_hint_wakes_the_durable_feed_without_disclosing_the_fact()
    {
        const string applicationId = "lifecycle.realtime.application";
        const string consumerId = "profiles.lifecycle";
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthorizationBuilder().AddPolicy("application", policy => policy.RequireAssertion(_ => true));
        builder.Services.AddSingleton<IBaseHttpPrincipalMapper, HeaderPrincipalMapper>();
        builder.Services.AddHPDBase(hpd =>
        {
            hpd.ConfigureSchema(options => options.ApplicationId = applicationId)
                .ConfigureInMemoryStore(options =>
                {
                    options.StoreId = "lifecycle-realtime";
                    options.Collections = [HttpPrivateSubjectRecord.Collection.Definition];
                })
                .ConfigureTokenProtection(options => options.ActiveKey = new BaseOpaqueTokenKey
                {
                    Id = 1,
                    Key = System.Security.Cryptography.SHA256.HashData("lifecycle-realtime-persistent-token-key"u8),
                    IssueNotBefore = DateTimeOffset.UnixEpoch,
                })
                .AddRealtime()
                .AddAspNetCore();
            hpd.AddPolicyAuthority<RealtimeAllowPolicy>(new BasePolicyAuthorityDefinition
            {
                Id = "lifecycle.realtime.policy", Version = 1, OwningModuleId = "tests",
                EvaluatorContractId = "lifecycle.realtime.policy", EvaluatorContractVersion = 1,
                CompositionOrder = 0,
            });
            AddStaticGrant(hpd, "system.private", new AccessGrant
            {
                Id = "system.private",
                Subject = new AccessSubject { Kind = AccessSubjectKind.ServicePrincipal, Id = "profiles-worker" },
                Action = "*", Effect = GrantEffect.Allow,
                Scope = new ResourceScope { Kind = ResourceScopeKind.Runtime },
            });
            AddLifecycleGrant(hpd, consumerId + ".read", applicationId, consumerId);
            AddLifecycleGrant(hpd, "base.subjectLifecycle.feed.read", applicationId, "base.subjectLifecycle.feed.read");
            hpd.AddCollection(HttpPrivateSubjectRecord.Collection);
            hpd.AddExportedSubject(HttpExportedSubject.HPDBaseSubjectRegistration).AddSubjectLifecycleConsumer(new()
            {
                Id = consumerId, Version = 1, OwningModuleId = "profiles", Audience = BaseSubjectLifecycleConsumerAudience.Service,
                ContractId = "http.exported-subject", ContractVersion = 1,
                ObservedStates = [BaseSubjectLifecycleState.Inactive], DeliveryGrantId = consumerId + ".read",
                Limits = new() { MaximumFactsPerPage = 64, MaximumResultBytes = 131072, MaximumCheckpointLag = TimeSpan.FromDays(1), ReadTimeout = TimeSpan.FromSeconds(5) },
            });
        });

        await using WebApplication app = builder.Build();
        app.MapHPDBaseApplicationApi(new()
        {
            AuthorizationPolicy = "application", MapRecords = false, MapRegisteredReads = false,
            MapSubjectLifecycle = true, MapRealtime = true,
        });
        await app.StartAsync();

        WebSocketClient webSocketClient = app.GetTestServer().CreateWebSocketClient();
        webSocketClient.ConfigureRequest = request => request.Headers["X-Test-Principal"] = "service";
        using WebSocket socket = await webSocketClient
            .ConnectAsync(new Uri("ws://localhost" + BaseRealtimeRoutes.WebSocketV2), CancellationToken.None);
        BaseRealtimeWelcomeMessage welcome = (BaseRealtimeWelcomeMessage)await ReceiveRealtimeAsync(socket);
        await SendRealtimeAsync(socket, new BaseRealtimeJoinMessage
        {
            ConnectionId = welcome.ConnectionId, ConnectionEpoch = welcome.ConnectionEpoch, Ref = "subject-hints",
            Channel = new BaseRealtimeSubjectLifecycleHintRequest { ConsumerId = consumerId, ConsumerVersion = 1 },
        });
        BaseRealtimeServerMessage joinResponse = await ReceiveRealtimeAsync(socket);
        joinResponse.Should().BeOfType<BaseRealtimeJoinedMessage>(joinResponse is BaseRealtimeErrorMessage error ? error.Error.Code : null);
        BaseRealtimeJoinedMessage joined = (BaseRealtimeJoinedMessage)joinResponse;
        joined.Delivery.Should().Be("lifecycle-hints-non-authoritative");

        IBaseRecordRuntime runtime = app.Services.GetRequiredService<IBaseRecordRuntime>();
        PrincipalContext principal = ServicePrincipal();
        OperationResult<RecordEnvelope> created = await runtime.CreateAsync(
            HttpPrivateSubjectRecord.Collection.Id,
            new RecordCreateRequest
            {
                RequestedId = RecordId.Create("subject-1"),
                Payload = FieldPayload(("active", true), ("tombstoned", false)),
            }, principal, Operation(applicationId, BaseOperationKind.Create, HttpPrivateSubjectRecord.Collection.Id));
        created.IsSuccess().Should().BeTrue(created.Error?.Code);
        OperationResult<RecordEnvelope> changed = await runtime.PatchAsync(
            HttpPrivateSubjectRecord.Collection.Id, RecordId.Create("subject-1"),
            new RecordPatchRequest { Patch = FieldPayload(("active", false)), RemovedFieldIds = [] },
            principal, Operation(applicationId, BaseOperationKind.Patch, HttpPrivateSubjectRecord.Collection.Id));
        changed.IsSuccess().Should().BeTrue(changed.Error?.Code);

        BaseRealtimeSubjectLifecycleHintMessage hint = (BaseRealtimeSubjectLifecycleHintMessage)await ReceiveRealtimeAsync(socket);
        hint.Ref.Should().Be("subject-hints");
        hint.ChannelEpoch.Should().Be(joined.ChannelEpoch);
        hint.Checkpoint.Should().NotBeNullOrWhiteSpace();
        JsonSerializer.SerializeToElement(hint).TryGetProperty("fact", out _).Should().BeFalse();

        BaseSession session = app.Services.GetRequiredService<IBaseSessionFactory>().For(principal);
        BaseInstalledSubjectLifecycleConsumer installed = app.Services.GetRequiredService<BaseSubjectLifecycleRegistry>().All.Single();
        BaseResult<BaseUntypedSubjectLifecyclePage> page = await app.Services.GetRequiredService<IBaseSubjectLifecycleRuntime>()
            .ReadUntypedAsync(session, installed, null, 1, CancellationToken.None);
        BaseSubjectLifecycleFact durableFact = page.RequireValue().Facts.Single();
        durableFact.Kind.Should().Be(BaseSubjectLifecycleFactKind.Transitioned);
        durableFact.Transitioned!.CurrentState.Should().Be(BaseSubjectLifecycleState.Inactive);
    }

    [Fact]
    public async Task Zero_consumer_graph_materializes_no_lifecycle_worker_routes()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthorizationBuilder().AddPolicy("application", policy => policy.RequireAssertion(_ => true));
        builder.Services.AddSingleton<IBaseHttpPrincipalMapper, HeaderPrincipalMapper>();
        builder.Services.AddHPDBase(hpd =>
        {
            hpd.ConfigureSchema(options => options.ApplicationId = "lifecycle.zero-consumer.application")
                .ConfigureInMemoryStore(options => { options.StoreId = "lifecycle-zero-consumer"; options.Collections = [HttpPrivateSubjectRecord.Collection.Definition]; })
                .ConfigureTokenProtection(options => options.ActiveKey = new BaseOpaqueTokenKey
                {
                    Id = 1, Key = System.Security.Cryptography.SHA256.HashData("lifecycle-zero-consumer-token-key"u8),
                    IssueNotBefore = DateTimeOffset.UnixEpoch,
                })
                .AddAspNetCore();
            hpd.AddCollection(HttpPrivateSubjectRecord.Collection);
            hpd.AddExportedSubject(HttpExportedSubject.HPDBaseSubjectRegistration);
        });
        await using WebApplication app = builder.Build();
        app.MapHPDBaseApplicationApi(new() { AuthorizationPolicy = "application", MapRecords = false, MapRegisteredReads = false, MapSubjectLifecycle = true });
        await app.StartAsync();
        HttpClient client = app.GetTestClient();
        foreach (string route in new[]
        {
            "/base/subject-lifecycle/feed/read",
            "/base/subject-lifecycle/feed/checkpoints",
        })
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, route);
            request.Headers.Add("X-Test-Principal", "service");
            using HttpResponseMessage response = await client.SendAsync(request);
            response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        }
    }

    [Fact]
    public async Task Service_generation_contains_worker_handle_and_browser_principal_cannot_open_worker_routes()
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthorizationBuilder().AddPolicy("application", policy => policy.RequireAssertion(_ => true));
        builder.Services.AddSingleton<IBaseHttpPrincipalMapper, HeaderPrincipalMapper>();
        builder.Services.AddHPDBase(hpd =>
        {
            hpd.ConfigureSchema(options => options.ApplicationId = "lifecycle.http.application")
                .ConfigureInMemoryStore(options => { options.StoreId = "lifecycle-http"; options.Collections = [HttpPrivateSubjectRecord.Collection.Definition]; })
                .ConfigureTokenProtection(options => options.ActiveKey = new BaseOpaqueTokenKey
                {
                    Id = 1, Key = System.Security.Cryptography.SHA256.HashData("lifecycle-http-persistent-token-key"u8),
                    IssueNotBefore = DateTimeOffset.UnixEpoch,
                })
                .AddAspNetCore();
            hpd.AddPolicyAuthority<RealtimeAllowPolicy>(new BasePolicyAuthorityDefinition
            {
                Id = "lifecycle.http.policy", Version = 1, OwningModuleId = "tests",
                EvaluatorContractId = "lifecycle.http.policy", EvaluatorContractVersion = 1,
                CompositionOrder = 0,
            });
            AddLifecycleGrant(hpd, "profiles.lifecycle.read", "lifecycle.http.application", "profiles.lifecycle");
            AddLifecycleGrant(hpd, "base.subjectLifecycle.feed.read", "lifecycle.http.application", "base.subjectLifecycle.feed.read");
            hpd.AddCollection(HttpPrivateSubjectRecord.Collection);
            hpd.AddExportedSubject(HttpExportedSubject.HPDBaseSubjectRegistration).AddSubjectLifecycleConsumer(new()
            {
                Id = "profiles.lifecycle", Version = 1, OwningModuleId = "profiles", Audience = BaseSubjectLifecycleConsumerAudience.Service,
                ContractId = "http.exported-subject", ContractVersion = 1, ObservedStates = [BaseSubjectLifecycleState.Inactive, BaseSubjectLifecycleState.Tombstoned, BaseSubjectLifecycleState.Retired],
                DeliveryGrantId = "profiles.lifecycle.read", Limits = new() { MaximumFactsPerPage = 64, MaximumResultBytes = 131072, MaximumCheckpointLag = TimeSpan.FromDays(1), ReadTimeout = TimeSpan.FromSeconds(5) },
            }).AddSubjectLifecycleConsumer(new()
            {
                Id = "private.lifecycle", Version = 1, OwningModuleId = "private", Audience = BaseSubjectLifecycleConsumerAudience.Service,
                ContractId = "http.exported-subject", ContractVersion = 1, ObservedStates = [BaseSubjectLifecycleState.Retired],
                DeliveryGrantId = "private.lifecycle.read", Limits = new() { MaximumFactsPerPage = 64, MaximumResultBytes = 131072, MaximumCheckpointLag = TimeSpan.FromDays(1), ReadTimeout = TimeSpan.FromSeconds(5) },
            });
        });
        await using WebApplication app = builder.Build();
        app.MapHPDBaseApplicationApi(new() { AuthorizationPolicy = "application", MapRecords = false, MapRegisteredReads = false, MapClientGeneration = true, MapSubjectLifecycle = true });
        await app.StartAsync(); HttpClient client = app.GetTestClient();

        using var generationRequest = new HttpRequestMessage(HttpMethod.Get, "/base/client-generation");
        generationRequest.Headers.Add("X-Test-Principal", "service");
        using HttpResponseMessage generated = await client.SendAsync(generationRequest);
        generated.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument snapshot = JsonDocument.Parse(await generated.Content.ReadAsByteArrayAsync());
        snapshot.RootElement.GetProperty("application").GetProperty("audience").GetString().Should().Be("service");
        JsonElement consumer = snapshot.RootElement.GetProperty("subjectLifecycleConsumers").EnumerateArray().Single();
        consumer.GetProperty("id").GetString().Should().Be("profiles.lifecycle");
        consumer.GetProperty("checksum").GetString().Should().MatchRegex("^[0-9a-f]{64}$");
        consumer.GetProperty("audience").GetString().Should().Be("service");
        Dictionary<string, string> lifecycleTypes = snapshot.RootElement.GetProperty("schema").GetProperty("types").EnumerateArray()
            .ToDictionary(static value => value.GetProperty("id").GetString()!, static value => value.GetProperty("node").GetProperty("kind").GetString()!, StringComparer.Ordinal);
        lifecycleTypes["base.subjectLifecycle.authorityEpoch"].Should().Be("subject-lifecycle-authority-epoch");
        lifecycleTypes["base.subjectLifecycle.incarnation"].Should().Be("subject-lifecycle-incarnation");
        lifecycleTypes["base.subjectLifecycle.cursor"].Should().Be("subject-lifecycle-cursor");
        lifecycleTypes["base.subjectLifecycle.checkpoint"].Should().Be("subject-lifecycle-checkpoint");
        lifecycleTypes["base.subjectLifecycle.page"].Should().Be("object");
        Dictionary<string, (string Category, bool Retryable)> lifecycleErrors = snapshot.RootElement.GetProperty("errors").EnumerateArray()
            .ToDictionary(static value => value.GetProperty("code").GetString()!, static value => (value.GetProperty("category").GetString()!, value.GetProperty("retryable").GetBoolean()), StringComparer.Ordinal);
        lifecycleErrors[BaseSubjectErrorCodes.CursorOvertaken].Should().Be(("conflict", false));
        lifecycleErrors[BaseSubjectErrorCodes.LifecycleCapacityExceeded].Should().Be(("store", true));
        lifecycleErrors[BaseSubjectErrorCodes.LifecycleProviderContractInvalid].Should().Be(("capability", false));

        using var browserGenerationRequest = new HttpRequestMessage(HttpMethod.Get, "/base/client-generation");
        browserGenerationRequest.Headers.Add("X-Test-Principal", "application");
        using HttpResponseMessage browserGeneration = await client.SendAsync(browserGenerationRequest);
        browserGeneration.StatusCode.Should().Be(HttpStatusCode.OK);
        using JsonDocument browserSnapshot = JsonDocument.Parse(await browserGeneration.Content.ReadAsByteArrayAsync());
        browserSnapshot.RootElement.GetProperty("subjectLifecycleConsumers").GetArrayLength().Should().Be(0);
        browserSnapshot.RootElement.GetProperty("endpoints").EnumerateArray().Select(value => value.GetProperty("operation").GetString()).Should()
            .NotContain(["SubjectLifecycleRead", "SubjectLifecycleCheckpoint"]);
        browserSnapshot.RootElement.GetProperty("capabilities").EnumerateArray().Select(value => value.GetProperty("id").GetString()).Should()
            .NotContain([HPDBaseCapabilities.SubjectLifecycleFeedRead, HPDBaseCapabilities.SubjectLifecycleFeedCheckpoint]);

        using var browserRead = new HttpRequestMessage(HttpMethod.Post, "/base/subject-lifecycle/feed/read")
        { Content = new StringContent("{\"consumerId\":\"profiles.lifecycle\",\"consumerVersion\":1,\"contractId\":\"http.exported-subject\",\"contractVersion\":1,\"take\":1}", Encoding.UTF8, "application/json") };
        browserRead.Headers.Add("X-Test-Principal", "application");
        using HttpResponseMessage denied = await client.SendAsync(browserRead);
        denied.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        foreach (string invalidBody in new[]
        {
            "{\"consumerId\":\"profiles.lifecycle\",\"consumerId\":\"profiles.lifecycle\",\"consumerVersion\":1,\"contractId\":\"http.exported-subject\",\"contractVersion\":1,\"take\":1}",
            "{\"consumerId\":\"profiles.lifecycle\",\"consumerVersion\":1,\"contractId\":\"http.exported-subject\",\"contractVersion\":1,\"take\":1,\"checkpoint\":\"forbidden\"}",
        })
        {
            using var invalidRead = new HttpRequestMessage(HttpMethod.Post, "/base/subject-lifecycle/feed/read")
            { Content = new StringContent(invalidBody, Encoding.UTF8, "application/json") };
            invalidRead.Headers.Add("X-Test-Principal", "service");
            using HttpResponseMessage invalid = await client.SendAsync(invalidRead);
            invalid.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        }

        using var substitutedIdentity = new HttpRequestMessage(HttpMethod.Post, "/base/subject-lifecycle/feed/checkpoints")
        {
            Content = new StringContent("""{"consumerId":"profiles.lifecycle","consumerVersion":1,"contractId":"http.exported-subject","contractVersion":1,"checkpoint":"ponmlkjihgfedcba","identity":{"scope":"subject-lifecycle:profiles.lifecycle","operation":"subjectLifecycle.advance","idempotencyKey":"aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa","fingerprint":"AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA="}}""", Encoding.UTF8, "application/json"),
        };
        substitutedIdentity.Headers.Add("X-Test-Principal", "service");
        substitutedIdentity.Headers.Add(BaseHttpHeaders.IdempotencyKey, new string('b', 64));
        using HttpResponseMessage identityDenied = await client.SendAsync(substitutedIdentity);
        identityDenied.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        using JsonDocument deniedProblem = JsonDocument.Parse(await identityDenied.Content.ReadAsByteArrayAsync());
        deniedProblem.RootElement.GetProperty("detail").GetString().Should().Be("The subject lifecycle contract is invalid.");
    }


    private sealed class HeaderPrincipalMapper : IBaseHttpPrincipalMapper
    {
        public ValueTask<PrincipalContext> MapAsync(HttpContext context, HPDBaseEndpointDescriptor endpoint, CancellationToken cancellationToken = default)
        {
            bool service = string.Equals(context.Request.Headers["X-Test-Principal"], "service", StringComparison.Ordinal);
            return ValueTask.FromResult(new PrincipalContext { AuthenticationState = service ? PrincipalAuthenticationState.Service : PrincipalAuthenticationState.Authenticated, SubjectKind = service ? AccessSubjectKind.ServicePrincipal : AccessSubjectKind.User, SubjectId = service ? "profiles-worker" : "user" });
        }
    }

    private static void AddLifecycleGrant(HPDBaseBuilder builder, string id, string applicationId, string action) =>
        AddStaticGrant(builder, id, new AccessGrant
        {
            Id = id, ApplicationId = applicationId, ModuleId = "profiles", Audience = HPDBaseEndpointAudience.Application,
            Subject = new AccessSubject { Kind = AccessSubjectKind.ServicePrincipal, Id = "profiles-worker" },
            Action = action, Effect = GrantEffect.Allow,
            Scope = new ResourceScope
            {
                Kind = ResourceScopeKind.SubjectContract, SubjectContractId = "http.exported-subject",
                SubjectContractVersion = 1,
            },
        });

    private static void AddStaticGrant(HPDBaseBuilder builder, string id, AccessGrant grant) =>
        builder.AddStaticGrantAuthority(new BaseGrantAuthorityDefinition
        {
            Id = id, Version = 1, OwningModuleId = "tests",
            SourceContractId = "lifecycle.realtime.static-grant", SourceContractVersion = 1,
        }, grant);

    private static PrincipalContext ServicePrincipal() => new()
    {
        AuthenticationState = PrincipalAuthenticationState.Service,
        SubjectKind = AccessSubjectKind.ServicePrincipal,
        SubjectId = "profiles-worker",
    };

    private static OperationContext Operation(string applicationId, BaseOperationKind kind, string collectionId) => new()
    {
        ApplicationId = applicationId, Operation = kind, CollectionId = collectionId,
        Audience = HPDBaseEndpointAudience.Application, Mode = OperationMode.System,
    };

    private static RecordPayload FieldPayload(params (string Name, object Value)[] fields) => new()
    {
        Kind = RecordPayloadKind.FieldMap,
        Fields = fields.ToDictionary(static item => item.Name, static item => JsonSerializer.SerializeToElement(item.Value), StringComparer.Ordinal),
    };

    private static async Task SendRealtimeAsync(WebSocket socket, BaseRealtimeClientMessage message) =>
        await socket.SendAsync(JsonSerializer.SerializeToUtf8Bytes(message, HPDBaseRealtimeJsonSerializerContext.Default.BaseRealtimeClientMessage),
            WebSocketMessageType.Text, true, CancellationToken.None);

    private static async Task<BaseRealtimeServerMessage> ReceiveRealtimeAsync(WebSocket socket)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        byte[] buffer = new byte[64 * 1024];
        WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, timeout.Token);
        result.MessageType.Should().Be(WebSocketMessageType.Text);
        return JsonSerializer.Deserialize(buffer.AsSpan(0, result.Count), HPDBaseRealtimeJsonSerializerContext.Default.BaseRealtimeServerMessage)!;
    }

    private sealed class RealtimeAllowPolicy : IPolicyEvaluator
    {
        public ValueTask<PolicyDecision> EvaluateAsync(PolicyEvaluationRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(PolicyDecision.Allow());
    }
}
