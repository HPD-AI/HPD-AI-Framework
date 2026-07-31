using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace HPD.Base.AspNetCore.Tests.Files.Endpoints;

public sealed class FileRouteTests
{
    [Fact]
    public async Task FilesRoutesAreAbsentUntilFilesApiIsMapped()
    {
        await using var app = await CreateAsync(mapFiles: false);
        var client = app.GetTestClient();

        var response = await client.GetAsync("/base/files/assets/objects/obj_1");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>()
            .Should().NotContain(endpoint => endpoint.RoutePattern.RawText == "/base/files/{bucketId}/objects/{objectId}");
    }

    [Fact]
    public async Task FilesRoutesAppearOnlyWhenMappedAndCarryMetadata()
    {
        await using var app = await CreateAsync(mapFiles: true);

        var endpoints = app.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToArray();
        endpoints.Should().Contain(endpoint => endpoint.RoutePattern.RawText == "/base/files/{bucketId}/objects/{objectId}");
        endpoints.Should().Contain(endpoint => endpoint.Metadata.OfType<IEndpointNameMetadata>().Any(name => name.EndpointName == FileHttpRouteNames.Download));
        endpoints.Should().Contain(endpoint => endpoint.Metadata.OfType<HPDBaseFilesOpenApiMetadata>().Any(metadata => metadata.OperationId == FileHttpRouteNames.MetadataGet));
    }

    [Fact]
    public async Task MappedRoutesReturnProblemDetailsWhenPolicyFailsClosed()
    {
        await using var app = await CreateAsync(mapFiles: true);
        var client = app.GetTestClient();

        var response = await client.GetAsync("/base/files/assets/objects/obj_1/metadata");

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task DescriptorRoutesAppearOnlyAfterFilesApiIsMappedAndRegistryRebuilt()
    {
        await using var unmapped = await CreateAsync(mapFiles: false);
        await unmapped.Services.GetRequiredService<HPD.Base.IBaseDescriptorRegistry>().RebuildAsync();
        unmapped.Services.GetRequiredService<HPD.Base.IBaseDescriptorRegistry>().Current.Manifest.Projections
            .Should().NotContain(projection => projection.Id == FileModuleIds.AspNetCoreModule);

        await using var mapped = await CreateAsync(mapFiles: true);
        await mapped.Services.GetRequiredService<HPD.Base.IBaseDescriptorRegistry>().RebuildAsync();
        var projection = mapped.Services.GetRequiredService<HPD.Base.IBaseDescriptorRegistry>().Current.Manifest.Projections
            .Should().ContainSingle(candidate => candidate.Id == FileModuleIds.AspNetCoreModule)
            .Subject;
        projection.Routes.Should().Contain(route => route.OperationId == FileHttpRouteNames.Download);
    }

    [Fact]
    public async Task InMemoryProviderEnablesUploadMetadataDownloadListAndDeleteRoutes()
    {
        await using var app = await CreateAsync(mapFiles: true, useInMemory: true);
        var client = app.GetTestClient();

        using var uploadContent = new StringContent("hello files", Encoding.UTF8, "text/plain");
        uploadContent.Headers.Add(FileHttpHeaders.ObjectKey, "docs/hello.txt");
        uploadContent.Headers.Add(FileHttpHeaders.ObjectName, "hello.txt");

        var upload = await client.PostAsync("/base/files/assets/objects", uploadContent);
        upload.StatusCode.Should().Be(HttpStatusCode.Created);
        upload.Headers.Location.Should().NotBeNull();

        var jsonOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;
        var uploadResult = await upload.Content.ReadFromJsonAsync<FileObjectUploadResult>(jsonOptions);
        uploadResult.Should().NotBeNull();
        var objectId = uploadResult!.Metadata.ObjectId.Value;

        var metadataResponse = await client.GetAsync($"/base/files/assets/objects/{objectId}/metadata");
        metadataResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var metadata = await metadataResponse.Content.ReadFromJsonAsync<FileObjectMetadata>(jsonOptions);
        metadata!.ContentType.Should().StartWith("text/plain");

        var download = await client.GetAsync($"/base/files/assets/objects/{objectId}");
        download.StatusCode.Should().Be(HttpStatusCode.OK);
        download.Content.Headers.ContentType?.MediaType.Should().Be("text/plain");
        download.Content.Headers.ContentLength.Should().Be(11);
        download.Headers.ETag.Should().NotBeNull();
        download.Headers.CacheControl?.NoStore.Should().BeTrue();
        (await download.Content.ReadAsStringAsync()).Should().Be("hello files");

        var list = await client.GetAsync("/base/files/assets/objects?prefix=docs");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await list.Content.ReadFromJsonAsync<FileObjectListResult>(jsonOptions);
        page!.Items.Should().Contain(item => item.ObjectId.Value == objectId);

        var delete = await client.DeleteAsync($"/base/files/assets/objects/{objectId}");
        delete.StatusCode.Should().Be(HttpStatusCode.NoContent);
    }

    [Fact]
    public async Task ListRouteBindsLimitAndCustomPrefixFlowsToLocationAndDescriptors()
    {
        await using var app = await CreateAsync(mapFiles: true, useInMemory: true, filesRoutePrefix: "/custom/files");
        var client = app.GetTestClient();
        var jsonOptions = app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<Microsoft.AspNetCore.Http.Json.JsonOptions>>().Value.SerializerOptions;

        using var firstContent = new StringContent("first", Encoding.UTF8, "text/plain");
        firstContent.Headers.Add(FileHttpHeaders.ObjectKey, "docs/first.txt");
        using var secondContent = new StringContent("second", Encoding.UTF8, "text/plain");
        secondContent.Headers.Add(FileHttpHeaders.ObjectKey, "docs/second.txt");

        var firstUpload = await client.PostAsync("/custom/files/assets/objects", firstContent);
        var secondUpload = await client.PostAsync("/custom/files/assets/objects", secondContent);

        firstUpload.StatusCode.Should().Be(HttpStatusCode.Created);
        firstUpload.Headers.Location?.ToString().Should().StartWith("/custom/files/assets/objects/");
        secondUpload.StatusCode.Should().Be(HttpStatusCode.Created);

        var list = await client.GetAsync("/custom/files/assets/objects?prefix=docs&limit=1&cursor=ignored");
        list.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await list.Content.ReadFromJsonAsync<FileObjectListResult>(jsonOptions);
        page!.Items.Should().HaveCount(1);

        await app.Services.GetRequiredService<HPD.Base.IBaseDescriptorRegistry>().RebuildAsync();
        var projection = app.Services.GetRequiredService<HPD.Base.IBaseDescriptorRegistry>().Current.Manifest.Projections
            .Should().ContainSingle(candidate => candidate.Id == FileModuleIds.AspNetCoreModule)
            .Subject;
        projection.Routes.Should().Contain(route => route.OperationId == FileHttpRouteNames.Upload && route.Path == "/custom/files/{bucketId}/objects");
    }

    [Fact]
    public async Task MappedRoutesReturnProblemDetailsForUploadValidationFailures()
    {
        await using var app = await CreateAsync(mapFiles: true, useInMemory: true);
        var client = app.GetTestClient();

        using var uploadContent = new StringContent("hello files", Encoding.UTF8, "text/plain");
        var upload = await client.PostAsync("/base/files/assets/objects", uploadContent);

        upload.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        upload.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
    }

    [Fact]
    public async Task GeneratedOpenApiDocumentIncludesMappedFileRoutes()
    {
        await using var app = await CreateAsync(mapFiles: true, mapOpenApi: true);
        var client = app.GetTestClient();

        using var response = await client.GetAsync("/base/openapi/base-public.json");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var paths = document.RootElement.GetProperty("paths");
        var pathNames = paths.EnumerateObject().Select(path => path.Name).ToArray();
        paths.TryGetProperty("/base/files/{bucketId}/objects", out var collectionPath).Should().BeTrue("document paths were {0}", string.Join(", ", pathNames));
        collectionPath.GetProperty("post").GetProperty("operationId").GetString().Should().Be(FileHttpRouteNames.Upload);
        collectionPath.GetProperty("post").GetProperty("parameters").EnumerateArray()
            .Should().Contain(parameter => parameter.GetProperty("name").GetString() == FileHttpHeaders.ObjectKey);
        collectionPath.GetProperty("get").GetProperty("parameters").EnumerateArray()
            .Should().Contain(parameter => parameter.GetProperty("name").GetString() == "limit");
        paths.TryGetProperty("/base/files/{bucketId}/objects/{objectId}", out var objectPath).Should().BeTrue();
        objectPath.GetProperty("get").GetProperty("operationId").GetString().Should().Be(FileHttpRouteNames.Download);
        objectPath.GetProperty("delete").GetProperty("responses").TryGetProperty("204", out _).Should().BeTrue();
    }

    private static async Task<WebApplication> CreateAsync(bool mapFiles, bool useInMemory = false, bool mapOpenApi = false, string filesRoutePrefix = "/base/files")
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Services.AddHPDBaseRuntime();
        builder.Services.AddHPDBaseAspNetCore();
        if (mapOpenApi)
            HPD.Base.AspNetCore.HPDBaseOpenApiServiceCollectionExtensions.AddHPDBaseOpenApi(builder.Services);
        builder.Services.AddHPDBaseFiles(options =>
        {
            options.Buckets.Add(new FileBucketDescriptor
            {
                BucketId = new FileBucketId("assets"),
                DisplayName = "Assets",
                ProviderRef = new FileProviderRef(useInMemory ? "volatile" : "none")
            });
        });
        builder.Services.AddHPDBaseFilesAspNetCore();
        if (useInMemory)
        {
            builder.Services.AddSingleton<IFilePolicyOrchestrator, AllowFilePolicy>();
            builder.Services.AddHPDBaseFilesVolatileProvider();
        }

        var app = builder.Build();
        app.MapHPDBaseApi();
        if (mapFiles)
            app.MapHPDBaseFilesApi(filesRoutePrefix);
        if (mapOpenApi)
            HPD.Base.AspNetCore.HPDBaseOpenApiEndpointRouteBuilderExtensions.MapHPDBaseOpenApi(app);

        await app.StartAsync();
        return app;
    }

    private sealed class AllowFilePolicy : IFilePolicyOrchestrator
    {
        public ValueTask<OperationResult<FilePolicyEvaluation>> EvaluateAsync(FilePolicyRequest request, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(new OperationResult<FilePolicyEvaluation>
            {
                Status = OperationStatus.Ok,
                Value = new FilePolicyEvaluation { Allowed = true }
            });
    }
}
