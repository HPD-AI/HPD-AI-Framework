using System.Text.Json;
using HPD.Base;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.Tests.Operations;

public sealed class PatchReplaceDeleteOperationPipelineTests
{
    [Fact]
    public async Task EmptyPatchFailsValidationBeforeStoreCall()
    {
        var store = new FakeRecordStore("primary");
        using var provider = OperationTestServices.Build(store);

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().PatchAsync(
            "items",
            RecordId.Create("rec_1"),
            new RecordPatchRequest
            {
                Patch = new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = [] }
            },
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Patch),
            CancellationToken.None);

        Assert.Equal(OperationStatus.ValidationFailed, result.Status);
        Assert.Equal(0, store.PatchCalls);
    }

    [Fact]
    public async Task ExpectedRevisionPatchRequiresRevisionedStore()
    {
        var store = new FakeRecordStore("primary");
        using var provider = OperationTestServices.Build(store);

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().PatchAsync(
            "items",
            RecordId.Create("rec_1"),
            new RecordPatchRequest
            {
                Patch = FieldMapPayload("title", "updated"),
                ExpectedRevision = new RevisionToken("rev_1")
            },
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Patch),
            CancellationToken.None);

        Assert.Equal(OperationStatus.Unsupported, result.Status);
        Assert.Equal(0, store.PatchCalls);
    }

    [Fact]
    public async Task ExpectedRevisionPatchUsesRevisionedStoreMethod()
    {
        var store = new FakeRevisionedRecordStore("primary");
        store.AddRecord(ExistingRecord());
        using var provider = OperationTestServices.Build(store);

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().PatchAsync(
            "items",
            RecordId.Create("rec_1"),
            new RecordPatchRequest
            {
                Patch = FieldMapPayload("title", "updated"),
                ExpectedRevision = new RevisionToken("rev_1")
            },
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Patch),
            CancellationToken.None);

        Assert.Equal(OperationStatus.Updated, result.Status);
        Assert.Equal(1, store.PatchCalls);
        Assert.Equal("rev_1", store.LastPatchRequest!.ExpectedRevision!.Value.Value);
        Assert.Single(result.Events!);
    }

    [Fact]
    public async Task ExpectedRevisionReplaceUsesRevisionedStoreMethod()
    {
        var store = new FakeRevisionedRecordStore("primary");
        store.AddRecord(ExistingRecord());
        using var provider = OperationTestServices.Build(store);

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().ReplaceAsync(
            "items",
            RecordId.Create("rec_1"),
            new RecordReplaceRequest
            {
                Payload = JsonPayload("title", "replacement"),
                ExpectedRevision = new RevisionToken("rev_1")
            },
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Replace),
            CancellationToken.None);

        Assert.Equal(OperationStatus.Updated, result.Status);
        Assert.Equal(1, store.ReplaceCalls);
        Assert.Equal("rev_1", store.LastReplaceRequest!.ExpectedRevision!.Value.Value);
        Assert.Single(result.Events!);
    }

    [Fact]
    public async Task PatchPassesSchemaValidatedPayloadToStore()
    {
        var store = new FakeRecordStore("primary");
        store.AddRecord(ExistingRecord());
        using var provider = OperationTestServices.Build(
            store,
            configureServices: services => services.AddSingleton<IBaseSchemaValidator>(new NormalizingSchemaValidator()));

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().PatchAsync(
            "items",
            RecordId.Create("rec_1"),
            new RecordPatchRequest { Patch = FieldMapPayload("title", "original") },
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Patch),
            CancellationToken.None);

        Assert.Equal(OperationStatus.Updated, result.Status);
        Assert.Equal("patch-normalized", store.LastPatchRequest!.Patch.Fields!["normalized"].GetString());
    }

    [Fact]
    public async Task PatchEvaluatesPolicyAgainstMergedCandidateAndDispatchesBeforeSnapshot()
    {
        var store = new FakeRecordStore("primary");
        store.AddRecord(ExistingRecord("rec_1", ("title", "old"), ("status", "active")));
        var policy = new CapturingPolicyEvaluator();
        var publisher = new CapturingEventPublisher();
        using var provider = OperationTestServices.Build(
            store,
            policy,
            configureServices: services => services.AddSingleton<IBaseEventPublisher>(publisher));

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().PatchAsync(
            "items",
            RecordId.Create("rec_1"),
            new RecordPatchRequest { Patch = FieldMapPayload("title", "new") },
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Patch),
            CancellationToken.None);

        Assert.Equal(OperationStatus.Updated, result.Status);
        Assert.NotNull(policy.LastRequest);
        Assert.NotNull(policy.LastRequest!.Resource.ExistingRecord);
        Assert.Equal("new", policy.LastRequest.Resource.ProposedPayload!.Fields!["title"].GetString());
        Assert.Equal("active", policy.LastRequest.Resource.ProposedPayload.Fields["status"].GetString());
        var proposedRecord = policy.LastRequest.Resource.ProposedRecord;
        Assert.NotNull(proposedRecord);
        var proposedFields = proposedRecord!.Payload.Fields;
        Assert.NotNull(proposedFields);
        Assert.Equal("rec_1", proposedRecord.Id.Value);
        Assert.Equal("new", proposedFields!["title"].GetString());
        Assert.Equal("active", proposedFields["status"].GetString());
        Assert.Equal(["title"], store.LastPatchRequest!.Patch.Fields!.Keys.ToArray());
        Assert.NotNull(publisher.LastEvent);
        var before = publisher.LastEvent!.Before;
        Assert.NotNull(before);
        var beforeRecord = before!;
        Assert.NotNull(beforeRecord.Payload);
        var beforeFields = beforeRecord.Payload!.Fields;
        Assert.NotNull(beforeFields);
        Assert.Equal("old", beforeFields!["title"].GetString());
    }

    [Fact]
    public async Task WriteCheckEvaluatesPatchAgainstMergedPayloadBeforeStoreCall()
    {
        var store = new FakeRecordStore("primary");
        store.AddRecord(ExistingRecord("rec_1", ("title", "old"), ("ownerId", "user-1")));
        using var provider = OperationTestServices.Build(store, new ConstrainedPolicyEvaluator(writeCheck: OwnerWriteCheck("user-1")));

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().PatchAsync(
            "items",
            RecordId.Create("rec_1"),
            new RecordPatchRequest { Patch = FieldMapPayload("title", "new") },
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Patch),
            CancellationToken.None);

        Assert.Equal(OperationStatus.Updated, result.Status);
        Assert.Equal(1, store.PatchCalls);
    }

    [Fact]
    public async Task WriteCheckDeniesPatchAgainstMergedPayloadBeforeStoreCall()
    {
        var store = new FakeRecordStore("primary");
        store.AddRecord(ExistingRecord("rec_1", ("title", "old"), ("ownerId", "user-2")));
        using var provider = OperationTestServices.Build(store, new ConstrainedPolicyEvaluator(writeCheck: OwnerWriteCheck("user-1")));

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().PatchAsync(
            "items",
            RecordId.Create("rec_1"),
            new RecordPatchRequest { Patch = FieldMapPayload("title", "new") },
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Patch),
            CancellationToken.None);

        Assert.Equal(OperationStatus.PolicyDenied, result.Status);
        Assert.Equal("base.runtime.policy.writeCheck.denied", result.Error!.Code);
        Assert.Equal(0, store.PatchCalls);
    }

    [Fact]
    public async Task ReplacePassesSchemaValidatedPayloadToStore()
    {
        var store = new FakeRecordStore("primary");
        store.AddRecord(ExistingRecord());
        using var provider = OperationTestServices.Build(
            store,
            configureServices: services => services.AddSingleton<IBaseSchemaValidator>(new NormalizingSchemaValidator()));

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().ReplaceAsync(
            "items",
            RecordId.Create("rec_1"),
            new RecordReplaceRequest { Payload = JsonPayload("title", "original") },
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Replace),
            CancellationToken.None);

        Assert.Equal(OperationStatus.Updated, result.Status);
        Assert.Equal("replace-normalized", store.LastReplaceRequest!.Payload.Fields!["normalized"].GetString());
    }

    [Fact]
    public async Task WriteCheckDeniesReplaceAgainstProposedPayloadBeforeStoreCall()
    {
        var store = new FakeRecordStore("primary");
        store.AddRecord(ExistingRecord());
        using var provider = OperationTestServices.Build(store, new ConstrainedPolicyEvaluator(writeCheck: OwnerWriteCheck("user-1")));

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().ReplaceAsync(
            "items",
            RecordId.Create("rec_1"),
            new RecordReplaceRequest { Payload = JsonPayload("ownerId", "user-2") },
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Replace),
            CancellationToken.None);

        Assert.Equal(OperationStatus.PolicyDenied, result.Status);
        Assert.Equal("base.runtime.policy.writeCheck.denied", result.Error!.Code);
        Assert.Equal(0, store.ReplaceCalls);
    }

    [Fact]
    public async Task ExpectedRevisionDeleteFailsClosedWithoutAdvertisedDeleteCapability()
    {
        var store = new FakeRevisionedRecordStore("primary");
        using var provider = OperationTestServices.Build(store);

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().DeleteAsync(
            "items",
            RecordId.Create("rec_1"),
            new RecordDeleteRequest { ExpectedRevision = new RevisionToken("rev_1") },
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Delete),
            CancellationToken.None);

        Assert.Equal(OperationStatus.Unsupported, result.Status);
        Assert.Equal(0, store.DeleteCalls);
    }

    [Fact]
    public async Task ExpectedRevisionDeleteUsesBaseDeleteWhenStoreAdvertisesRevisionDelete()
    {
        var store = new FakeRecordStore(
            "primary",
            revision: new RevisionCapability
            {
                Supported = true,
                Guarantee = RevisionGuarantee.Store,
                Delete = true
            });
        store.AddRecord(ExistingRecord());
        using var provider = OperationTestServices.Build(store);

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().DeleteAsync(
            "items",
            RecordId.Create("rec_1"),
            new RecordDeleteRequest { ExpectedRevision = new RevisionToken("rev_1") },
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Delete),
            CancellationToken.None);

        Assert.Equal(OperationStatus.Deleted, result.Status);
        Assert.Equal(1, store.DeleteCalls);
    }

    [Fact]
    public async Task DeleteDispatchesEventAfterSuccessfulStoreCall()
    {
        var store = new FakeRecordStore("primary");
        store.AddRecord(ExistingRecord());
        using var provider = OperationTestServices.Build(store);

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().DeleteAsync(
            "items",
            RecordId.Create("rec_1"),
            new RecordDeleteRequest(),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Delete),
            CancellationToken.None);

        Assert.Equal(OperationStatus.Deleted, result.Status);
        Assert.Equal(1, store.DeleteCalls);
        Assert.Single(result.Events!);
    }

    [Fact]
    public async Task DeleteEvaluatesPolicyAgainstExistingCandidateBeforeStoreCall()
    {
        var store = new FakeRecordStore("primary");
        store.AddRecord(ExistingRecord());
        using var provider = OperationTestServices.Build(store, new DenyExistingRecordPolicyEvaluator());

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().DeleteAsync(
            "items",
            RecordId.Create("rec_1"),
            new RecordDeleteRequest(),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Delete),
            CancellationToken.None);

        Assert.Equal(OperationStatus.PolicyDenied, result.Status);
        Assert.Equal(1, store.GetCalls);
        Assert.Equal(0, store.DeleteCalls);
    }

    [Fact]
    public async Task WriteCheckDeniesDeleteAgainstExistingPayloadBeforeStoreCall()
    {
        var store = new FakeRecordStore("primary");
        store.AddRecord(ExistingRecord("rec_1", ("title", "old"), ("ownerId", "user-2")));
        using var provider = OperationTestServices.Build(store, new ConstrainedPolicyEvaluator(writeCheck: OwnerWriteCheck("user-1")));

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().DeleteAsync(
            "items",
            RecordId.Create("rec_1"),
            new RecordDeleteRequest(),
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Delete),
            CancellationToken.None);

        Assert.Equal(OperationStatus.PolicyDenied, result.Status);
        Assert.Equal("base.runtime.policy.writeCheck.denied", result.Error!.Code);
        Assert.Equal(1, store.GetCalls);
        Assert.Equal(0, store.DeleteCalls);
    }

    [Fact]
    public async Task DeleteRedactsReturnedPreviousAndEventSnapshot()
    {
        var store = new FakeRecordStore("primary");
        store.AddRecord(new RecordEnvelope
        {
            CollectionId = "items",
            Id = RecordId.Create("rec_1"),
            Payload = new RecordPayload
            {
                Kind = RecordPayloadKind.FieldMap,
                Fields = new Dictionary<string, JsonElement>
                {
                    ["title"] = Json("hello"),
                    ["secret"] = Json("hidden")
                }
            },
            Metadata = new RecordMetadata()
        });
        var publisher = new CapturingEventPublisher();
        using var provider = OperationTestServices.Build(
            store,
            fields:
            [
                new FieldDefinition { Id = "title", ApplicationName = "title", WireName = "title", Type = BaseFieldTypes.String },
                new FieldDefinition { Id = "secret", ApplicationName = "secret", WireName = "secret", Type = BaseFieldTypes.String, Hidden = true }
            ],
            configureServices: services => services.AddSingleton<IBaseEventPublisher>(publisher));

        var result = await provider.GetRequiredService<IBaseRecordRuntime>().DeleteAsync(
            "items",
            RecordId.Create("rec_1"),
            new RecordDeleteRequest { ReturnPrevious = true },
            RuntimeTestData.AnonymousPrincipal,
            RuntimeTestData.Operation(BaseOperationKind.Delete),
            CancellationToken.None);

        Assert.Equal(OperationStatus.Deleted, result.Status);
        Assert.Equal(["title"], result.Value!.Previous!.Payload.Fields!.Keys.ToArray());
        Assert.True(result.Value.Previous.Policy!.Redacted);
        Assert.NotNull(publisher.LastEvent);
        Assert.True(publisher.LastEvent!.Before!.Redacted);
        Assert.Equal(["title"], publisher.LastEvent.Before.Payload!.Fields!.Keys.ToArray());
    }

    private static RecordPayload FieldMapPayload(string name, string value)
    {
        using var document = JsonDocument.Parse($$"""{"{{name}}":"{{value}}"}""");
        return new RecordPayload
        {
            Kind = RecordPayloadKind.FieldMap,
            Fields = new Dictionary<string, JsonElement>
            {
                [name] = document.RootElement.GetProperty(name).Clone()
            }
        };
    }

    private static RecordPayload JsonPayload(string name, string value)
    {
        using var document = JsonDocument.Parse($$"""{"{{name}}":"{{value}}"}""");
        return new RecordPayload
        {
            Kind = RecordPayloadKind.Json,
            Json = document.RootElement.Clone()
        };
    }

    private static JsonElement Json(string value)
    {
        using var document = JsonDocument.Parse($"\"{value}\"");
        return document.RootElement.Clone();
    }

    private static FilterExpression OwnerWriteCheck(string ownerId) => new()
    {
        Kind = FilterNodeKind.Compare,
        Field = "ownerId",
        Operator = FilterOperator.Equal,
        Value = new QueryValue
        {
            Kind = QueryValueKind.String,
            String = ownerId
        }
    };

    private static RecordEnvelope ExistingRecord(string id = "rec_1", params (string Name, string Value)[] fields) => new()
    {
        CollectionId = "items",
        Id = RecordId.Create(id),
        Payload = new RecordPayload
        {
            Kind = RecordPayloadKind.FieldMap,
            Fields = fields.Length == 0
                ? new Dictionary<string, JsonElement> { ["title"] = Json("existing") }
                : fields.ToDictionary(field => field.Name, field => Json(field.Value), StringComparer.Ordinal)
        },
        Metadata = new RecordMetadata { Revision = new RevisionToken("rev_1") }
    };

    private sealed class CapturingPolicyEvaluator : IPolicyEvaluator
    {
        public PolicyEvaluationRequest? LastRequest { get; private set; }

        public ValueTask<PolicyDecision> EvaluateAsync(
            PolicyEvaluationRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            LastRequest = request;
            return ValueTask.FromResult(new PolicyDecision
            {
                Effect = PolicyEffect.Allow,
                Outcome = PolicyOutcome.Allowed
            });
        }
    }
}
