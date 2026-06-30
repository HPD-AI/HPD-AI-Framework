namespace HPD.Base.AspNetCore.Tests.PublicApi;

public sealed class DeferredFeatureAbsenceTests
{
    [Fact]
    public async Task DeferredHttpRoutesAreAbsent()
    {
        await using var app = await TestBaseApp.CreateAsync();
        var client = app.GetTestClient();

        (await client.PostAsJsonAsync("/base/batch", new { })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/base/files/example")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/base/graphql")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/base/openapi.json")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/base/realtime")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/base/search")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ManifestDoesNotAdvertiseDeferredRoutes()
    {
        await using var app = await TestBaseApp.CreateAsync();
        var manifest = await app.ReadBaseJsonAsync<BaseManifest>((await app.GetTestClient().GetAsync("/base/manifest")).Content);
        var paths = manifest!.Projections!.SelectMany(projection => projection.Routes ?? []).Select(route => route.Path);

        paths.Should().NotContain(path =>
            path.Contains("batch", StringComparison.Ordinal)
            || path.Contains("files", StringComparison.Ordinal)
            || path.Contains("graphql", StringComparison.Ordinal)
            || path.Contains("openapi", StringComparison.Ordinal)
            || path.Contains("search", StringComparison.Ordinal)
            || path.Contains("realtime", StringComparison.Ordinal));
    }
}
