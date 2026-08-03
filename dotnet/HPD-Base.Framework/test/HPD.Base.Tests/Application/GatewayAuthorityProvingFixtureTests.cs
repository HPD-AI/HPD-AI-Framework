using System.Security.Cryptography;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Tests.Application;

public sealed class GatewayAuthorityProvingFixtureTests
{
    [Fact]
    public async Task ManagedAuthorityGraphCommitsOnceAndCasFailureLeavesNoPartialHistory()
    {
        string database = Path.Combine(
            Path.GetTempPath(),
            "hpd-base-gateway-proof-" + Guid.NewGuid().ToString("N") + ".db");
        try
        {
            BaseCollection<JsonElement>[] collections =
            [
                Collection("gateway.revisions", BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge),
                Collection("gateway.validations", BaseCollectionMutationMode.AppendOnly),
                Collection("gateway.audit", BaseCollectionMutationMode.AppendOnlyWithAdministrativePurge),
                Collection("gateway.intents", BaseCollectionMutationMode.AppendOnly),
                Collection("gateway.desired", BaseCollectionMutationMode.Mutable),
                Collection("gateway.outbox", BaseCollectionMutationMode.AppendOnly),
            ];
            var services = new ServiceCollection().AddLogging();
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
            await using ServiceProvider provider = services.BuildServiceProvider();
            IBaseSchemaManager schemas = provider.GetRequiredService<IBaseSchemaManager>();
            BaseSchemaPlan plan = (await schemas.PlanAsync(new BaseSchemaPlanRequest { StoreId = "gateway-proof" })).Value!;
            (await schemas.ApplyAsync(new BaseSchemaApplyRequest { ProtectedArtifact = plan.ProtectedArtifact }))
                .IsSuccess().Should().BeTrue();
            (await provider.GetRequiredService<IHPDBaseApplication>().InitializeAsync()).IsSuccess().Should().BeTrue();

            IBaseRecordRuntime runtime = provider.GetRequiredService<IBaseRecordRuntime>();
            PrincipalContext principal = new()
            {
                AuthenticationState = PrincipalAuthenticationState.Authenticated,
                SubjectId = "gateway-authority",
            };
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
            (await runtime.GetAsync(
                "gateway.outbox", new RecordId("outbox-1"), principal,
                Operation(BaseOperationKind.Get, "gateway.outbox", "outbox-1"))).IsSuccess().Should().BeTrue();

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

    private static BaseRecordBatchRequest AuthorityBatch(
        string requestKey,
        string revision,
        string validation,
        string audit,
        string intent,
        string outbox,
        RevisionToken desiredRevision) => new()
        {
            Mode = BaseRecordBatchExecutionMode.Atomic,
            RequestIdentity = BaseMutationRequestIdentity.Create(
                "gateway.namespace",
                "gateway.submit-and-activate",
                requestKey,
                BaseMutationRequestFingerprint.Create(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(requestKey)))),
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
}
