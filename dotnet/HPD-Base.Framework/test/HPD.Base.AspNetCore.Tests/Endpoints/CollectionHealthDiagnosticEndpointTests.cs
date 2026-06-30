namespace HPD.Base.AspNetCore.Tests.Endpoints;

public sealed class CollectionHealthDiagnosticEndpointTests
{
    [Theory]
    [InlineData("/base/collections")]
    [InlineData("/base/admin/collections")]
    public async Task CollectionListEndpointsReturnCollectionArray(string path)
    {
        await using var app = await TestBaseApp.CreateAsync();
        var collections = await app.ReadBaseJsonAsync<CollectionDefinition[]>((await app.GetTestClient().GetAsync(path)).Content);

        collections.Should().Contain(collection => collection.Id == "items");
    }

    [Theory]
    [InlineData("/base/collections/items")]
    [InlineData("/base/admin/collections/items")]
    public async Task CollectionGetEndpointsReturnCollection(string path)
    {
        await using var app = await TestBaseApp.CreateAsync();
        var collection = await app.ReadBaseJsonAsync<CollectionDefinition>((await app.GetTestClient().GetAsync(path)).Content);

        collection!.Id.Should().Be("items");
    }

    [Theory]
    [InlineData("/base/health")]
    [InlineData("/base/admin/health")]
    [InlineData("/base/diagnostics")]
    [InlineData("/base/admin/diagnostics")]
    public async Task HealthAndDiagnosticsEndpointsReturnArrays(string path)
    {
        await using var app = await TestBaseApp.CreateAsync();
        var response = await app.GetTestClient().GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().StartWith("[");
    }
}
