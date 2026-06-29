using HPD.Base.Runtime.Descriptors;
using HPD.Base.Runtime.Stores;
using HPD.Base.Results;
using HPD.Base.Stores;

namespace HPD.Base.Runtime.Capabilities;

internal sealed class DefaultBaseCapabilityValidator : IBaseCapabilityValidator
{
    private readonly IRecordStoreRegistry _stores;

    public DefaultBaseCapabilityValidator(IRecordStoreRegistry stores)
    {
        _stores = stores;
    }

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

            ValidateCrud(collection.Id, collection.Operations, store.Capabilities.Crud, issues);
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
        HPD.Base.Descriptors.CapabilityFeatureDescriptor feature,
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

            if (revision.Patch && store is not IRevisionedRecordStore)
            {
                issues.Add(Fatal(
                    "base.runtime.capability.revision.interfaceMismatch",
                    $"Revision patch feature '{feature.FeatureId}' requires IRevisionedRecordStore.",
                    collectionId));
            }

            if (revision.Delete && !SupportsExpectedRevisionDelete(store))
            {
                issues.Add(Fatal(
                    "base.runtime.capability.revision.deleteUnsupported",
                    $"Revision delete feature '{feature.FeatureId}' requires a store that advertises atomic expected-revision delete.",
                    collectionId));
            }
        }
    }

    private static bool SupportsExpectedRevisionDelete(IRecordStore store) =>
        store.Capabilities.Revision is
        {
            Supported: true,
            Delete: true,
            Guarantee: RevisionGuarantee.Store or RevisionGuarantee.Native
        };

    private void ValidateStreamingFeature(
        HPD.Base.Descriptors.CapabilityFeatureDescriptor feature,
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

    private static void ValidateCrud(
        string collectionId,
        HPD.Base.Schema.CollectionOperationMatrix? matrix,
        CrudCapability capability,
        List<BaseRuntimeValidationIssue> issues)
    {
        if (matrix is null)
        {
            return;
        }

        Check(matrix.List, capability.List, "list");
        Check(matrix.Get, capability.Get, "get");
        Check(matrix.Create, capability.Create, "create");
        Check(matrix.Patch, capability.Patch, "patch");
        Check(matrix.Replace, capability.Replace, "replace");
        Check(matrix.Delete, capability.Delete, "delete");

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

    private static BaseRuntimeValidationIssue Fatal(string code, string message, string targetRef) => new()
    {
        Severity = BaseRuntimeValidationSeverity.Fatal,
        Kind = BaseRuntimeValidationFailureKind.CapabilityDependencyConflict,
        Code = code,
        Message = message,
        TargetRef = targetRef
    };
}
