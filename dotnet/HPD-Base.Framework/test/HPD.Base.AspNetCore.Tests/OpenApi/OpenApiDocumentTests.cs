namespace HPD.Base.AspNetCore.Tests.OpenApi;

public sealed class OpenApiDocumentTests
{
    [Fact]
    public async Task MapHPDBaseApiAloneDoesNotExposeOpenApiDocuments()
    {
        await using var app = await TestBaseApp.CreateAsync();
        var response = await app.GetTestClient().GetAsync("/base/openapi/base-public.json");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task OptInMapsPublicAndAdminDocuments()
    {
        await using var app = await TestBaseApp.CreateAsync(mapOpenApi: true);
        using var publicDoc = await GetDocument(app, "base-public");
        using var adminDoc = await GetDocument(app, "base-admin");

        Paths(publicDoc).Should().ContainKey("/base/manifest");
        Paths(publicDoc).Should().ContainKey("/base/collections/{collectionId}/records");
        Paths(publicDoc).Should().NotContainKey("/base/admin/manifest");

        Paths(adminDoc).Should().ContainKey("/base/admin/manifest");
        Paths(adminDoc).Should().ContainKey("/base/admin/collections/{collectionId}");
        Paths(adminDoc).Should().NotContainKey("/base/collections/{collectionId}/records");
    }

    [Fact]
    public async Task OpenApiDocumentEndpointsAreExcludedFromDescriptionsByDefault()
    {
        await using var app = await TestBaseApp.CreateAsync(mapOpenApi: true);

        var endpoint = OpenApiEndpoint(app, "/base/openapi/{documentName}.json");

        endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.IExcludeFromDescriptionMetadata>()?.ExcludeFromDescription.Should().BeTrue();
    }

    [Fact]
    public async Task OpenApiDocumentEndpointDescriptionExclusionCanBeDisabled()
    {
        await using var app = await TestBaseApp.CreateAsync(
            configureOpenApiEndpoints: options => options.ExcludeOpenApiEndpointFromDescription = false,
            mapOpenApi: true);

        var endpoint = OpenApiEndpoint(app, "/base/openapi/{documentName}.json");

        endpoint.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.IExcludeFromDescriptionMetadata>().Should().BeNull();
    }

    [Fact]
    public async Task AdminPolicyExplainAppearsOnlyWhenMapped()
    {
        await using var withoutExplain = await TestBaseApp.CreateAsync(mapOpenApi: true);
        using var defaultAdminDoc = await GetDocument(withoutExplain, "base-admin");
        Paths(defaultAdminDoc).Should().NotContainKey("/base/admin/policy/explain");

        await using var withExplain = await TestBaseApp.CreateAsync(
            configureEndpoints: options => options.MapAdminPolicyExplain = true,
            mapOpenApi: true);
        using var mappedAdminDoc = await GetDocument(withExplain, "base-admin");
        Paths(mappedAdminDoc).Should().ContainKey("/base/admin/policy/explain");
    }

    [Fact]
    public async Task DocumentsIncludeOperationIdsHeadersAndProblemDetails()
    {
        await using var app = await TestBaseApp.CreateAsync(mapOpenApi: true);
        using var publicDoc = await GetDocument(app, "base-public");

        publicDoc.RootElement.GetProperty("info").GetProperty("version").GetString().Should().Be("1.0.0");
        publicDoc.RootElement.GetProperty("x-hpd-document-name").GetString().Should().Be("base-public");

        var create = Operation(publicDoc, "/base/collections/{collectionId}/records", "post");
        create.GetProperty("operationId").GetString().Should().Be(BaseRouteIds.RecordsCreate);
        create.GetProperty("x-hpd-operation-id").GetString().Should().Be(BaseRouteIds.RecordsCreate);
        create.GetProperty("x-hpd-route-visibility").GetString().Should().Be("Public");
        create.GetProperty("x-hpd-request-dto-id").GetString().Should().Be("base.recordCreateRequest");
        create.GetProperty("x-hpd-response-dto-id").GetString().Should().Be("base.recordEnvelope");
        create.GetProperty("x-hpd-error-dto-id").GetString().Should().Be("hpd.base.aspnet.problemDetails");
        create.GetProperty("x-hpd-required-feature-ids").EnumerateArray()
            .Select(featureId => featureId.GetString())
            .Should().Contain("records.create");
        create.GetProperty("parameters").EnumerateArray()
            .Select(parameter => parameter.GetProperty("name").GetString())
            .Should().Contain(["collectionId", BaseHttpHeaders.IdempotencyKey, BaseHttpHeaders.CorrelationId]);

        var list = Operation(publicDoc, "/base/collections/{collectionId}/records", "get");
        list.GetProperty("parameters").EnumerateArray()
            .Select(parameter => parameter.GetProperty("name").GetString())
            .Should().Contain(["collectionId", "filter", "where[field]", "sort", "nulls[field]", "page", "perPage", "offset", "limit", "cursor", "cursorDir", "select", "include", "count", "ext[module.name]"]);

        var batch = Operation(publicDoc, "/base/records/batch", "post");
        batch.GetProperty("operationId").GetString().Should().Be(BaseRouteIds.RecordsBatch);
        batch.GetProperty("x-hpd-request-dto-id").GetString().Should().Be(BaseDtoIds.BaseRecordBatchRequest);
        batch.GetProperty("x-hpd-response-dto-id").GetString().Should().Be(BaseDtoIds.BaseRecordBatchResult);
        batch.GetProperty("x-hpd-required-feature-ids").EnumerateArray()
            .Select(featureId => featureId.GetString())
            .Should().Contain(BaseFeatureIds.RecordsBatch);

        var upsert = Operation(
            publicDoc,
            "/base/collections/{collectionId}/records/{id}:upsert",
            "put");
        upsert.GetProperty("operationId").GetString().Should().Be(BaseRouteIds.RecordsUpsert);
        upsert.GetProperty("x-hpd-request-dto-id").GetString().Should().Be(BaseDtoIds.RecordUpsertRequest);
        upsert.GetProperty("x-hpd-response-dto-id").GetString().Should().Be(BaseDtoIds.RecordUpsertResult);
        upsert.GetProperty("x-hpd-required-feature-ids").EnumerateArray()
            .Select(featureId => featureId.GetString())
            .Should().Contain(BaseFeatureIds.RecordsUpsert);

        var responses = create.GetProperty("responses");
        responses.TryGetProperty("201", out var created).Should().BeTrue();
        created.GetProperty("headers").EnumerateObject()
            .Select(header => header.Name)
            .Should().Contain([BaseHttpHeaders.Revision, BaseHttpHeaders.CorrelationId, "Location"]);

        responses.TryGetProperty("400", out var badRequest).Should().BeTrue();
        badRequest.GetProperty("content").TryGetProperty("application/problem+json", out _).Should().BeTrue();
    }

    [Fact]
    public async Task HPDExtensionsCanBeDisabledThroughOpenApiOptions()
    {
        await using var app = await TestBaseApp.CreateAsync(
            configureOpenApi: options => options.AddHPDExtensions = false,
            mapOpenApi: true);
        using var publicDoc = await GetDocument(app, "base-public");

        publicDoc.RootElement.TryGetProperty("x-hpd-document-name", out _).Should().BeFalse();
        Operation(publicDoc, "/base/collections/{collectionId}/records", "post")
            .TryGetProperty("x-hpd-operation-id", out _)
            .Should().BeFalse();
    }

    [Fact]
    public async Task AdminDocumentAddsSecurityOnlyWhenAdminAuthorizationMetadataExists()
    {
        await using var noAuthApp = await TestBaseApp.CreateAsync(mapOpenApi: true);
        using var noAuthDoc = await GetDocument(noAuthApp, "base-admin");
        Operation(noAuthDoc, "/base/admin/manifest", "get").TryGetProperty("security", out _).Should().BeFalse();

        await using var authApp = await TestBaseApp.CreateAsync(
            configureServices: services => services.AddAuthorization(),
            configureEndpoints: options => options.RequireAuthorizationForAdminRoutes = true,
            mapOpenApi: true);
        using var authDoc = await GetDocument(authApp, "base-admin");

        authDoc.RootElement.GetProperty("components").GetProperty("securitySchemes").TryGetProperty("Bearer", out _).Should().BeTrue();
        Operation(authDoc, "/base/admin/manifest", "get").TryGetProperty("security", out var security).Should().BeTrue();
        security.EnumerateArray().Should().NotBeEmpty();
    }

    [Fact]
    public async Task AdminSecurityCanBeDisabledThroughOpenApiOptions()
    {
        await using var app = await TestBaseApp.CreateAsync(
            configureServices: services => services.AddAuthorization(),
            configureEndpoints: options => options.RequireAuthorizationForAdminRoutes = true,
            configureOpenApi: options => options.AddBearerSecurityScheme = false,
            mapOpenApi: true);
        using var adminDoc = await GetDocument(app, "base-admin");

        adminDoc.RootElement.GetProperty("components").TryGetProperty("securitySchemes", out _).Should().BeFalse();
        Operation(adminDoc, "/base/admin/manifest", "get").TryGetProperty("security", out _).Should().BeFalse();
    }

    [Fact]
    public async Task AdminPolicyExplainDocumentsNoStoreWithoutETag()
    {
        await using var app = await TestBaseApp.CreateAsync(
            configureEndpoints: options => options.MapAdminPolicyExplain = true,
            mapOpenApi: true);
        using var adminDoc = await GetDocument(app, "base-admin");

        var responses = Operation(adminDoc, "/base/admin/policy/explain", "post").GetProperty("responses");
        var headers = responses.GetProperty("200").GetProperty("headers").EnumerateObject()
            .Select(header => header.Name)
            .ToArray();

        headers.Should().Contain("Cache-Control");
        headers.Should().Contain(BaseHttpHeaders.CorrelationId);
        headers.Should().NotContain("ETag");
    }

    [Fact]
    public async Task DeferredRoutesAreAbsent()
    {
        await using var app = await TestBaseApp.CreateAsync(
            configureEndpoints: options => options.MapAdminPolicyExplain = true,
            mapOpenApi: true);
        using var publicDoc = await GetDocument(app, "base-public");
        using var adminDoc = await GetDocument(app, "base-admin");

        var allPaths = Paths(publicDoc).Keys.Concat(Paths(adminDoc).Keys).ToArray();
        allPaths.Should().NotContain(path =>
            path.Contains("graphql", StringComparison.OrdinalIgnoreCase)
            || path.Contains("files", StringComparison.OrdinalIgnoreCase)
            || path.Contains("search", StringComparison.OrdinalIgnoreCase)
            || path.Contains("realtime", StringComparison.OrdinalIgnoreCase));
    }

    private static async Task<JsonDocument> GetDocument(WebApplication app, string documentName)
    {
        var json = await app.GetTestClient().GetStringAsync($"/base/openapi/{documentName}.json");
        return JsonDocument.Parse(json);
    }

    private static Dictionary<string, JsonElement> Paths(JsonDocument document) =>
        document.RootElement.GetProperty("paths")
            .EnumerateObject()
            .ToDictionary(property => property.Name, property => property.Value);

    private static JsonElement Operation(JsonDocument document, string path, string method) =>
        document.RootElement.GetProperty("paths").GetProperty(path).GetProperty(method);

    private static Microsoft.AspNetCore.Routing.RouteEndpoint OpenApiEndpoint(WebApplication app, string routePattern) =>
        app.Services.GetRequiredService<Microsoft.AspNetCore.Routing.EndpointDataSource>().Endpoints
            .OfType<Microsoft.AspNetCore.Routing.RouteEndpoint>()
            .Single(endpoint => string.Equals(endpoint.RoutePattern.RawText, routePattern, StringComparison.Ordinal));
}
