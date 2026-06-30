namespace HPD.Base.AspNetCore.Tests.Descriptors;

public sealed class DescriptorTests
{
    [Fact]
    public async Task ProjectionDescriptorContainsExactPhaseOneRouteTable()
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
            "Post /base/collections/{collectionId}/query",
            "Get /base/collections/{collectionId}/records/{id}",
            "Post /base/collections/{collectionId}/records",
            "Patch /base/collections/{collectionId}/records/{id}",
            "Put /base/collections/{collectionId}/records/{id}",
            "Delete /base/collections/{collectionId}/records/{id}"
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
            "base.recordQuery"
        ]);
    }
}
