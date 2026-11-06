using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HPD.Agent.Middleware;

namespace HPD.Agent;

internal static class ContentRuntime
{
    internal static async ValueTask<NormalizedToolResult> ProjectAsync(
        NormalizedToolResult normalized,
        HPDAIFunctionFactory.HPDAIFunction? function,
        FunctionExecutionPreparation preparation,
        AgentRunConfig runConfig,
        AgentContext context,
        CancellationToken cancellationToken)
    {
        var policy = runConfig.ToolOutputs?.Resolve(context.Config?.ToolOutputs ?? new())
            ?? context.Config?.ToolOutputs
            ?? new ToolOutputConfig();
        var plan = function?.HPDOptions.ContentProjectionPlan;
        if (!policy.Enabled || plan is not { HasOffloads: true })
            return EnforceInlineBudget(normalized, policy);

        try
        {
            if (normalized.Parts.Count != 1 || normalized.Parts[0] is not NormalizedJsonPart jsonPart)
                throw new ToolResultNormalizationException(
                    "tool_output_projection_failed",
                    "Native offloading requires one canonical JSON result.");

            var root = JsonNode.Parse(jsonPart.Json.GetRawText())
                ?? throw new ToolResultNormalizationException(
                    "tool_output_projection_failed",
                    "Native tool result could not be represented as JSON.");
            var captured = new JsonObject();
            var parts = new List<ContentPartReference>();
            foreach (var path in plan.Paths)
            {
                if (!TryGetNode(root, path.Pointer, out var value) || value is null)
                    continue;
                var clone = value.DeepClone();
                captured[path.Name] = clone;
                var bytes = Encoding.UTF8.GetBytes(clone.ToJsonString());
                parts.Add(new ContentPartReference
                {
                    Name = path.Name,
                    Digest = Digest(bytes),
                    MediaType = "application/json",
                    SizeBytes = bytes.Length,
                    Sensitivity = path.Sensitivity
                });
            }

            if (parts.Count == 0)
                return EnforceInlineBudget(normalized, policy);
            var sessionId = context.Session?.Id
                ?? throw new ToolResultNormalizationException("content_store_unavailable", "Tool output offloading requires an active session.");
            var threadId = context.Thread?.Id
                ?? throw new ToolResultNormalizationException("content_store_unavailable", "Tool output offloading requires an active thread.");
            var store = context.ContentStore
                ?? throw new ToolResultNormalizationException("content_store_unavailable", "No content store is configured.");
            var bundle = new JsonObject { ["parts"] = captured };
            var bundleBytes = Encoding.UTF8.GetBytes(bundle.ToJsonString());
            var identity = string.Join('|', sessionId, threadId, preparation.FunctionCall.CallId,
                preparation.Function?.Name, Digest(bundleBytes));
            var stableName = "tool-output-" + Hash(Encoding.UTF8.GetBytes(identity));
            var scope = ContentScope.Create(sessionId);
            var existing = (await store.QueryAsync(scope, new ContentQuery
                {
                    Name = stableName,
                    Tags = new Dictionary<string, string> { ["kind"] = "tool-output" }
                }, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
            var info = existing ?? await store.WriteBytesAsync(
                scope,
                bundleBytes,
                new ContentMetadata
                {
                    ContentType = "application/json",
                    Name = stableName,
                    Origin = ContentSource.Agent,
                    Description = "Native tool output bundle",
                    Tags = new Dictionary<string, string>
                    {
                        ["kind"] = "tool-output",
                        ["thread-id"] = threadId,
                        ["function"] = preparation.Function?.Name ?? "unknown",
                        ["call-id"] = preparation.FunctionCall.CallId,
                        ["sensitivity"] = (parts.Count == 0
                            ? ContentSensitivity.Internal
                            : parts.Max(static part => part.Sensitivity)).ToString()
                    }
                },
                new ContentWriteOptions { Mode = ContentWriteMode.Create },
                cancellationToken).ConfigureAwait(false);
            var sensitivity = parts.Count == 0
                ? ContentSensitivity.Internal
                : parts.Max(static part => part.Sensitivity);
            var reference = new ContentReference
            {
                ContentId = info.Address.ContentId,
                Version = info.Address.Version,
                Digest = info.Address.Sha256
                    ?? throw new ToolResultNormalizationException("content_write_failed", "The content store did not return a digest."),
                MediaType = info.ContentType,
                SizeBytes = info.SizeBytes,
                Disposition = sensitivity == ContentSensitivity.Restricted
                    ? ContentDisposition.InternalOnly
                    : ContentDisposition.ModelRetrievable,
                Lifetime = store.PersistenceCapability == ContentStorePersistenceCapability.RestartDurable
                    ? ContentLifetime.SessionLifetimeAcrossRestart
                    : ContentLifetime.Ephemeral,
                Sensitivity = sensitivity,
                Source = new ContentSourceInfo
                {
                    SessionId = sessionId,
                    ThreadId = threadId,
                    CallId = preparation.FunctionCall.CallId,
                    FunctionName = preparation.Function?.Name,
                    PartName = null
                },
                Parts = parts
            };
            var referenceNode = JsonSerializer.SerializeToNode(reference, HPDJsonContext.Default.ContentReference);
            foreach (var path in plan.Paths)
            {
                if (parts.Any(part => part.Name == path.Name) &&
                    TryGetNode(root, path.Pointer, out var current) &&
                    current is not null)
                {
                    SetNode(root, path.Pointer, referenceNode?.DeepClone());
                }
            }
            return EnforceInlineBudget(
                new NormalizedToolResult([new NormalizedJsonPart(
                    JsonSerializer.SerializeToElement(root, HPDJsonContext.Default.JsonNode))],
                    normalized.DeclaredResultType),
                policy);
        }
        catch (ToolResultNormalizationException exception)
        {
            if (policy.FailureMode == ToolOutputFailureMode.PreserveInline &&
                InlineSize(normalized) <= policy.MaxInlineResultBytes)
                return normalized;
            return Failure(exception.Code, exception.Message);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            if (policy.FailureMode == ToolOutputFailureMode.PreserveInline &&
                InlineSize(normalized) <= policy.MaxInlineResultBytes)
                return normalized;
            return Failure("content_write_failed", exception.Message);
        }
    }

    private static NormalizedToolResult EnforceInlineBudget(
        NormalizedToolResult result,
        ToolOutputConfig policy)
    {
        var size = InlineSize(result);
        if (size <= policy.MaxInlineResultBytes)
            return result;
        if (policy.FailureMode == ToolOutputFailureMode.PreserveInline)
            return result;
        return Failure(
            "tool_output_too_large",
            $"Tool result is {size} bytes; the model-facing limit is {policy.MaxInlineResultBytes} bytes.");
    }

    private static long InlineSize(NormalizedToolResult result)
    {
        var model = result.ToModelResult();
        return model switch
        {
            null => 0,
            string text => Encoding.UTF8.GetByteCount(text),
            JsonElement json => Encoding.UTF8.GetByteCount(json.GetRawText()),
            _ => Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(
                model, HPDJsonContext.Default.Options.GetTypeInfo(model.GetType())!))
        };
    }

    private static NormalizedToolResult Failure(string code, string message) =>
        new([new NormalizedToolErrorPart(code, message)], typeof(ToolResultError).FullName);

    private static bool TryGetNode(JsonNode root, string pointer, out JsonNode? value)
    {
        value = root;
        foreach (var part in pointer.Split('/', StringSplitOptions.RemoveEmptyEntries)
                     .Select(static value => value.Replace("~1", "/").Replace("~0", "~")))
        {
            if (value is not JsonObject obj || !obj.TryGetPropertyValue(part, out value))
                return false;
        }
        return true;
    }

    private static void SetNode(JsonNode root, string pointer, JsonNode? replacement)
    {
        var parts = pointer.Split('/', StringSplitOptions.RemoveEmptyEntries)
            .Select(static value => value.Replace("~1", "/").Replace("~0", "~"))
            .ToArray();
        var node = root;
        for (var index = 0; index < parts.Length - 1; index++)
        {
            if (node is not JsonObject obj || obj[parts[index]] is not JsonObject next)
                return;
            node = next;
        }
        if (node is JsonObject target)
            target[parts[^1]] = replacement;
    }

    private static string Digest(byte[] bytes) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
}
