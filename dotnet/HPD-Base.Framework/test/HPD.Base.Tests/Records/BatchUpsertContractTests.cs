using System.Reflection;
using System.Text.Json;
using HPD.Base;

namespace HPD.Base.Tests.Abstractions.Records;

public sealed class BatchUpsertContractTests
{
    [Fact]
    public void BatchUnionIsClosedTypedAndHasNoObjectBody()
    {
        var properties = typeof(BaseRecordBatchItem).GetProperties(BindingFlags.Instance | BindingFlags.Public);

        Assert.DoesNotContain(properties, property => property.PropertyType == typeof(object));
        Assert.Equal(
            [typeof(RecordCreateRequest), typeof(RecordPatchRequest), typeof(RecordReplaceRequest), typeof(RecordDeleteRequest), typeof(RecordUpsertRequest)],
            properties
                .Where(property => property.Name is nameof(BaseRecordBatchItem.Create)
                    or nameof(BaseRecordBatchItem.Patch)
                    or nameof(BaseRecordBatchItem.Replace)
                    or nameof(BaseRecordBatchItem.Delete)
                    or nameof(BaseRecordBatchItem.Upsert))
                .Select(property => Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType)
                .ToArray());
    }

    [Fact]
    public void UpsertCarriesSeparateCreateAndUpdateBranches()
    {
        var request = new RecordUpsertRequest
        {
            Id = new RecordId("rec_1"),
            CreatePayload = Payload("created"),
            UpdatePayload = Payload("updated"),
            UpdateMode = RecordUpsertUpdateMode.Patch,
            Condition = RecordUpsertExistenceCondition.Any,
            ExpectedRevision = new RevisionToken("rev_1")
        };

        Assert.NotSame(request.CreatePayload, request.UpdatePayload);
        Assert.Equal("rec_1", request.Id.Value);
        Assert.Equal(RecordUpsertUpdateMode.Patch, request.UpdateMode);
        Assert.Equal("rev_1", request.ExpectedRevision?.Value);
    }

    [Fact]
    public void BatchAndUpsertRoundTripThroughGeneratedMetadata()
    {
        var request = new BaseRecordBatchRequest
        {
            Mode = BaseRecordBatchExecutionMode.Atomic,
            Operations =
            [
                new BaseRecordBatchItem
                {
                    ItemId = "item-1",
                    CollectionId = "projects",
                    Kind = BaseRecordMutationKind.Upsert,
                    Upsert = new RecordUpsertRequest
                    {
                        Id = new RecordId("rec_1"),
                        CreatePayload = Payload("created"),
                        UpdatePayload = Payload("updated"),
                        UpdateMode = RecordUpsertUpdateMode.Replace,
                        Condition = RecordUpsertExistenceCondition.UpdateOnly
                    }
                }
            ]
        };

        var json = JsonSerializer.Serialize(request, HPDBaseJsonSerializerContext.Default.BaseRecordBatchRequest);
        var roundTrip = JsonSerializer.Deserialize(json, HPDBaseJsonSerializerContext.Default.BaseRecordBatchRequest);

        Assert.NotNull(roundTrip);
        Assert.Equal(BaseRecordBatchExecutionMode.Atomic, roundTrip.Mode);
        Assert.Equal(BaseRecordMutationKind.Upsert, roundTrip.Operations[0].Kind);
        Assert.Equal(RecordUpsertUpdateMode.Replace, roundTrip.Operations[0].Upsert?.UpdateMode);
        Assert.Contains("\"mode\":\"atomic\"", json, StringComparison.Ordinal);
        Assert.DoesNotContain("$type", json, StringComparison.Ordinal);
    }

    [Fact]
    public void CapabilityContractsStateGuaranteesInsteadOfSupportBooleans()
    {
        var batchProperties = typeof(BatchCapabilityConstraints).GetProperties().Select(property => property.Name).ToArray();
        var revisionProperties = typeof(RevisionCapability).GetProperties().Select(property => property.Name).ToArray();

        Assert.DoesNotContain("Supported", batchProperties);
        Assert.DoesNotContain("FeatureIds", batchProperties);
        Assert.Contains(nameof(BatchCapabilityConstraints.Modes), batchProperties);
        Assert.Contains(nameof(BatchCapabilityConstraints.Isolation), batchProperties);
        Assert.Contains(nameof(RevisionCapability.Patch), revisionProperties);
        Assert.Contains(nameof(RevisionCapability.Replace), revisionProperties);
        Assert.Contains(nameof(RevisionCapability.Delete), revisionProperties);
    }

    [Fact]
    public void LegacyTransactionAndCollectionBatchVocabularyIsAbsent()
    {
        Assert.DoesNotContain("Transaction", Enum.GetNames<BaseOperationKind>());
        Assert.Null(typeof(CollectionOperationMatrix).GetProperty("Batch"));
        Assert.DoesNotContain(
            typeof(IRecordStore).GetMethods(),
            method => method.Name is "CreateAsync" or "PatchAsync" or "ReplaceAsync" or "DeleteAsync");
    }

    [Fact]
    public void ExecutionContractsAreFixedAndProviderNeutral()
    {
        var storeMethods = typeof(IAtomicRecordStore).GetMethods();
        var sessionMethods = typeof(IAtomicRecordSession).GetMethods();

        Assert.Contains(storeMethods, method =>
            method.Name == nameof(IAtomicRecordStore.ExecuteAtomicAsync)
            && method.ReturnType == typeof(ValueTask<RecordMutationExecutionResult>));
        Assert.DoesNotContain(storeMethods, method => method.IsGenericMethod);
        Assert.DoesNotContain(sessionMethods, method => method.Name.Contains("Transaction", StringComparison.Ordinal));
        Assert.DoesNotContain(sessionMethods, method => method.ReturnType.IsGenericType
            && method.ReturnType.GetGenericArguments().Any(argument =>
                argument.Namespace?.StartsWith("Microsoft.Data.Sqlite", StringComparison.Ordinal) == true));
    }

    [Fact]
    public void CanonicalFactSeparatesRequestedUpsertFromPhysicalMutation()
    {
        var fact = new BaseRecordMutationFact
        {
            RequestedOperation = BaseRecordMutationKind.Upsert,
            CommittedOperation = BaseCommittedRecordMutationKind.Patch,
            UpsertOutcome = RecordUpsertOutcome.Updated,
            Collection = Collection(),
            Event = new EventReference
            {
                EventId = "evt_1",
                Type = BaseEventTypes.RecordPatched,
                Guarantee = EventDeliveryGuarantee.BestEffort
            }
        };

        Assert.Equal(BaseRecordMutationKind.Upsert, fact.RequestedOperation);
        Assert.Equal(BaseCommittedRecordMutationKind.Patch, fact.CommittedOperation);
        Assert.Equal(RecordUpsertOutcome.Updated, fact.UpsertOutcome);
        Assert.DoesNotContain("Upsert", Enum.GetNames<BaseCommittedRecordMutationKind>());
    }

    [Fact]
    public void ProcessingOutcomeEnforcesItsErrorInvariant()
    {
        var error = Error(BaseMutationErrorCodes.BatchItemInvalid);

        Assert.Throws<ArgumentException>(() =>
            new AtomicMutationProcessingResult(
                AtomicMutationProcessingOutcome.ReadyToCommit,
                [],
                error));
        Assert.Throws<ArgumentException>(() =>
            new AtomicMutationProcessingResult(
                AtomicMutationProcessingOutcome.Failed,
                []));

        var ready = new AtomicMutationProcessingResult(
            AtomicMutationProcessingOutcome.ReadyToCommit,
            []);
        var failed = new AtomicMutationProcessingResult(
            AtomicMutationProcessingOutcome.Failed,
            [],
            error);

        Assert.Null(ready.Error);
        Assert.Same(error, failed.Error);
    }

    [Fact]
    public void ProviderOutcomeDoesNotExposeProvisionalFactsWhenIndeterminate()
    {
        var ready = new AtomicMutationProcessingResult(
            AtomicMutationProcessingOutcome.ReadyToCommit,
            []);

        Assert.Throws<ArgumentException>(() =>
            new RecordMutationExecutionResult(
                RecordMutationExecutionOutcome.Indeterminate,
                ready,
                Error(BaseMutationErrorCodes.BatchIndeterminate)));
        Assert.Throws<ArgumentException>(() =>
            new RecordMutationExecutionResult(
                RecordMutationExecutionOutcome.Committed,
                processing: null));

        var indeterminate = new RecordMutationExecutionResult(
            RecordMutationExecutionOutcome.Indeterminate,
            processing: null,
            Error(BaseMutationErrorCodes.BatchIndeterminate));

        Assert.Null(indeterminate.Processing);
    }

    [Fact]
    public void StableFailureCodesMatchTheFinalLedger()
    {
        Assert.Equal("base.runtime.batch.indeterminate", BaseMutationErrorCodes.BatchIndeterminate);
        Assert.Equal("base.runtime.batch.multipleStores", BaseMutationErrorCodes.BatchMultipleStores);
        Assert.Equal("base.runtime.upsert.preconditionFailed", BaseMutationErrorCodes.UpsertPreconditionFailed);
        Assert.Equal("base.runtime.transaction.timeout", BaseMutationErrorCodes.TransactionTimeout);
    }

    private static RecordPayload Payload(string value)
    {
        using var document = JsonDocument.Parse($$"""{"value":"{{value}}"}""");
        return new RecordPayload
        {
            Kind = RecordPayloadKind.Json,
            Json = document.RootElement.Clone()
        };
    }

    private static CollectionDefinition Collection() => new()
    {
        Id = "projects",
        Name = "projects",
        Kind = "base",
        SchemaMode = SchemaMode.Loose,
        UnknownFields = UnknownFieldPolicy.Preserve
    };

    private static BaseError Error(string code) => new()
    {
        Code = code,
        Message = "The operation failed.",
        Category = ErrorCategory.Validation
    };
}
