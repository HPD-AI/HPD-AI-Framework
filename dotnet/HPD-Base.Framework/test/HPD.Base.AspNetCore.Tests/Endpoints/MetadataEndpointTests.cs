namespace HPD.Base.AspNetCore.Tests.Endpoints;

public sealed class MetadataEndpointTests
{
    [Theory]
    [InlineData("/base/manifest")]
    [InlineData("/base/admin/manifest")]
    public async Task ManifestEndpointsReturnManifest(string path)
    {
        await using var app = await TestBaseApp.CreateAsync();
        var response = await app.GetTestClient().GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var manifest = await app.ReadBaseJsonAsync<BaseManifest>(response.Content);
        manifest!.Runtime.Should().NotBeNull();
    }

    [Theory]
    [InlineData("/base/capabilities")]
    [InlineData("/base/admin/capabilities")]
    public async Task CapabilityEndpointsReturnCapabilities(string path)
    {
        await using var app = await TestBaseApp.CreateAsync();
        var response = await app.GetTestClient().GetAsync(path);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadAsStringAsync()).Should().Contain("families");
    }

    [Theory]
    [InlineData("/base/schema")]
    [InlineData("/base/admin/schema")]
    public async Task SchemaEndpointsReturnSchema(string path)
    {
        await using var app = await TestBaseApp.CreateAsync();
        var schema = await app.ReadBaseJsonAsync<SchemaMetadata>((await app.GetTestClient().GetAsync(path)).Content);

        schema!.Collections.Should().Contain(collection => collection.Id == "items");
    }

    [Fact]
    public async Task ExpandedManifestCanSelectAllAllowedSections()
    {
        await using var app = await TestBaseApp.CreateAsync();
        var expanded = await app.ReadBaseJsonAsync<ExpandedBaseManifest>((await app.GetTestClient().GetAsync("/base/manifest?expand=schema&expand=capabilities,health,diagnostics,collections")).Content);

        expanded!.Schema.Should().NotBeNull();
        expanded.Capabilities.Should().NotBeNull();
        expanded.Health.Should().NotBeNull();
        expanded.Diagnostics.Should().NotBeNull();
        expanded.Collections.Should().Contain(collection => collection.Id == "items");
    }
}
