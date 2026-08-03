using HPD.Base;
using Microsoft.AspNetCore.Http;

namespace HPD.Base.AspNetCore.Tests;

public sealed class EndpointIntegrationTests
{
    [Fact]
    public async Task ManifestExpansionAndCollectionsAreServed()
    {
        await using var app = await TestBaseApp.CreateAsync();
        var client = app.GetTestClient();

        var manifestResponse = await client.GetAsync("/base/manifest");
        var manifest = await ReadJson<BaseManifest>(app, manifestResponse.Content);
        manifest.Should().NotBeNull();
        manifest!.Projections.Should().Contain(projection => projection.Id == "hpd.base.aspnetcore");

        var expanded = await client.GetAsync("/base/manifest?expand=schema,capabilities,health,diagnostics,collections");
        expanded.StatusCode.Should().Be(HttpStatusCode.OK);

        var collectionsResponse = await client.GetAsync("/base/collections");
        var collections = await ReadJson<CollectionDefinition[]>(app, collectionsResponse.Content);
        collections.Should().Contain(collection => collection.Id == "items");
    }

    [Fact]
    public async Task UnknownManifestExpandReturnsProblemDetails()
    {
        await using var app = await TestBaseApp.CreateAsync();
        var response = await app.GetTestClient().GetAsync("/base/manifest?expand=mystery");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("base.http.manifest.unknownExpand");
    }

    [Fact]
    public async Task RecordCrudRoutesDelegateThroughRuntime()
    {
        await using var app = await TestBaseApp.CreateAsync();
        var client = app.GetTestClient();

        var create = await client.PostAsync("/base/collections/items/records", JsonContent.Create(new RecordCreateRequest
        {
            Payload = TestBaseApp.Payload(("title", "hello"))
        }, HPDBaseJsonSerializerContext.Default.RecordCreateRequest));
        create.StatusCode.Should().Be(HttpStatusCode.Created);
        create.Headers.Location.Should().NotBeNull();

        var created = await ReadJson<RecordEnvelope>(app, create.Content);
        created.Should().NotBeNull();

        var getResponse = await client.GetAsync($"/base/collections/items/records/{created!.Id.Value}");
        var get = await ReadJson<RecordEnvelope>(app, getResponse.Content);
        get!.Id.Should().Be(created.Id);

        var listResponse = await client.GetAsync("/base/collections/items/records?where[title]=hello");
        var list = await ReadJson<RecordPage>(app, listResponse.Content);
        list!.Items.Should().Contain(item => item.Id == created.Id);

        var patch = await client.PatchAsync($"/base/collections/items/records/{created.Id.Value}", JsonContent.Create(new RecordPatchRequest
        {
            Patch = TestBaseApp.Patch("title", "patched"),
            ExpectedRevision = created.Metadata.Revision
        }, HPDBaseJsonSerializerContext.Default.RecordPatchRequest));
        patch.StatusCode.Should().Be(HttpStatusCode.OK);

        var patched = await ReadJson<RecordEnvelope>(app, patch.Content);
        var delete = await client.SendAsync(new HttpRequestMessage(HttpMethod.Delete, $"/base/collections/items/records/{created.Id.Value}")
        {
            Content = JsonContent.Create(new RecordDeleteRequest
            {
                ExpectedRevision = patched!.Metadata.Revision,
                ReturnPrevious = true
            }, HPDBaseJsonSerializerContext.Default.RecordDeleteRequest)
        });

        delete.StatusCode.Should().Be(HttpStatusCode.OK);
        var deleted = await ReadJson<DeleteResult>(app, delete.Content);
        deleted!.Previous.Should().NotBeNull();
    }

    [Fact]
    public async Task DeferredRoutesAreAbsent()
    {
        await using var app = await TestBaseApp.CreateAsync();
        var client = app.GetTestClient();

        (await client.PutAsJsonAsync("/base/collections/items/records", new { })).StatusCode.Should().Be(HttpStatusCode.MethodNotAllowed);
        (await client.PostAsJsonAsync("/base/batch", new { })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/base/files/anything")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/base/graphql")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/base/openapi.json")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task AtomicBatchIdempotencyHeaderReturnsCommittedThenDuplicate()
    {
        await using var app = await TestBaseApp.CreateAsync();
        HttpClient client = app.GetTestClient();
        var batch = new BaseRecordBatchRequest
        {
            Mode = BaseRecordBatchExecutionMode.Atomic,
            Operations =
            [
                new BaseRecordBatchItem
                {
                    ItemId = "create-1", CollectionId = "items", Kind = BaseRecordMutationKind.Create,
                    Create = new RecordCreateRequest { RequestedId = new RecordId("atomic-http-1"), Payload = TestBaseApp.Payload(("title", "once")) },
                }
            ],
        };
        async Task<HttpResponseMessage> SendAsync()
        {
            var message = new HttpRequestMessage(HttpMethod.Post, "/base/records/batch")
            {
                Content = JsonContent.Create(batch, HPDBaseJsonSerializerContext.Default.BaseRecordBatchRequest),
            };
            message.Headers.Add(BaseHttpHeaders.IdempotencyKey, "request-1");
            return await client.SendAsync(message);
        }

        HttpResponseMessage first = await SendAsync();
        HttpResponseMessage second = await SendAsync();
        first.StatusCode.Should().Be(HttpStatusCode.OK);
        second.StatusCode.Should().Be(HttpStatusCode.OK);
        first.Headers.GetValues(BaseHttpHeaders.RequestDisposition).Should().Equal("committed");
        second.Headers.GetValues(BaseHttpHeaders.RequestDisposition).Should().Equal("duplicate");
        (await ReadJson<BaseRecordBatchResult>(app, second.Content))!.RequestDisposition.Should().Be(BaseMutationRequestDisposition.Duplicate);
    }

    [Fact]
    public async Task AtomicBatchRejectsUnknownJsonMembersBeforeIdentityConstruction()
    {
        await using var app = await TestBaseApp.CreateAsync();
        HttpClient client = app.GetTestClient();
        using var message = new HttpRequestMessage(HttpMethod.Post, "/base/records/batch")
        {
            Content = new StringContent(
                """{"mode":"atomic","operations":[],"unknownBehavior":"must-not-hash"}""",
                System.Text.Encoding.UTF8,
                "application/json"),
        };
        message.Headers.Add(BaseHttpHeaders.IdempotencyKey, "request-with-unknown-member");

        HttpResponseMessage response = await client.SendAsync(message);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static async Task<T?> ReadJson<T>(WebApplication app, HttpContent content)
    {
        var json = await content.ReadAsStringAsync();
        return JsonSerializer.Deserialize<T>(json, app.Services.GetRequiredService<IHPDBaseRuntime>().Json.Options);
    }
}
