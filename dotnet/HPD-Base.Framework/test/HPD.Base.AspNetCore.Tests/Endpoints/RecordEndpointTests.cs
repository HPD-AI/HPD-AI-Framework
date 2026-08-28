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

        var query = await client.PostAsync("/base/collections/items/records:query", JsonContent.Create(new RecordQuery
        {
            Select = ["title"]
        }, HPDBaseJsonSerializerContext.Default.RecordQuery));
        query.StatusCode.Should().Be(HttpStatusCode.OK);

        var get = await app.ReadBaseJsonAsync<RecordEnvelope>((await client.GetAsync($"/base/collections/items/records/{created.Id.Value}")).Content);
        get!.Id.Should().Be(created.Id);

        var patch = await client.PatchAsync($"/base/collections/items/records/{created.Id.Value}", JsonContent.Create(new RecordPatchRequest
        {
            ExpectedRevision = created.Metadata.Revision,
            Patch = TestBaseApp.Patch("title", "beta"),
            RemovedFieldIds = []
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
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("base.http.body.invalidJson")
            .And.Contain("Request body is not valid JSON.")
            .And.NotContain("Path:")
            .And.NotContain("BytePositionInLine")
            .And.NotContain("System.Text.Json");
    }

    [Fact]
    public async Task UnknownLengthRequestBodyCannotBypassConfiguredLimit()
    {
        await using var app = await TestBaseApp.CreateAsync(
            configureAspNetCore: options => options.Limits.MaxRequestBodyLength = 64);
        var content = new UnknownLengthContent(
            """{"payload":{"kind":"json","json":{"title":"this body is deliberately much larger than sixty-four bytes"}}}""");

        var response = await app.GetTestClient().PostAsync(
            "/base/collections/items/records",
            content);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("base.http.body.tooLarge")
            .And.Contain("Request body exceeds the configured maximum length.");
    }

    [Theory]
    [InlineData("/base/collections/items/records:query", false)]
    [InlineData("/base/collections/items/records:query", true)]
    [InlineData("/base/collections/items/records/missing", false)]
    [InlineData("/base/collections/items/records/missing", true)]
    public async Task OptionalQueryAndDeleteBodiesReturnStablePayloadTooLarge(string path, bool unknownLength)
    {
        await using var app = await TestBaseApp.CreateAsync(
            configureAspNetCore: options => options.Limits.MaxRequestBodyLength = 64);
        const string payload = """{"padding":"this body is deliberately much larger than sixty-four bytes so it must be rejected"}""";
        HttpContent content = unknownLength
            ? new UnknownLengthContent(payload)
            : new StringContent(payload, System.Text.Encoding.UTF8, "application/json");
        var request = new HttpRequestMessage(
            path.EndsWith("records:query", StringComparison.Ordinal) ? HttpMethod.Post : HttpMethod.Delete,
            path)
        {
            Content = content,
        };

        HttpResponseMessage response = await app.GetTestClient().SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.RequestEntityTooLarge);
        string body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("base.http.body.tooLarge").And.NotContain("RequestBodyTooLargeException");
    }

    [Fact]
    public async Task BatchAndUpsertRoutesUseCanonicalMutationRuntime()
    {
        await using var app = await TestBaseApp.CreateAsync();
        var client = app.GetTestClient();
        var upsertId = RecordId.Create("upsert-route");

        var upsertResponse = await client.PutAsync(
            $"/base/collections/items/records/{upsertId.Value}:upsert",
            JsonContent.Create(
                new RecordUpsertRequest
                {
                    Id = upsertId,
                    CreatePayload = TestBaseApp.Payload(("title", "created")),
                    UpdatePayload = TestBaseApp.Patch("title", "updated"),
                    UpdateMode = RecordUpsertUpdateMode.Patch,
                    Condition = RecordUpsertExistenceCondition.Any
                },
                HPDBaseJsonSerializerContext.Default.RecordUpsertRequest));

        upsertResponse.StatusCode.Should().Be(HttpStatusCode.Created);
        var upsert = await app.ReadBaseJsonAsync<RecordUpsertResult>(upsertResponse.Content);
        upsert!.Outcome.Should().Be(RecordUpsertOutcome.Created);
        upsert.Record.Id.Should().Be(upsertId);

        var batchResponse = await client.PostAsync(
            "/base/records/batch",
            JsonContent.Create(
                new BaseRecordBatchRequest
                {
                    Mode = BaseRecordBatchExecutionMode.Atomic,
                    Operations =
                    [
                        new BaseRecordBatchItem
                        {
                            ItemId = "first",
                            CollectionId = "items",
                            Kind = BaseRecordMutationKind.Create,
                            Create = new RecordCreateRequest
                            {
                                RequestedId = RecordId.Create("batch-route"),
                                Payload = TestBaseApp.Payload(("title", "one"))
                            }
                        },
                        new BaseRecordBatchItem
                        {
                            ItemId = "second",
                            CollectionId = "items",
                            Kind = BaseRecordMutationKind.Patch,
                            RecordId = RecordId.Create("batch-route"),
                            Patch = new RecordPatchRequest
                            {
                                Patch = TestBaseApp.Patch("title", "two"),
                                RemovedFieldIds = []
                            }
                        }
                    ]
                },
                HPDBaseJsonSerializerContext.Default.BaseRecordBatchRequest));

        batchResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var batch = await app.ReadBaseJsonAsync<BaseRecordBatchResult>(batchResponse.Content);
        batch!.Outcome.Should().Be(BaseRecordBatchOutcome.Committed);
        batch.Items.Should().OnlyContain(item => item.Disposition == BaseRecordBatchItemDisposition.Committed);
    }

    [Fact]
    public async Task UpsertRouteRejectsBodyIdMismatch()
    {
        await using var app = await TestBaseApp.CreateAsync();
        var response = await app.GetTestClient().PutAsync(
            "/base/collections/items/records/route-id:upsert",
            JsonContent.Create(
                new RecordUpsertRequest
                {
                    Id = RecordId.Create("body-id"),
                    CreatePayload = TestBaseApp.Payload(("title", "created")),
                    UpdatePayload = TestBaseApp.Patch("title", "updated"),
                    UpdateMode = RecordUpsertUpdateMode.Patch,
                    Condition = RecordUpsertExistenceCondition.Any
                },
                HPDBaseJsonSerializerContext.Default.RecordUpsertRequest));

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain("base.http.recordId.conflict");
    }

    private sealed class UnknownLengthContent(string body) : HttpContent
    {
        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }

        protected override Task SerializeToStreamAsync(
            Stream stream,
            TransportContext? context) =>
            stream.WriteAsync(System.Text.Encoding.UTF8.GetBytes(body)).AsTask();
    }
}
