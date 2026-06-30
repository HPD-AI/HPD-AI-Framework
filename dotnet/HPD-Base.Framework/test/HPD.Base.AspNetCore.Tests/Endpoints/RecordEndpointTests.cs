namespace HPD.Base.AspNetCore.Tests.Endpoints;

public sealed class RecordEndpointTests
{
    [Fact]
    public async Task ListQueryGetCreatePatchReplaceDeleteRoutesWork()
    {
        await using var app = await TestBaseApp.CreateAsync();
        var client = app.GetTestClient();
        var created = await client.CreateRecordAsync(app, "alpha");

        var list = await app.ReadBaseJsonAsync<RecordPage>((await client.GetAsync("/base/collections/items/records?where[title]=alpha")).Content);
        list!.Items.Should().Contain(item => item.Id == created.Id);

        var query = await client.PostAsync("/base/collections/items/query", JsonContent.Create(new RecordQuery
        {
            Select = ["title"]
        }, HPDBaseJsonSerializerContext.Default.RecordQuery));
        query.StatusCode.Should().Be(HttpStatusCode.OK);

        var get = await app.ReadBaseJsonAsync<RecordEnvelope>((await client.GetAsync($"/base/collections/items/records/{created.Id.Value}")).Content);
        get!.Id.Should().Be(created.Id);

        var patch = await client.PatchAsync($"/base/collections/items/records/{created.Id.Value}", JsonContent.Create(new RecordPatchRequest
        {
            ExpectedRevision = created.Metadata.Revision,
            Patch = TestBaseApp.Patch("title", "beta")
        }, HPDBaseJsonSerializerContext.Default.RecordPatchRequest));
        patch.StatusCode.Should().Be(HttpStatusCode.OK);
        var patched = await app.ReadBaseJsonAsync<RecordEnvelope>(patch.Content);

        var replace = await client.PutAsync($"/base/collections/items/records/{created.Id.Value}", JsonContent.Create(new RecordReplaceRequest
        {
            ExpectedRevision = patched!.Metadata.Revision,
            Payload = TestBaseApp.Payload(("title", "gamma"))
        }, HPDBaseJsonSerializerContext.Default.RecordReplaceRequest));
        replace.StatusCode.Should().Be(HttpStatusCode.OK);
        var replaced = await app.ReadBaseJsonAsync<RecordEnvelope>(replace.Content);

        var delete = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/base/collections/items/records/{created.Id.Value}")
        {
            Content = JsonContent.Create(new RecordDeleteRequest
            {
                ExpectedRevision = replaced!.Metadata.Revision
            }, HPDBaseJsonSerializerContext.Default.RecordDeleteRequest)
        });

        delete.StatusCode.Should().Be(HttpStatusCode.OK);
        (await app.ReadBaseJsonAsync<DeleteResult>(delete.Content))!.Deleted.Should().BeTrue();
    }

    [Fact]
    public async Task MissingRecordReturnsProblemDetails()
    {
        await using var app = await TestBaseApp.CreateAsync();
        var response = await app.GetTestClient().GetAsync("/base/collections/items/records/missing");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task InvalidJsonBodyReturnsProblemDetails()
    {
        await using var app = await TestBaseApp.CreateAsync();
        var response = await app.GetTestClient().PostAsync("/base/collections/items/records", new StringContent("{", System.Text.Encoding.UTF8, "application/json"));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("base.http.body.invalidJson");
    }
}
