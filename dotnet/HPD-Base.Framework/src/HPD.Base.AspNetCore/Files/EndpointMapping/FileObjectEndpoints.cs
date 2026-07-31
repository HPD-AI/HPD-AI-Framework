using HPD.Base.AspNetCore;
using HPD.Base;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base.AspNetCore;

internal static class FileObjectEndpoints
{
    public static void Map(RouteGroupBuilder group)
    {
        group.MapPost("/{bucketId}/objects", (RequestDelegate)Upload)
            .WithHPDBaseFilesOpenApi(FileHttpRouteNames.Upload);
        group.MapGet("/{bucketId}/objects", (RequestDelegate)List)
            .WithHPDBaseFilesOpenApi(FileHttpRouteNames.List);
        group.MapGet("/{bucketId}/objects/{objectId}", (RequestDelegate)Download)
            .WithHPDBaseFilesOpenApi(FileHttpRouteNames.Download);
        group.MapMethods("/{bucketId}/objects/{objectId}", [HttpMethods.Head], (RequestDelegate)Head)
            .WithHPDBaseFilesOpenApi(FileHttpRouteNames.Head);
        group.MapGet("/{bucketId}/objects/{objectId}/metadata", (RequestDelegate)Metadata)
            .WithHPDBaseFilesOpenApi(FileHttpRouteNames.MetadataGet);
        group.MapDelete("/{bucketId}/objects/{objectId}", (RequestDelegate)Delete)
            .WithHPDBaseFilesOpenApi(FileHttpRouteNames.Delete);
    }

    private static async Task Upload(HttpContext httpContext)
    {
        var service = httpContext.RequestServices.GetRequiredService<IFileObjectService>();
        var mapper = httpContext.RequestServices.GetRequiredService<IBaseHttpResultMapper>();
        var request = new FileObjectUploadRequest
        {
            BucketId = new FileBucketId(RouteValue(httpContext, "bucketId")),
            Key = Header(httpContext, FileHttpHeaders.ObjectKey) is { Length: > 0 } key ? new FileObjectKey(key) : null,
            Name = Header(httpContext, FileHttpHeaders.ObjectName),
            ContentType = httpContext.Request.ContentType,
            SizeBytes = httpContext.Request.ContentLength,
            Checksum = Header(httpContext, FileHttpHeaders.Checksum) is { Length: > 0 } checksum ? new FileObjectChecksum(checksum) : null,
            Content = httpContext.Request.Body
        };

        var result = await service.UploadAsync(request, Context(httpContext), httpContext.RequestAborted);
        var location = result.Value?.Metadata is { } metadata
            ? $"{httpContext.Request.Path}/{Uri.EscapeDataString(metadata.ObjectId.Value)}"
            : null;
        await mapper.ToHttpResult(result, httpContext, new HPDBaseHttpResultMappingContext { Location = location }).ExecuteAsync(httpContext);
    }

    private static async Task List(HttpContext httpContext)
    {
        var service = httpContext.RequestServices.GetRequiredService<IFileObjectService>();
        var mapper = httpContext.RequestServices.GetRequiredService<IBaseHttpResultMapper>();
        var prefix = httpContext.Request.Query.TryGetValue("prefix", out var values) ? values.ToString() : null;
        var limit = TryBindInt(httpContext, "limit");
        var cursor = httpContext.Request.Query.TryGetValue("cursor", out var cursorValues) ? cursorValues.ToString() : null;
        var result = await service.ListMetadataAsync(new FileObjectListRequest
        {
            BucketId = new FileBucketId(RouteValue(httpContext, "bucketId")),
            Prefix = string.IsNullOrWhiteSpace(prefix) ? null : new FileObjectKey(prefix),
            Limit = limit,
            Cursor = string.IsNullOrWhiteSpace(cursor) ? null : cursor
        }, Context(httpContext), httpContext.RequestAborted);

        await mapper.ToHttpResult(result, httpContext, new HPDBaseHttpResultMappingContext()).ExecuteAsync(httpContext);
    }

    private static async Task Download(HttpContext httpContext)
    {
        var service = httpContext.RequestServices.GetRequiredService<IFileObjectService>();
        var mapper = httpContext.RequestServices.GetRequiredService<IBaseHttpResultMapper>();
        var responseWriter = httpContext.RequestServices.GetRequiredService<FileDownloadResponseWriter>();
        var result = await service.OpenDownloadAsync(DownloadRequest(httpContext), Context(httpContext), httpContext.RequestAborted);
        if (!result.IsSuccess() || result.Value is null)
        {
            await mapper.ToHttpResult(result, httpContext, new HPDBaseHttpResultMappingContext()).ExecuteAsync(httpContext);
            return;
        }

        await responseWriter.WriteAsync(httpContext, result.Value);
    }

    private static async Task Head(HttpContext httpContext)
    {
        var service = httpContext.RequestServices.GetRequiredService<IFileObjectService>();
        var mapper = httpContext.RequestServices.GetRequiredService<IBaseHttpResultMapper>();
        var result = await service.GetMetadataAsync(MetadataRequest(httpContext), Context(httpContext), httpContext.RequestAborted);
        if (!result.IsSuccess() || result.Value is null)
        {
            await mapper.ToHttpResult(result, httpContext, new HPDBaseHttpResultMappingContext()).ExecuteAsync(httpContext);
            return;
        }

        FileDownloadResponseWriter.ApplyFileHeaders(
            httpContext,
            result.Value,
            result.Value.SizeBytes,
            result.Value.ContentType,
            result.Value.Revision?.Value);
        httpContext.Response.StatusCode = StatusCodes.Status204NoContent;
    }

    private static async Task Metadata(HttpContext httpContext)
    {
        var service = httpContext.RequestServices.GetRequiredService<IFileObjectService>();
        var mapper = httpContext.RequestServices.GetRequiredService<IBaseHttpResultMapper>();
        var result = await service.GetMetadataAsync(MetadataRequest(httpContext), Context(httpContext), httpContext.RequestAborted);
        await mapper.ToHttpResult(result, httpContext, new HPDBaseHttpResultMappingContext()).ExecuteAsync(httpContext);
    }

    private static async Task Delete(HttpContext httpContext)
    {
        var service = httpContext.RequestServices.GetRequiredService<IFileObjectService>();
        var mapper = httpContext.RequestServices.GetRequiredService<IBaseHttpResultMapper>();
        var result = await service.DeleteAsync(new FileObjectDeleteRequest
        {
            BucketId = new FileBucketId(RouteValue(httpContext, "bucketId")),
            ObjectId = new FileObjectId(RouteValue(httpContext, "objectId"))
        }, Context(httpContext), httpContext.RequestAborted);
        await mapper.ToHttpResult(result, httpContext, new HPDBaseHttpResultMappingContext()).ExecuteAsync(httpContext);
    }

    private static FileObjectDownloadRequest DownloadRequest(HttpContext httpContext) => new()
    {
        BucketId = new FileBucketId(RouteValue(httpContext, "bucketId")),
        ObjectId = new FileObjectId(RouteValue(httpContext, "objectId"))
    };

    private static FileObjectMetadataRequest MetadataRequest(HttpContext httpContext) => new()
    {
        BucketId = new FileBucketId(RouteValue(httpContext, "bucketId")),
        ObjectId = new FileObjectId(RouteValue(httpContext, "objectId"))
    };

    private static FileOperationContext Context(HttpContext httpContext) => new()
    {
        SubjectId = httpContext.User.Identity?.Name,
        CorrelationId = httpContext.TraceIdentifier
    };

    private static string RouteValue(HttpContext httpContext, string key) => Convert.ToString(httpContext.Request.RouteValues[key], System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

    private static string? Header(HttpContext httpContext, string key) =>
        httpContext.Request.Headers.TryGetValue(key, out var value) ? value.ToString() : null;

    private static int? TryBindInt(HttpContext httpContext, string key) =>
        httpContext.Request.Query.TryGetValue(key, out var value)
        && int.TryParse(value.ToString(), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : null;
}
