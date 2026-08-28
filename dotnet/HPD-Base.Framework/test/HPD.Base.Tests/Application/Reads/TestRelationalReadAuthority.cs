namespace HPD.Base.Tests;

internal static class TestRelationalReadAuthority
{
    internal static BaseRelationalReadSnapshotAuthority Create(
        BaseRelationalReadExecutionRequest request,
        long? schemaGeneration = null) =>
        BaseRelationalReadSnapshotAuthorityContract.Create(
            request.ApplicationId,
            request.LogicalStoreId,
            "test-relational-store-instance",
            restoreEpoch: 0,
            schemaGeneration ?? request.Plan.SchemaGeneration,
            request.LogicalSchemaChecksum,
            request.Plan.Sources.Select(static source => source.CollectionId)
                .Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)
                .Select(static id => new BaseRelationalCollectionSnapshotAuthority
                {
                    CollectionId = id,
                    CollectionGeneration = 0,
                }));
}
