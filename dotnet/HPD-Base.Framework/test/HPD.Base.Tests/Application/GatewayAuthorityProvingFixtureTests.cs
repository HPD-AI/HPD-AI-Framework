using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Tests.Application;

public sealed class GatewayAuthorityProvingFixtureTests
{
    [Fact]
    public async Task ManagedAuthorityStateAndReceiptReconstructAcrossProcessRestart()
    {
        string database = Path.Combine(
            Path.GetTempPath(),
            "hpd-base-gateway-restart-proof-" + Guid.NewGuid().ToString("N") + ".db");
        BaseCollection<JsonElement>[] collections = AuthorityCollections();
        var observer = new ProvingMutationObserver();
        RevisionToken expectedRevision;
        try
        {
            await using (ServiceProvider first = Services(database, collections, observer).BuildServiceProvider())
            {
                await ApplyAndInitializeAsync(first);
                IBaseRecordRuntime runtime = first.GetRequiredService<IBaseRecordRuntime>();
                PrincipalContext principal = Principal();
                RecordEnvelope desired = (await runtime.CreateAsync(
                    "gateway.desired",
                    new RecordCreateRequest { RequestedId = new RecordId("desired"), Payload = Payload("generation", 1) },
                    principal,
                    Operation(BaseOperationKind.Create, "gateway.desired", "desired"))).Value!;
                expectedRevision = desired.Metadata.Revision!.Value;
                OperationResult<BaseRecordBatchResult> committed = await runtime.BatchAsync(
                    AuthorityBatch("restart-request", "revision", "validation", "audit", "intent", "outbox", expectedRevision),
                    principal,
                    Operation(BaseOperationKind.Batch, "gateway.revisions"));
                committed.Value!.RequestDisposition.Should().Be(BaseMutationRequestDisposition.Committed);
            }

            await using ServiceProvider restarted = Services(database, collections, observer).BuildServiceProvider();
            (await restarted.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
            IBaseRecordRuntime restartedRuntime = restarted.GetRequiredService<IBaseRecordRuntime>();
            PrincipalContext restartedPrincipal = Principal();

            foreach ((string Collection, string Id) fact in new[]
            {
                ("gateway.revisions", "revision"),
                ("gateway.validations", "validation"),
                ("gateway.audit", "audit"),
                ("gateway.intents", "intent"),
                ("gateway.desired", "desired"),
                ("gateway.outbox", "outbox"),
            })
            {
                (await restartedRuntime.GetAsync(
                    fact.Collection,
                    new RecordId(fact.Id),
                    restartedPrincipal,
                    Operation(BaseOperationKind.Get, fact.Collection, fact.Id)))
                    .IsSuccess().Should().BeTrue($"{fact.Collection} must reconstruct after restart");
            }

            OperationResult<BaseRecordBatchResult> duplicate = await restartedRuntime.BatchAsync(
                AuthorityBatch("restart-request", "revision", "validation", "audit", "intent", "outbox", expectedRevision),
                restartedPrincipal,
                Operation(BaseOperationKind.Batch, "gateway.revisions"));
            duplicate.Value!.RequestDisposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
            observer.Count.Should().Be(7, "restart and duplicate replay must not synthesize committed mutations");
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            string directory = Path.GetDirectoryName(database)!;
            string name = Path.GetFileName(database);
            foreach (string file in Directory.GetFiles(directory).Where(file => Path.GetFileName(file).StartsWith(name, StringComparison.Ordinal)))
                File.Delete(file);
        }
    }

    [Fact]
    public async Task ManagedAuthorityGraphCommitsOnceAndCasFailureLeavesNoPartialHistory()
    {
        string database = Path.Combine(
            Path.GetTempPath(),
            "hpd-base-gateway-proof-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            BaseCollection<JsonElement>[] collections = AuthorityCollections();
            var observer = new ProvingMutationObserver();
            await using ServiceProvider provider = Services(database, collections, observer).BuildServiceProvider();
            await ApplyAndInitializeAsync(provider);

            IBaseRecordRuntime runtime = provider.GetRequiredService<IBaseRecordRuntime>();
            PrincipalContext principal = Principal();
            OperationResult<RecordEnvelope> initialDesired = await runtime.CreateAsync(
                "gateway.desired",
                new RecordCreateRequest { RequestedId = new RecordId("desired"), Payload = Payload("generation", 1) },
                principal,
                Operation(BaseOperationKind.Create, "gateway.desired", "desired"));
            initialDesired.IsSuccess().Should().BeTrue(initialDesired.Error?.Code);

            BaseRecordBatchRequest accepted = AuthorityBatch(
                "request-1",
                "revision-1",
                "validation-1",
                "audit-1",
                "intent-1",
                "outbox-1",
                initialDesired.Value!.Metadata.Revision!.Value);

            OperationResult<BaseRecordBatchResult> committed = await runtime.BatchAsync(
                accepted,
                principal,
                Operation(BaseOperationKind.Batch, "gateway.revisions"));
            OperationResult<BaseRecordBatchResult> duplicate = await runtime.BatchAsync(
                accepted,
                principal,
                Operation(BaseOperationKind.Batch, "gateway.revisions"));

            committed.Value!.Outcome.Should().Be(BaseRecordBatchOutcome.Committed);
            committed.Value.RequestDisposition.Should().Be(BaseMutationRequestDisposition.Committed);
            duplicate.Value!.RequestDisposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
            observer.Count.Should().Be(7, "the duplicate must not redeliver six committed authority mutations");
            (await runtime.GetAsync(
                "gateway.outbox", new RecordId("outbox-1"), principal,
                Operation(BaseOperationKind.Get, "gateway.outbox", "outbox-1"))).IsSuccess().Should().BeTrue();

            BaseRecordBatchRequest changedBoundSemantics = AuthorityBatch(
                "request-1",
                "revision-1",
                "validation-1",
                "audit-1",
                "intent-1",
                "outbox-1",
                initialDesired.Value.Metadata.Revision!.Value,
                fingerprintSeed: "changed-actor-content-token-target");
            OperationResult<BaseRecordBatchResult> fingerprintConflict = await runtime.BatchAsync(
                changedBoundSemantics,
                principal,
                Operation(BaseOperationKind.Batch, "gateway.revisions"));
            fingerprintConflict.Status.Should().Be(OperationStatus.Conflict);
            fingerprintConflict.Error!.Code.Should().Be(BaseMutationRequestErrorCodes.FingerprintConflict);

            BaseRecordBatchRequest rejectedCas = AuthorityBatch(
                "request-2",
                "revision-2",
                "validation-2",
                "audit-2",
                "intent-2",
                "outbox-2",
                initialDesired.Value.Metadata.Revision!.Value);
            OperationResult<BaseRecordBatchResult> rejected = await runtime.BatchAsync(
                rejectedCas,
                principal,
                Operation(BaseOperationKind.Batch, "gateway.revisions"));

            rejected.Value!.Outcome.Should().Be(BaseRecordBatchOutcome.RolledBack);
            (await runtime.GetAsync(
                "gateway.revisions", new RecordId("revision-2"), principal,
                Operation(BaseOperationKind.Get, "gateway.revisions", "revision-2")))
                .Status.Should().Be(OperationStatus.NotFound);
            (await runtime.GetAsync(
                "gateway.outbox", new RecordId("outbox-2"), principal,
                Operation(BaseOperationKind.Get, "gateway.outbox", "outbox-2")))
                .Status.Should().Be(OperationStatus.NotFound);
            observer.Count.Should().Be(7);

            RecordEnvelope currentDesired = (await runtime.GetAsync(
                "gateway.desired", new RecordId("desired"), principal,
                Operation(BaseOperationKind.Get, "gateway.desired", "desired"))).Value!;
            BaseRecordBatchRequest correctedRetry = AuthorityBatch(
                "request-2",
                "revision-2",
                "validation-2",
                "audit-2",
                "intent-2",
                "outbox-2",
                currentDesired.Metadata.Revision!.Value);
            OperationResult<BaseRecordBatchResult> retried = await runtime.BatchAsync(
                correctedRetry,
                principal,
                Operation(BaseOperationKind.Batch, "gateway.revisions"));
            retried.Value!.RequestDisposition.Should().Be(BaseMutationRequestDisposition.Committed,
                "the failed CAS must not leave an accepted request receipt");
            observer.Count.Should().Be(13);
        }
        finally
        {
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            string directory = Path.GetDirectoryName(database)!;
            string name = Path.GetFileName(database);
            foreach (string file in Directory.GetFiles(directory).Where(file => Path.GetFileName(file).StartsWith(name, StringComparison.Ordinal)))
                File.Delete(file);
        }
    }

    private static BaseCollection<JsonElement>[] AuthorityCollections() =>
    [
        Collection("gateway.revisions", BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge),
        Collection("gateway.validations", BaseCollectionMutationMode.AppendOnly),
        Collection("gateway.audit", BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge),
        Collection("gateway.intents", BaseCollectionMutationMode.AppendOnly),
        Collection("gateway.desired", BaseCollectionMutationMode.Mutable),
        Collection("gateway.outbox", BaseCollectionMutationMode.AppendOnly),
    ];

    private static ServiceCollection Services(
        string database,
        BaseCollection<JsonElement>[] collections,
        ProvingMutationObserver observer)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IBaseCommittedMutationObserver>(observer);
        services.AddSingleton<IPolicyEvaluator, ProvingPolicyEvaluator>();
        services.AddHPDBase(builder =>
        {
            builder.ConfigureSchema(options =>
            {
                options.ApplicationId = "gateway-authority-proof";
                options.PlanProtectionKey = Enumerable.Repeat((byte)0x37, 32).ToArray();
            });
            foreach (BaseCollection<JsonElement> collection in collections)
                builder.AddCollection(collection);
            builder.UseSqlite(options =>
            {
                options.StoreId = "gateway-proof";
                options.DataSource = database;
            });
        });
        return services;
    }

    private static async Task ApplyAndInitializeAsync(ServiceProvider provider)
    {
        IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>();
        BaseSchemaPlan plan = (await schemas.PlanAsync(new BaseSchemaPlanRequest { StoreId = "gateway-proof" })).Value!;
        (await schemas.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = plan.ProtectedArtifact }))
            .IsSuccess().Should().BeTrue();
        (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();
    }

    private static PrincipalContext Principal() => new()
    {
        AuthenticationState = PrincipalAuthenticationState.Authenticated,
        SubjectId = "gateway-authority",
    };

    private static BaseRecordBatchRequest AuthorityBatch(
        string requestKey,
        string revision,
        string validation,
        string audit,
        string intent,
        string outbox,
        RevisionToken desiredRevision,
        string? fingerprintSeed = null) => new()
        {
            Mode = BaseRecordBatchExecutionMode.Atomic,
            RequestIdentity = BaseMutationRequestIdentity.Create(
                "gateway.namespace",
                "gateway.submit-and-activate",
                requestKey,
                BaseMutationRequestFingerprint.Create(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(fingerprintSeed ?? requestKey)))),
            Operations =
            [
                Create("revision", "gateway.revisions", revision, Payload("kind", "accepted-revision")),
                Create("validation", "gateway.validations", validation, Payload("valid", true)),
                Create("audit", "gateway.audit", audit, Payload("action", "accepted")),
                Create("intent", "gateway.intents", intent, Payload("state", "pending")),
                new BaseRecordBatchItem
                {
                    ItemId = "desired",
                    CollectionId = "gateway.desired",
                    Kind = BaseRecordMutationKind.Replace,
                    RecordId = new RecordId("desired"),
                    Replace = new RecordReplaceRequest
                    {
                        ExpectedRevision = desiredRevision,
                        Payload = Payload("generation", 2),
                    },
                },
                Create("outbox", "gateway.outbox", outbox, Payload("delivery", "pending")),
            ],
        };

    private static BaseRecordBatchItem Create(
        string itemId,
        string collection,
        string id,
        RecordPayload payload) => new()
        {
            ItemId = itemId,
            CollectionId = collection,
            Kind = BaseRecordMutationKind.Create,
            Create = new RecordCreateRequest { RequestedId = new RecordId(id), Payload = payload },
        };

    private static BaseCollection<JsonElement> Collection(
        string id,
        BaseCollectionMutationMode mode) => BaseCollection<JsonElement>.Create(
            new CollectionDefinition
            {
                Id = id,
                Name = id,
                Kind = BaseCollectionKinds.Document,
                SchemaMode = SchemaMode.Loose,
                UnknownFields = UnknownFieldPolicy.Preserve,
                MutationMode = mode,
            },
            HPDBaseJsonSerializerContext.Default.JsonElement,
            static _ => { });

    private static RecordPayload Payload<T>(string name, T value)
    {
        JsonElement element = JsonSerializer.SerializeToElement(value);
        byte[] json = JsonSerializer.SerializeToUtf8Bytes(
            new Dictionary<string, JsonElement> { [name] = element },
            HPDBaseJsonSerializerContext.Default.DictionaryStringJsonElement);
        using JsonDocument document = JsonDocument.Parse(json);
        return new RecordPayload { Kind = RecordPayloadKind.Json, Json = document.RootElement.Clone() };
    }

    private static OperationContext Operation(BaseOperationKind kind, string collection, string? record = null) => new()
    {
        Operation = kind,
        CollectionId = collection,
        RecordId = record,
        Now = DateTimeOffset.UtcNow,
    };

    private sealed class ProvingPolicyEvaluator : IPolicyEvaluator
    {
        public ValueTask<PolicyDecision> EvaluateAsync(
            PolicyEvaluationRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = request;
            return ValueTask.FromResult(new PolicyDecision
            {
                Effect = PolicyEffect.Allow,
                Outcome = PolicyOutcome.Allowed,
            });
        }
    }

    private sealed class ProvingMutationObserver : IBaseCommittedMutationObserver
    {
        private int _count;
        public int Count => Volatile.Read(ref _count);
        public ValueTask ObserveAsync(BaseRecordMutationEvent mutation, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _ = mutation;
            Interlocked.Increment(ref _count);
            return ValueTask.CompletedTask;
        }
    }
}
