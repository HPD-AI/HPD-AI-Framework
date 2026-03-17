using HPD.Agent;
using HPD.Agent.AspNetCore.Lifecycle;
using HPD.Agent.Hosting.Data;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace HPD.Agent.AspNetCore.EndpointMapping.Endpoints;

/// <summary>
/// Asset management endpoints for the HPD-Agent API.
/// Assets are session-scoped and shared across all branches.
/// </summary>
internal static class AssetEndpoints
{
    /// <summary>
    /// Maps all asset-related endpoints.
    /// </summary>
    internal static void Map(IEndpointRouteBuilder endpoints, AspNetCoreSessionManager manager)
    {
        // POST /sessions/{sid}/assets - Upload asset (multipart/form-data)
        endpoints.MapPost("/sessions/{sid}/assets", (string sid, HttpRequest request, CancellationToken ct) =>
                UploadAsset(sid, request, manager, ct))
            .WithName("UploadAsset")
            .WithSummary("Upload an asset (multipart/form-data)")
            .DisableAntiforgery(); // Allow multipart uploads

        // GET /sessions/{sid}/assets - List assets for session
        endpoints.MapGet("/sessions/{sid}/assets", (string sid, CancellationToken ct) =>
                ListAssets(sid, manager, ct))
            .WithName("ListAssets")
            .WithSummary("List all assets in a session");

        // GET /sessions/{sid}/assets/{assetId} - Download asset (returns binary)
        endpoints.MapGet("/sessions/{sid}/assets/{assetId}", (string sid, string assetId, CancellationToken ct) =>
                DownloadAsset(sid, assetId, manager, ct))
            .WithName("DownloadAsset")
            .WithSummary("Download an asset (returns binary content)");

        // DELETE /sessions/{sid}/assets/{assetId} - Delete asset
        endpoints.MapDelete("/sessions/{sid}/assets/{assetId}", (string sid, string assetId, CancellationToken ct) =>
                DeleteAsset(sid, assetId, manager, ct))
            .WithName("DeleteAsset")
            .WithSummary("Delete an asset");
    }

    private static async Task<IResult> UploadAsset(
        string sid,
        HttpRequest request,
        AspNetCoreSessionManager manager,
        CancellationToken ct = default)
    {
        try
        {
            var session = await manager.Store.LoadSessionAsync(sid, ct);
            if (session == null)
            {
                return ErrorResponses.NotFound();
            }

            var contentStore = manager.Store.GetContentStore(sid);
            if (contentStore == null)
            {
                return ErrorResponses.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["AssetStoreNotAvailable"] = ["Content storage is not available for this session store."]
                });
            }

            if (!request.HasFormContentType)
            {
                return ErrorResponses.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["InvalidContentType"] = ["Request must be multipart/form-data."]
                });
            }

            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");

            if (file == null || file.Length == 0)
            {
                return ErrorResponses.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["NoFileProvided"] = ["No file was provided in the 'file' field."]
                });
            }

            using var stream = file.OpenReadStream();
            using var memoryStream = new MemoryStream();
            await stream.CopyToAsync(memoryStream, ct);

            var assetData = memoryStream.ToArray();
            var contentType = file.ContentType ?? "application/octet-stream";

            // Upload to content store with session scope and /uploads folder tag
            var assetId = await contentStore.PutAsync(
                scope: sid,
                data: assetData,
                contentType: contentType,
                metadata: new ContentMetadata
                {
                    Name = file.FileName,
                    Origin = ContentSource.User,
                    Tags = new Dictionary<string, string>
                    {
                        ["folder"] = "/uploads",
                        ["session"] = sid
                    }
                },
                cancellationToken: ct);

            // Get metadata from the store
            var content = await contentStore.GetAsync(sid, assetId, ct);
            if (content == null)
            {
                return ErrorResponses.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["UploadFailed"] = ["Asset was uploaded but could not be retrieved."]
                });
            }

            var dto = new AssetDto(
                assetId,
                content.ContentType,
                content.Data.Length,
                content.Info.CreatedAt.ToString("O"));

            return ErrorResponses.Created($"/sessions/{sid}/assets/{assetId}", dto);
        }
        catch (Exception ex)
        {
            return ErrorResponses.ValidationProblem(new Dictionary<string, string[]>
            {
                ["UploadAssetError"] = [ex.Message]
            });
        }
    }

    private static async Task<IResult> ListAssets(
        string sid,
        AspNetCoreSessionManager manager,
        CancellationToken ct = default)
    {
        try
        {
            var session = await manager.Store.LoadSessionAsync(sid, ct);
            if (session == null)
            {
                return ErrorResponses.NotFound();
            }

            var contentStore = manager.Store.GetContentStore(sid);
            if (contentStore == null)
            {
                return ErrorResponses.Json(new List<AssetDto>());
            }

            // Query /uploads folder within session scope
            var assets = await contentStore.QueryAsync(
                scope: sid,
                query: new ContentQuery { Tags = new Dictionary<string, string> { ["folder"] = "/uploads" } },
                cancellationToken: ct);
            var dtos = assets.Select(a => new AssetDto(
                a.Id,
                a.ContentType,
                a.SizeBytes,
                a.CreatedAt.ToString("O"))).ToList();

            return ErrorResponses.Json(dtos);
        }
        catch (Exception ex)
        {
            return ErrorResponses.ValidationProblem(new Dictionary<string, string[]>
            {
                ["ListAssetsError"] = [ex.Message]
            });
        }
    }

    private static async Task<IResult> DownloadAsset(
        string sid,
        string assetId,
        AspNetCoreSessionManager manager,
        CancellationToken ct = default)
    {
        try
        {
            var session = await manager.Store.LoadSessionAsync(sid, ct);
            if (session == null)
            {
                return ErrorResponses.NotFound();
            }

            var contentStore = manager.Store.GetContentStore(sid);
            if (contentStore == null)
            {
                return ErrorResponses.NotFound();
            }

            var content = await contentStore.GetAsync(sid, assetId, ct);
            if (content == null)
            {
                return ErrorResponses.NotFound();
            }

            // Include filename in Content-Disposition header for proper download handling
            return Results.File(content.Data, content.ContentType, content.Info.Name);
        }
        catch (Exception ex)
        {
            return ErrorResponses.ValidationProblem(new Dictionary<string, string[]>
            {
                ["DownloadAssetError"] = [ex.Message]
            });
        }
    }

    private static async Task<IResult> DeleteAsset(
        string sid,
        string assetId,
        AspNetCoreSessionManager manager,
        CancellationToken ct = default)
    {
        try
        {
            var session = await manager.Store.LoadSessionAsync(sid, ct);
            if (session == null)
            {
                return ErrorResponses.NotFound();
            }

            var contentStore = manager.Store.GetContentStore(sid);
            if (contentStore == null)
            {
                return ErrorResponses.NotFound();
            }

            // Check if asset exists before deleting
            var content = await contentStore.GetAsync(sid, assetId, ct);
            if (content == null)
            {
                return ErrorResponses.NotFound();
            }

            await contentStore.DeleteAsync(sid, assetId, ct);

            return ErrorResponses.NoContent();
        }
        catch (Exception ex)
        {
            return ErrorResponses.ValidationProblem(new Dictionary<string, string[]>
            {
                ["DeleteAssetError"] = [ex.Message]
            });
        }
    }
}
