using HPD.Base;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Net.Http.Headers;

namespace HPD.Base.AspNetCore;

internal sealed class FileDownloadResponseWriter(ILogger<FileDownloadResponseWriter> logger)
{
    public async Task WriteAsync(
        HttpContext httpContext,
        FileObjectDownloadResult download)
    {
        try
        {
            ApplyFileHeaders(
                httpContext,
                download.Metadata,
                download.ContentLength,
                download.ContentType,
                download.ETag);

            await using (download.ConfigureAwait(false))
            {
                await download.Content.CopyToAsync(
                    httpContext.Response.Body,
                    httpContext.RequestAborted).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (httpContext.RequestAborted.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            HPDBaseFilesAspNetCoreLog.DownloadResponseStreamFailed(
                logger,
                "unexpected",
                "files.response.streamFailed",
                httpContext.Response.HasStarted);
            throw;
        }
    }

    internal static void ApplyFileHeaders(
        HttpContext httpContext,
        FileObjectMetadata metadata,
        long? contentLength,
        string? contentType,
        string? etag)
    {
        httpContext.Response.ContentType = string.IsNullOrWhiteSpace(contentType)
            ? "application/octet-stream"
            : contentType;
        if (contentLength is not null)
            httpContext.Response.ContentLength = contentLength;
        if (!string.IsNullOrWhiteSpace(etag))
            httpContext.Response.Headers.ETag = EntityTagHeaderValue.Parse('"' + etag.Trim('"') + '"').ToString();
        if (metadata.UpdatedAt is not null)
            httpContext.Response.Headers.LastModified = metadata.UpdatedAt.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
        httpContext.Response.Headers.CacheControl = "no-store";
    }
}
