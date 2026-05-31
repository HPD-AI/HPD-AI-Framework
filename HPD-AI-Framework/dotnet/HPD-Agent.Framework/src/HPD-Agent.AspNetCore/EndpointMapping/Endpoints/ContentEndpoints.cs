using HPD.Agent.Hosting.Data;
using HPD.Agent.Hosting.Lifecycle;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Routing;

namespace HPD.Agent.AspNetCore.EndpointMapping.Endpoints;

/// <summary>
/// Content management endpoints for the HPD-Agent API.
/// Content items are session-scoped and shared across all branches.
/// </summary>
internal static class ContentEndpoints
{
    /// <summary>
    /// Maps all content-related endpoints.
    /// </summary>
    internal static void Map(IEndpointRouteBuilder endpoints, IAgentContentService content)
    {
        // POST /sessions/{sid}/content - Upload content (multipart/form-data)
        endpoints.MapPost("/sessions/{sid}/content", (string sid, HttpRequest request, CancellationToken ct) =>
                UploadContent(sid, request, content, ct))
            .WithName("UploadContent")
            .WithSummary("Upload content (multipart/form-data)")
            .DisableAntiforgery(); // Allow multipart uploads

        // GET /sessions/{sid}/content - List content for session
        endpoints.MapGet("/sessions/{sid}/content", (string sid, CancellationToken ct) =>
                ListContent(sid, content, ct))
            .WithName("ListContent")
            .WithSummary("List all content in a session");

        // GET /sessions/{sid}/content/{contentId} - Download content (returns binary)
        endpoints.MapGet("/sessions/{sid}/content/{contentId}", (string sid, string contentId, CancellationToken ct) =>
                DownloadContent(sid, contentId, content, ct))
            .WithName("DownloadContent")
            .WithSummary("Download content (returns binary content)");

        // DELETE /sessions/{sid}/content/{contentId} - Delete content
        endpoints.MapDelete("/sessions/{sid}/content/{contentId}", (string sid, string contentId, CancellationToken ct) =>
                DeleteContent(sid, contentId, content, ct))
            .WithName("DeleteContent")
            .WithSummary("Delete content");
    }

    private static async Task<Results<Created<ContentDto>, NotFound, ValidationProblem>> UploadContent(
        string sid,
        HttpRequest request,
        IAgentContentService content,
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
            var result = await content.UploadContentAsync(sid, stream, file.FileName, file.ContentType, ct);
            return result.Status switch
            {
                AgentServiceStatus.Success => TypedResults.Created(
                    $"/sessions/{sid}/content/{result.Value!.ContentId}",
                    result.Value),
                AgentServiceStatus.NotFound => TypedResults.NotFound(),
                _ => TypedResults.ValidationProblem(ToValidation(result))
            };
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["UploadContentError"] = [ex.Message]
            });
        }
    }

    private static async Task<Results<Ok<List<ContentDto>>, NotFound, ValidationProblem>> ListContent(
        string sid,
        IAgentContentService content,
        CancellationToken ct = default)
    {
        try
        {
            var result = await content.ListContentAsync(sid, ct);
            return result.Status == AgentServiceStatus.NotFound
                ? TypedResults.NotFound()
                : TypedResults.Ok(result.Value!.ToList());
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["ListContentError"] = [ex.Message]
            });
        }
    }

    private static async Task<Results<FileContentHttpResult, NotFound, ValidationProblem>> DownloadContent(
        string sid,
        string contentId,
        IAgentContentService content,
        CancellationToken ct = default)
    {
        try
        {
            var result = await content.DownloadContentAsync(sid, contentId, ct);
            if (result.Status == AgentServiceStatus.NotFound)
                return TypedResults.NotFound();

            return TypedResults.File(result.Value!.Data, result.Value.ContentType, result.Value.FileName);
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["DownloadContentError"] = [ex.Message]
            });
        }
    }

    private static async Task<Results<NoContent, NotFound, ValidationProblem>> DeleteContent(
        string sid,
        string contentId,
        IAgentContentService content,
        CancellationToken ct = default)
    {
        try
        {
            var result = await content.DeleteContentAsync(sid, contentId, ct);
            return result.Status == AgentServiceStatus.NotFound
                ? TypedResults.NotFound()
                : TypedResults.NoContent();
        }
        catch (Exception ex)
        {
            return TypedResults.ValidationProblem(new Dictionary<string, string[]>
            {
                ["DeleteContentError"] = [ex.Message]
            });
        }
    }

    private static Dictionary<string, string[]> ToValidation<T>(AgentServiceResult<T> result) =>
        new()
        {
            [result.ErrorCode ?? "ContentError"] = [result.ErrorMessage ?? "Content operation failed."]
        };
}
