
namespace HPD.Base;

internal sealed class DefaultBaseCapabilityValidator : IBaseCapabilityValidator
{
    private readonly IRecordStoreRegistry _stores;

    /// <summary>Initializes a new instance.</summary>
    public DefaultBaseCapabilityValidator(IRecordStoreRegistry stores)
    {
        _stores = stores;
    }

    /// <summary>Executes the validate capabilities operation.</summary>
    public BaseRuntimeValidationResult ValidateCapabilities(BaseDescriptorSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var issues = new List<BaseRuntimeValidationIssue>();
        foreach (var collection in snapshot.Schema.Collections ?? [])
        {
            var store = ResolveStore(collection.Id, collection.Store?.StoreId);
            if (store is null)
            {
                if (collection.Operations is not null)
                {
                    issues.Add(Fatal(
                        "base.runtime.capability.store.missing",
                        $"Collection '{collection.Id}' declares operations but has no registered store.",
                        collection.Id));
                }

                continue;
            }

            ValidateOperations(
                collection.Id,
                collection.Operations,
                store,
                issues);
            ValidateAtomicGuarantees(collection.Id, store, issues);
        }

        foreach (var feature in snapshot.Capabilities.Families.SelectMany(family => family.Features ?? []))
        {
            ValidateRevisionFeature(feature, issues);
            ValidateStreamingFeature(feature, issues);
        }

        return new BaseRuntimeValidationResult
        {
            Succeeded = issues.Count == 0,
            Issues = issues.Count == 0 ? null : issues.ToArray()
        };
    }

    private IRecordStore? ResolveStore(string collectionId, string? storeId) =>
        !string.IsNullOrWhiteSpace(storeId)
            ? _stores.GetStore(storeId)
            : _stores.GetStoreForCollection(collectionId);

    private void ValidateRevisionFeature(
        CapabilityFeatureDescriptor feature,
        List<BaseRuntimeValidationIssue> issues)
    {
        var revision = feature.Constraints?.StoreRevision;
        if (revision is null)
        {
            return;
        }

        foreach (var collectionId in feature.AppliesTo ?? [])
        {
            var store = ResolveStore(collectionId, null);
            if (store is null)
            {
                issues.Add(Fatal(
                    "base.runtime.capability.revision.storeMissing",
                    $"Revision feature '{feature.FeatureId}' applies to '{collectionId}' but no store is registered.",
                    collectionId));
                continue;
            }

            if ((revision.Patch || revision.Replace || revision.Delete)
                && store is not IRecordMutationStore)
            {
                issues.Add(Fatal(
                    "base.runtime.capability.revision.interfaceMismatch",
                    $"Revision mutation feature '{feature.FeatureId}' requires IRecordMutationStore.",
                    collectionId));
            }

            if (revision.Patch && !SupportsExpectedRevision(store, static capability => capability.Patch)
                || revision.Replace && !SupportsExpectedRevision(store, static capability => capability.Replace)
                || revision.Delete && !SupportsExpectedRevision(store, static capability => capability.Delete))
            {
                issues.Add(Fatal(
                    "base.runtime.capability.revision.operationUnsupported",
                    $"Revision feature '{feature.FeatureId}' requires matching atomic expected-revision support.",
                    collectionId));
            }
        }
    }

    private static bool SupportsExpectedRevision(
        IRecordStore store,
        Func<RevisionCapability, bool> operation) =>
        store.Capabilities.Revision is
        {
            Supported: true,
            Guarantee: RevisionGuarantee.Store or RevisionGuarantee.Native
        } revision
        && operation(revision);

    private void ValidateStreamingFeature(
        CapabilityFeatureDescriptor feature,
        List<BaseRuntimeValidationIssue> issues)
    {
        if (feature.Constraints?.StoreStreaming is null)
        {
            return;
        }

        foreach (var collectionId in feature.AppliesTo ?? [])
        {
            var store = ResolveStore(collectionId, null);
            if (store is null)
            {
                issues.Add(Fatal(
                    "base.runtime.capability.streaming.storeMissing",
                    $"Streaming feature '{feature.FeatureId}' applies to '{collectionId}' but no store is registered.",
                    collectionId));
                continue;
            }

            if (store is not IStreamingRecordStore)
            {
                issues.Add(Fatal(
                    "base.runtime.capability.streaming.interfaceMismatch",
                    $"Streaming feature '{feature.FeatureId}' requires IStreamingRecordStore.",
                    collectionId));
            }
        }
    }

    private static void ValidateOperations(
        string collectionId,
        CollectionOperationMatrix? matrix,
        IRecordStore store,
        List<BaseRuntimeValidationIssue> issues)
    {
        if (matrix is null)
        {
            return;
        }

        var capabilities = store.Capabilities;
        Check(matrix.List, capabilities.Read.List, "list");
        Check(matrix.Get, capabilities.Read.Get, "get");
        Check(matrix.Create, capabilities.Mutation.Create && store is IRecordMutationStore, "create");
        Check(matrix.Patch, capabilities.Mutation.Patch && store is IRecordMutationStore, "patch");
        Check(matrix.Replace, capabilities.Mutation.Replace && store is IRecordMutationStore, "replace");
        Check(matrix.Delete, capabilities.Mutation.Delete && store is IRecordMutationStore, "delete");
        Check(
            matrix.Upsert,
            capabilities.Upsert?.Atomic == true
            && store is IAtomicRecordStore,
            "upsert");

        void Check(bool claimed, bool supported, string operation)
        {
            if (claimed && !supported)
            {
                issues.Add(Fatal(
                    "base.runtime.capability.crud.unsupported",
                    $"Collection '{collectionId}' claims unsupported '{operation}' operation.",
                    collectionId));
            }
        }
    }

    private static void ValidateAtomicGuarantees(
        string collectionId,
        IRecordStore store,
        List<BaseRuntimeValidationIssue> issues)
    {
        var batch = store.Capabilities.Batch;
        if (batch?.Modes.Contains(BaseRecordBatchExecutionMode.Atomic) != true)
            return;

        if (store is not IAtomicRecordStore
            || !batch.Ordered
            || !batch.ReadYourWrites
            || batch.MinimumAcquisitionTimeout <= TimeSpan.Zero
            || batch.MinimumTransactionTimeout <= TimeSpan.Zero
            || batch.MinimumCommitCompletionTimeout <= TimeSpan.Zero
            || batch.TimeoutGranularity <= TimeSpan.Zero)
        {
            issues.Add(Fatal(
                "base.runtime.capability.atomic.interfaceMismatch",
                $"Collection '{collectionId}' advertises atomic execution without the required interface, ordering, and read-your-writes guarantees.",
                collectionId));
        }
    }

    private static BaseRuntimeValidationIssue Fatal(string code, string message, string targetRef) => new()
    {
        Severity = BaseRuntimeValidationSeverity.Fatal,
        Kind = BaseRuntimeValidationFailureKind.CapabilityDependencyConflict,
        Code = code,
        Message = message,
        TargetRef = targetRef
    };
}
