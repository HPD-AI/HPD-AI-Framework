using HPD.Agent.Hosting.Data;
using HPD.Agent.Hosting.Lifecycle;
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
    internal static void Map(IEndpointRouteBuilder endpoints, IAgentAssetService assets)
    {
        // POST /sessions/{sid}/assets - Upload asset (multipart/form-data)
        endpoints.MapPost("/sessions/{sid}/assets", (string sid, HttpRequest request, CancellationToken ct) =>
                UploadAsset(sid, request, assets, ct))
            .WithName("UploadAsset")
            .WithSummary("Upload an asset (multipart/form-data)")
            .DisableAntiforgery(); // Allow multipart uploads

        // GET /sessions/{sid}/assets - List assets for session
        endpoints.MapGet("/sessions/{sid}/assets", (string sid, CancellationToken ct) =>
                ListAssets(sid, assets, ct))
            .WithName("ListAssets")
            .WithSummary("List all assets in a session");

        // GET /sessions/{sid}/assets/{assetId} - Download asset (returns binary)
        endpoints.MapGet("/sessions/{sid}/assets/{assetId}", (string sid, string assetId, CancellationToken ct) =>
                DownloadAsset(sid, assetId, assets, ct))
            .WithName("DownloadAsset")
            .WithSummary("Download an asset (returns binary content)");

        // DELETE /sessions/{sid}/assets/{assetId} - Delete asset
        endpoints.MapDelete("/sessions/{sid}/assets/{assetId}", (string sid, string assetId, CancellationToken ct) =>
                DeleteAsset(sid, assetId, assets, ct))
            .WithName("DeleteAsset")
            .WithSummary("Delete an asset");
    }

    private static async Task<Results<Created<AssetDto>, NotFound, ValidationProblem>> UploadAsset(
        string sid,
        HttpRequest request,
        IAgentAssetService assets,
        CancellationToken ct = default)
    {
        try
        {
            if (!request.HasFormContentType)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["InvalidContentType"] = ["Request must be multipart/form-data."]
                });
            }

            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("file");

            if (file == null || file.Length == 0)
            {
                return TypedResults.ValidationProblem(new Dictionary<string, string[]>
                {
                    ["NoFileProvided"] = ["No file was provided in the 'file' field."]
                });
            }

            await using var stream = file.OpenReadStream();
            var result = await assets.UploadAssetAsync(sid, stream, file.FileName, file.ContentType, ct);
            return result.Status switch
            {
                AgentServiceStatus.Success => TypedResults.Created(
                    $"/sessions/{sid}/assets/{result.Value!.AssetId}",
                    result.Value),
                AgentServiceStatus.NotFound => TypedResults.NotFound(),
                _ => TypedResults.ValidationProblem(ToValidation(result))
            };
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["UploadAssetError"] = [ex.Message]
            });
        }
    }

    private static async Task<Results<Ok<List<AssetDto>>, NotFound, ValidationProblem>> ListAssets(
        string sid,
        IAgentAssetService assets,
        CancellationToken ct = default)
    {
        try
        {
            var result = await assets.ListAssetsAsync(sid, ct);
            return result.Status == AgentServiceStatus.NotFound
                ? TypedResults.NotFound()
                : TypedResults.Ok(result.Value!.ToList());
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["ListAssetsError"] = [ex.Message]
            });
        }
    }

    private static async Task<Results<FileContentHttpResult, NotFound, ValidationProblem>> DownloadAsset(
        string sid,
        string assetId,
        IAgentAssetService assets,
        CancellationToken ct = default)
    {
        try
        {
            var result = await assets.DownloadAssetAsync(sid, assetId, ct);
            if (result.Status == AgentServiceStatus.NotFound)
                return TypedResults.NotFound();

            return TypedResults.File(result.Value!.Data, result.Value.ContentType, result.Value.FileName);
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["DownloadAssetError"] = [ex.Message]
            });
        }
    }

    private static async Task<Results<NoContent, NotFound, ValidationProblem>> DeleteAsset(
        string sid,
        string assetId,
        IAgentAssetService assets,
        CancellationToken ct = default)
    {
        try
        {
            var result = await assets.DeleteAssetAsync(sid, assetId, ct);
            return result.Status == AgentServiceStatus.NotFound
                ? TypedResults.NotFound()
                : TypedResults.NoContent();
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["DeleteAssetError"] = [ex.Message]
            });
        }
    }

    private static Dictionary<string, string[]> ToValidation<T>(AgentServiceResult<T> result) =>
        new()
        {
            [result.ErrorCode ?? "AssetError"] = [result.ErrorMessage ?? "Asset operation failed."]
        };
}
