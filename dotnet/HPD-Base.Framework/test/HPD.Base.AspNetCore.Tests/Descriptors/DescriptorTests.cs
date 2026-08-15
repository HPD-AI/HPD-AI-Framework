namespace HPD.Base.AspNetCore.Tests.Descriptors;

public sealed class DescriptorTests
{
    [Fact]
    public async Task ProjectionDescriptorContainsExactRouteTable()
    {
        await using var app = await TestBaseApp.CreateAsync();
        var manifest = await app.ReadBaseJsonAsync<BaseManifest>((await app.GetTestClient().GetAsync("/base/manifest")).Content);

        var routes = manifest!.Projections!.Single(projection => projection.Id == "hpd.base.aspnetcore").Routes!;
        routes.Select(route => $"{route.Method} {route.Path}").Should().BeEquivalentTo([
            "Get /base/manifest",
            "Get /base/capabilities",
            "Get /base/schema",
            "Get /base/collections",
            "Get /base/collections/{collectionId}",
            "Get /base/health",
            "Get /base/diagnostics",
            "Get /base/collections/{collectionId}/records",
            "Post /base/collections/{collectionId}/records:query",
            "Get /base/collections/{collectionId}/records/{id}",
            "Post /base/collections/{collectionId}/records",
            "Patch /base/collections/{collectionId}/records/{id}",
            "Put /base/collections/{collectionId}/records/{id}",
            "Delete /base/collections/{collectionId}/records/{id}",
            "Post /base/records/batch",
            "Put /base/collections/{collectionId}/records/{id}:upsert",
            "Get /base/client-generation",
            "Get /base/client-generation"
        ]);
    }

    [Fact]
    public async Task AdminManifestIncludesAdminRouteDescriptors()
    {
        await using var app = await TestBaseApp.CreateAsync();
        var manifest = await app.ReadBaseJsonAsync<BaseManifest>((await app.GetTestClient().GetAsync("/base/admin/manifest")).Content);

        manifest!.Projections!.Single(projection => projection.Id == "hpd.base.aspnetcore")
            .Routes!
            .Where(route => route.Path.Contains("/admin/", StringComparison.Ordinal))
            .Should()
            .OnlyContain(route => route.Visibility == VisibilityLevel.Admin && route.AuthRequirement == RouteAuthRequirement.Admin);
    }

    [Fact]
    public async Task DtoContractsIncludeProblemDetailsAndRecordRequests()
    {
        await using var app = await TestBaseApp.CreateAsync();
        var manifest = await app.ReadBaseJsonAsync<BaseManifest>((await app.GetTestClient().GetAsync("/base/manifest")).Content);
        var dtoIds = manifest!.DtoContracts!.Select(dto => dto.Id);

        dtoIds.Should().Contain([
            "hpd.base.aspnet.problemDetails",
            "base.recordCreateRequest",
            "base.recordPatchRequest",
            "base.recordReplaceRequest",
            "base.recordDeleteRequest",
            BaseDtoIds.BaseRecordBatchRequest,
            BaseDtoIds.BaseRecordBatchResult,
            BaseDtoIds.RecordUpsertRequest,
            BaseDtoIds.RecordUpsertResult,
            "base.recordQuery"
        ]);
    }

    [Fact]
    public async Task ProjectionDoesNotClaimRuntimeOwnedMutationExecutionFeatures()
    {
        await using var app = await TestBaseApp.CreateAsync();
        var manifest = await app.ReadBaseJsonAsync<BaseManifest>(
            (await app.GetTestClient().GetAsync("/base/manifest")).Content);

        var projection = manifest!.Modules!
            .Single(module => module.Id == "hpd.base.aspnetcore");
        projection.ContributedCapabilities.Should().NotContain([
            BaseFeatureIds.RecordsBatch,
            BaseFeatureIds.RecordsUpsert,
            BaseFeatureIds.BatchOrderedIndependent,
            BaseFeatureIds.BatchOrderedStopOnFailure,
            BaseFeatureIds.BatchAtomic,
            BaseFeatureIds.BatchPartialResults
        ]);

        manifest.Capabilities!.FeatureIds.Should().Contain([
            BaseFeatureIds.RecordsBatch,
            BaseFeatureIds.RecordsUpsert,
            BaseFeatureIds.BatchOrderedIndependent,
            BaseFeatureIds.BatchOrderedStopOnFailure,
            BaseFeatureIds.BatchAtomic,
            BaseFeatureIds.BatchPartialResults
        ]);
    }
}
