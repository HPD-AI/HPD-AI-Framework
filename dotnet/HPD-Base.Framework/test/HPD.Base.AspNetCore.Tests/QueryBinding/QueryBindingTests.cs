namespace HPD.Base.AspNetCore.Tests.QueryBinding;

public sealed class QueryBindingTests
{
    [Theory]
    [InlineData("?where[title]=alpha")]
    [InlineData("?where[title][neq]=alpha")]
    [InlineData("?where[title][in]=alpha,beta")]
    [InlineData("?where[title][isDefined]=true")]
    [InlineData("?sort=-title&nulls[title]=last")]
    [InlineData("?page=2&perPage=5")]
    [InlineData("?offset=1&limit=5")]
    [InlineData("?cursor=abc&cursorDir=before&limit=5")]
    [InlineData("?select=title&include=owner&count=exact&ext[module.arg]=value")]
    public async Task SupportedQueryGrammarBinds(string query)
    {
        await using var app = await TestBaseApp.CreateAsync();
        var httpContext = new Microsoft.AspNetCore.Http.DefaultHttpContext();
        httpContext.Request.QueryString = new Microsoft.AspNetCore.Http.QueryString(query);

        var result = await app.Services.GetRequiredService<HPD.Base.AspNetCore.IBaseHttpQueryBinder>()
            .BindListQueryAsync(httpContext);

        result.Status.Should().Be(OperationStatus.Ok);
        result.Value.Should().NotBeNull();
    }

    [Theory]
    [InlineData("?filter={\"kind\":\"compare\",\"field\":\"title\",\"operator\":\"equal\",\"value\":{\"kind\":\"string\",\"string\":\"alpha\"}}&where[title]=alpha", "base.http.query.mixedFilter")]
    [InlineData("?where[title][unknown]=alpha", "base.http.query.unknownOperator")]
    [InlineData("?count=wrong", "base.http.query.invalidCount")]
    public async Task AmbiguousOrMalformedQueryReturnsValidationProblem(string query, string code)
    {
        await using var app = await TestBaseApp.CreateAsync();
        var response = await app.GetTestClient().GetAsync("/base/collections/items/records" + query);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadAsStringAsync()).Should().Contain(code);
    }

    [Fact]
    public async Task HeadersBindIntoMutationRequests()
    {
        await using var app = await TestBaseApp.CreateAsync();
        var client = app.GetTestClient();
        var created = await client.CreateRecordAsync(app);

        using var patch = new HttpRequestMessage(HttpMethod.Patch, $"/base/collections/items/records/{created.Id.Value}")
        {
            Content = JsonContent.Create(new RecordPatchRequest
            {
                Patch = TestBaseApp.Patch("title", "from-header")
            }, HPDBaseJsonSerializerContext.Default.RecordPatchRequest)
        };
        patch.Headers.TryAddWithoutValidation("If-Match", created.Metadata.ETag);

        (await client.SendAsync(patch)).StatusCode.Should().Be(HttpStatusCode.OK);

    }
}
