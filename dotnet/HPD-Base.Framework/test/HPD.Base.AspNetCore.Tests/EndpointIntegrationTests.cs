using HPD.Base;
using Microsoft.AspNetCore.Http;
using System.Text.Json.Serialization;

namespace HPD.Base.AspNetCore.Tests;

public sealed class EndpointIntegrationTests
{
    [Fact]
    public async Task ManifestExpansionAndCollectionsAreServed()
    {
        await using var app = await TestBaseApp.CreateAsync();
        var client = app.GetTestClient();

        var manifestResponse = await client.GetAsync("/base/manifest");
        var manifest = await ReadJson<BaseManifest>(app, manifestResponse.Content);
        manifest.Should().NotBeNull();
        manifest!.Projections.Should().Contain(projection => projection.Id == "hpd.base.aspnetcore");

        var expanded = await client.GetAsync("/base/manifest?expand=schema,capabilities,health,diagnostics,collections");
        expanded.StatusCode.Should().Be(HttpStatusCode.OK);

        var collectionsResponse = await client.GetAsync("/base/collections");
        var collections = await ReadJson<CollectionDefinition[]>(app, collectionsResponse.Content);
        collections.Should().Contain(collection => collection.Id == "items");
    }

    [Fact]
    public async Task UnknownManifestExpandReturnsProblemDetails()
    {
        await using var app = await TestBaseApp.CreateAsync();
        var response = await app.GetTestClient().GetAsync("/base/manifest?expand=mystery");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("base.http.manifest.unknownExpand");
    }

    [Fact]
    public async Task RecordCrudRoutesDelegateThroughRuntime()
    {
        await using var app = await TestBaseApp.CreateAsync();
        var client = app.GetTestClient();

        var create = await client.PostAsync("/base/collections/items/records", JsonContent.Create(new RecordCreateRequest
        {
            Payload = TestBaseApp.Payload(("title", "hello"))
        }, HPDBaseJsonSerializerContext.Default.RecordCreateRequest));
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        create.Headers.Location.Should().NotBeNull();

        var created = await ReadJson<RecordEnvelope>(app, create.Content);
        created.Should().NotBeNull();

        var getResponse = await client.GetAsync($"/base/collections/items/records/{created!.Id.Value}");
        var get = await ReadJson<RecordEnvelope>(app, getResponse.Content);
        get!.Id.Should().Be(created.Id);

        var listResponse = await client.GetAsync("/base/collections/items/records?where[title]=hello");
        var list = await ReadJson<RecordPage>(app, listResponse.Content);
        list!.Items.Should().Contain(item => item.Id == created.Id);

        var patch = await client.PatchAsync($"/base/collections/items/records/{created.Id.Value}", JsonContent.Create(new RecordPatchRequest
        {
            Patch = TestBaseApp.Patch("title", "patched"),
            ExpectedRevision = created.Metadata.Revision
        }, HPDBaseJsonSerializerContext.Default.RecordPatchRequest));
        patch.StatusCode.Should().Be(HttpStatusCode.OK);

        var patched = await ReadJson<RecordEnvelope>(app, patch.Content);
        var delete = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/base/collections/items/records/{created.Id.Value}")
        {
            Content = JsonContent.Create(new RecordDeleteRequest
            {
                ExpectedRevision = patched!.Metadata.Revision,
                ReturnPrevious = true
            }, HPDBaseJsonSerializerContext.Default.RecordDeleteRequest)
        });

        delete.StatusCode.Should().Be(HttpStatusCode.OK);
        var deleted = await ReadJson<DeleteResult>(app, delete.Content);
        deleted!.Previous.Should().NotBeNull();
    }

    [Fact]
    public async Task DeferredRoutesAreAbsent()
    {
        await using var app = await TestBaseApp.CreateAsync();
        var client = app.GetTestClient();

        (await client.PutAsJsonAsync("/base/collections/items/records", new { })).StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        (await client.PostAsJsonAsync("/base/batch", new { })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/base/files/anything")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/base/graphql")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/base/openapi.json")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AtomicBatchIdempotencyHeaderReturnsCommittedThenDuplicate()
    {
        await using var app = await TestBaseApp.CreateAsync();
        HttpClient client = app.GetTestClient();
        var batch = new BaseRecordBatchRequest
        {
            Mode = BaseRecordBatchExecutionMode.Atomic,
            Operations =
            [
                new BaseRecordBatchItem
                {
                    ItemId = "create-1", CollectionId = "items", Kind = BaseRecordMutationKind.Create,
                    Create = new RecordCreateRequest { RequestedId = new RecordId("atomic-http-1"), Payload = TestBaseApp.Payload(("title", "once")) },
                }
            ],
        };
        async Task<HttpResponseMessage> SendAsync()
        {
            var message = new HttpRequestMessage(HttpMethod.Post, "/base/records/batch")
            {
                Content = JsonContent.Create(batch, HPDBaseJsonSerializerContext.Default.BaseRecordBatchRequest),
            };
            message.Headers.Add(BaseHttpHeaders.IdempotencyKey, "request-1");
            return await client.SendAsync(message);
        }

        HttpResponseMessage first = await SendAsync();
        HttpResponseMessage second = await SendAsync();
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        first.Headers.GetValues(BaseHttpHeaders.RequestDisposition).Should().Equal("committed");
        second.Headers.GetValues(BaseHttpHeaders.RequestDisposition).Should().Equal("duplicate");
        (await ReadJson<BaseRecordBatchResult>(app, second.Content))!.RequestDisposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
    }

    [Fact]
    public async Task AtomicBatchRejectsUnknownJsonMembersBeforeIdentityConstruction()
    {
        await using var app = await TestBaseApp.CreateAsync();
        HttpClient client = app.GetTestClient();
        using var message = new HttpRequestMessage(HttpMethod.Post, "/base/records/batch")
        {
            Content = new StringContent(
                """{"mode":"atomic","operations":[],"unknownBehavior":"must-not-hash"}""",
                System.Text.Encoding.UTF8,
                "application/json"),
        };
        message.Headers.Add(BaseHttpHeaders.IdempotencyKey, "request-with-unknown-member");

        HttpResponseMessage response = await client.SendAsync(message);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task SubjectEpochRotationUsesClosedControlPlaneDtoAndCanonicalInt64Strings()
    {
        var administration = new SubjectAdministrationStub();
        WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthorizationBuilder().AddPolicy("control", policy => policy.RequireAssertion(_ => true));
        builder.Services.AddSingleton<IHPDBaseAdministration>(administration);
        builder.Services.AddSingleton<IBaseHttpPrincipalMapper, TestPrincipalMapper>();
        builder.Services.AddSingleton<IPolicyEvaluator, AllowPolicyEvaluator>();
        builder.Services.AddHPDBase(hpd => hpd
            .ConfigureTokenProtection(options => options.ActiveKey = new BaseOpaqueTokenKey
            {
                Id = 1, Key = System.Security.Cryptography.SHA256.HashData("hpd-base-http-subject-token-key"u8),
                IssueNotBefore = DateTimeOffset.UnixEpoch,
            })
            .AddCollection(HttpPrivateSubjectRecord.Collection)
            .AddCollection(HttpSubjectConsumerRecord.Collection)
            .AddExportedSubject(HttpExportedSubject.HPDBaseSubjectRegistration));
        builder.Services.AddHPDBaseAspNetCore(options =>
            options.Administration.StagingRoot = Path.Combine(Path.GetTempPath(), "hpd-base-l45-http-staging"));
        await using WebApplication app = builder.Build();
        app.MapHPDBasePublicApi();
        RouteGroupBuilder control = app.MapGroup("/base").RequireAuthorization("control");
        control.MapHPDBaseControlPlaneEndpoints(
            app,
            new HPDBaseControlPlaneEndpointSelection
            {
                MapRecords = false,
                MapRegisteredReads = false,
                MapAdministration = true,
                MapArtifactAdministration = true,
                MapPolicyExplain = false,
            },
            (endpoint, _) => endpoint.RequireAuthorization("control"));
        await app.StartAsync();
        HttpClient client = app.GetTestClient();

        HttpResponseMessage response = await client.PostAsync(
            "/base/administration/subjects:rotate-epoch",
            new StringContent(
                """{"storeId":"primary","contractId":"example.user","contractVersion":1,"expectedStateGeneration":"1","destructiveIntent":"rotate-subject-authority-epoch"}""",
                System.Text.Encoding.UTF8,
                "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        administration.Request.Should().NotBeNull();
        administration.Request!.ExpectedStateGeneration.Should().Be(1);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsByteArrayAsync());
        body.RootElement.GetProperty("previousStateGeneration").GetString().Should().Be("1");
        body.RootElement.GetProperty("publishedStateGeneration").GetString().Should().Be("2");
        body.RootElement.GetProperty("publicationPosition").GetString().Should().Be("9");

        HttpResponseMessage noncanonical = await client.PostAsync(
            "/base/administration/subjects:rotate-epoch",
            new StringContent(
                """{"storeId":"primary","contractId":"example.user","contractVersion":1,"expectedStateGeneration":"01","destructiveIntent":"rotate-subject-authority-epoch"}""",
                System.Text.Encoding.UTF8,
                "application/json"));
        HttpResponseMessage extra = await client.PostAsync(
            "/base/administration/subjects:rotate-epoch",
            new StringContent(
                """{"storeId":"primary","contractId":"example.user","contractVersion":1,"expectedStateGeneration":"1","destructiveIntent":"rotate-subject-authority-epoch","unknown":true}""",
                System.Text.Encoding.UTF8,
                "application/json"));

        noncanonical.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        extra.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        string retryJson = """
            {"storeId":"primary","definitionId":"graph.execute","definitionVersion":1,"activationId":"activation-1","expectedGeneration":7,"identity":{"scope":"activation-test","operation":"operator-retry","idempotencyKey":"retry-1","fingerprint":[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0]}}
            """;
        HttpResponseMessage retry = await client.PostAsync(
            "/base/control/activations/retry",
            new StringContent(retryJson, System.Text.Encoding.UTF8, "application/json"));
        retry.StatusCode.Should().Be(HttpStatusCode.OK);
        administration.ActivationRetry.Should().NotBeNull();
        administration.ActivationRetry!.ExpectedGeneration.Should().Be(7);

        string cancelJson = """
            {"storeId":"primary","definitionId":"graph.execute","definitionVersion":1,"activationId":"activation-1","expectedGeneration":8,"propagation":"none","identity":{"scope":"activation-test","operation":"cancel","idempotencyKey":"cancel-1","fingerprint":[0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0]}}
            """;
        HttpResponseMessage cancel = await client.PostAsync(
            "/base/control/activations/cancel",
            new StringContent(cancelJson, System.Text.Encoding.UTF8, "application/json"));
        cancel.StatusCode.Should().Be(HttpStatusCode.OK);
        administration.ActivationCancel.Should().NotBeNull();
        administration.ActivationCancel!.ExpectedGeneration.Should().Be(8);

        string queryJson = """
            {"storeId":"primary","scopeKind":"global","scopeValue":null,"definitionId":"graph.execute","definitionVersion":1,"states":"terminal","after":null,"take":8}
            """;
        HttpResponseMessage query = await client.PostAsync(
            "/base/control/activations/query",
            new StringContent(queryJson, System.Text.Encoding.UTF8, "application/json"));
        query.StatusCode.Should().Be(HttpStatusCode.OK);
        administration.ActivationRead.Should().NotBeNull();
        administration.ActivationRead!.Take.Should().Be(8);

        using var invalidSchedule = new HttpRequestMessage(HttpMethod.Post, "/base/control/schedules/mutate")
        {
            Content = new StringContent(
                """{"scheduleId":"daily","scheduleVersion":1,"kind":"create","expectedGeneration":null,"unknown":true}""",
                System.Text.Encoding.UTF8,
                "application/json"),
        };
        invalidSchedule.Headers.Add(BaseHttpHeaders.IdempotencyKey, "schedule-create-1");
        (await client.SendAsync(invalidSchedule)).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static async Task<T?> ReadJson<T>(WebApplication app, HttpContent content)
    {
        var json = await content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(json, app.Services.GetRequiredService<IHPDBaseRuntime>().Json.Options);
    }

    private sealed class SubjectAdministrationStub : IHPDBaseAdministration
    {
        public BaseSubjectEpochRotationRequest? Request { get; private set; }
        public BaseActivationAdministrationRetryRequest? ActivationRetry { get; private set; }
        public BaseActivationAdministrationCancelRequest? ActivationCancel { get; private set; }
        public BaseActivationAdministrationReadRequest? ActivationRead { get; private set; }
        public BaseAdministrationCapability Capability { get; } = new()
        {
            Backup = false,
            Validate = false,
            Restore = false,
            AdministrativePurge = false,
            VectorRebuild = false,
            OnlineBackup = false,
            WritersBlockedDuringBackup = false,
            ReadersBlockedDuringBackup = false,
            RestoreRequiresExclusiveMaintenance = false,
            Durable = false,
            MaxArtifactBytes = 0,
        };

        public ValueTask<BaseResult<BaseSubjectEpochRotationResult>> RotateSubjectEpochAsync(
            string storeId,
            PrincipalContext principal,
            BaseSubjectEpochRotationRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            storeId.Should().Be("primary");
            principal.Should().NotBeNull();
            Request = request;
            return ValueTask.FromResult<BaseResult<BaseSubjectEpochRotationResult>>(
                new BaseSuccess<BaseSubjectEpochRotationResult>(new BaseSubjectEpochRotationResult
                {
                    ContractId = request.ContractId,
                    ContractVersion = request.ContractVersion,
                    PreviousStateGeneration = 1,
                    PublishedStateGeneration = 2,
                    PublicationPosition = new BaseMutationJournalPosition(9),
                    ExaminedRecords = 4,
                    RewrittenReferences = 3,
                }, OperationStatus.Ok, null, null, null, null));
        }

        public ValueTask<BaseResult<BaseBackupManifest>> CreateBackupAsync(Stream destination, BaseBackupRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<BaseResult<BaseBackupManifest>> ValidateBackupAsync(Stream source, BaseBackupValidationRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<BaseResult<BaseRestoreResult>> RestoreAsync(Stream source, BaseRestoreRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<BaseResult<BasePurgeResult>> PurgeAsync(BasePurgeRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<BaseResult<BaseVectorRebuildResult>> RebuildVectorIndexAsync(BaseVectorRebuildRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<BaseResult<BaseSubjectLifecycleMaintenanceResult>> ExecuteSubjectAuthorityMaintenanceAsync(string storeId, PrincipalContext principal, BaseSubjectAuthorityMaintenanceExecutionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<BaseResult<BaseSubjectLifecycleInspectionResult>> InspectSubjectLifecycleAsync(string storeId, PrincipalContext principal, BaseSubjectLifecycleInspectionRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<BaseResult<BaseActivationTransitionResult>> CancelActivationAsync(BaseActivationAdministrationCancelRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ActivationCancel = request;
            return ValueTask.FromResult<BaseResult<BaseActivationTransitionResult>>(
                new BaseSuccess<BaseActivationTransitionResult>(new BaseActivationTransitionResult
                {
                    State = BaseActivationState.Cancelled,
                    Generation = checked(request.ExpectedGeneration + 1),
                    ControlChecksum = System.Collections.Immutable.ImmutableArray.CreateRange(new byte[32]),
                    Accounting = new BaseActivationAccounting
                    {
                        Candidates = 0, Comparisons = 1, IndexOperations = 1,
                        EvidenceBytes = 32, TransientBytes = 32, ReadIntervals = 0,
                    },
                    Disposition = BaseMutationRequestDisposition.Committed,
                }, OperationStatus.Ok, null, null, null, null));
        }
        public ValueTask<BaseResult<BaseActivationTransitionResult>> RetryActivationAsync(BaseActivationAdministrationRetryRequest request, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ActivationRetry = request;
            return ValueTask.FromResult<BaseResult<BaseActivationTransitionResult>>(
                new BaseSuccess<BaseActivationTransitionResult>(new BaseActivationTransitionResult
                {
                    State = BaseActivationState.RetryPending,
                    Generation = checked(request.ExpectedGeneration + 1),
                    ControlChecksum = System.Collections.Immutable.ImmutableArray.CreateRange(new byte[32]),
                    Accounting = new BaseActivationAccounting
                    {
                        Candidates = 0, Comparisons = 1, IndexOperations = 1,
                        EvidenceBytes = 32, TransientBytes = 32, ReadIntervals = 0,
                    },
                    Disposition = BaseMutationRequestDisposition.Committed,
                }, OperationStatus.Ok, null, null, null, null));
        }
        public ValueTask<BaseResult<BaseActivationTransitionResult>> ReconcileActivationAsync(BaseActivationAdministrationReconcileRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<BaseResult<BaseActivationTransitionResult>> DisposeActivationAsync(BaseActivationAdministrationDisposeRequest request, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public ValueTask<BaseResult<BaseActivationAdministrationPage>> ReadActivationsAsync(BaseActivationAdministrationReadRequest request, CancellationToken cancellationToken = default)
        {
            ActivationRead = request;
            return ValueTask.FromResult<BaseResult<BaseActivationAdministrationPage>>(
                new BaseSuccess<BaseActivationAdministrationPage>(new BaseActivationAdministrationPage
                {
                    Items = [], Next = null, CapturedIndexGeneration = 1, Intervals = [],
                    Accounting = new BaseActivationAccounting
                    {
                        Candidates = 0, Comparisons = 0, IndexOperations = 0,
                        ReadIntervals = 0, EvidenceBytes = 0, TransientBytes = 0,
                    },
                }, OperationStatus.Ok, null, null, null, null));
        }
    }
}

[BaseCollection("http.subject.private", typeof(HttpSubjectJsonContext), SystemOwnerModuleId = "http.subjects")]
internal sealed partial record HttpPrivateSubjectRecord
{
    [BaseField("http.subject.active")]
    public required bool Active { get; init; }
    [BaseField("http.subject.tombstoned")]
    public required bool Tombstoned { get; init; }
}

[BaseExportedSubject(
    "http.exported-subject",
    OwningModuleId = "http.subjects",
    PrivateRecordType = typeof(HttpPrivateSubjectRecord),
    AcquisitionGrantId = "http.subject.acquire",
    ValidationGrantId = "http.subject.validate",
    AdministrationGrantId = "http.subject.rotate",
    ValidationPlanId = "http.subject.validate.v1",
    ActiveFieldId = "http.subject.active",
    TombstoneFieldId = "http.subject.tombstoned")]
internal sealed partial class HttpExportedSubject;

[BaseCollection("http.subject.consumer", typeof(HttpSubjectJsonContext))]
internal sealed partial record HttpSubjectConsumerRecord
{
    [BaseField("http.subject.reference")]
    [BaseSubjectReference(typeof(HttpExportedSubject), Requirement = BaseSubjectReferenceRequirement.Active)]
    public required BaseSubjectReference<HttpExportedSubject> Subject { get; init; }
}

[JsonSerializable(typeof(HttpPrivateSubjectRecord))]
[JsonSerializable(typeof(HttpSubjectConsumerRecord))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
internal sealed partial class HttpSubjectJsonContext : JsonSerializerContext;
