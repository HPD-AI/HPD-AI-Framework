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
            var projectedPaths = new List<ContentProjectionPath>(plan.Paths.Count);
            foreach (var path in plan.Paths)
            {
                if (!TryGetNode(root, path.Pointer, out var value) || value is null)
                    continue;
                var (partValue, mediaType, partName, declaredSensitivity) = path.StreamingCapture
                    ? ReadCapture(value)
                    : (value.DeepClone(), path.MediaType, path.Name, ContentSensitivity.Public);
                var effectiveSensitivity = path.Sensitivity > declaredSensitivity
                    ? path.Sensitivity
                    : declaredSensitivity;
                if (parts.Any(part => string.Equals(part.Name, partName, StringComparison.Ordinal)))
                    throw new ToolResultNormalizationException(
                        "tool_output_projection_failed",
                        $"Content bundle part name '{partName}' is declared more than once.");
                captured[partName] = partValue;
                var bytes = PartBytes(partValue);
                parts.Add(new ContentPartReference
                {
                    Name = partName,
                    Digest = Digest(bytes),
                    MediaType = mediaType,
                    SizeBytes = bytes.Length,
                    Sensitivity = effectiveSensitivity
                });
                projectedPaths.Add(path);
                if (path.StreamingCapture)
                {
                    await TryPublishAsync(context, new ContentCaptureFinalizedEvent(
                        preparation.Function?.Name,
                        preparation.FunctionCall.CallId,
                        partName,
                        mediaType,
                        bytes.Length), cancellationToken).ConfigureAwait(false);
                }
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
            var bundleDigest = Digest(bundleBytes);
            var projectionContract = string.Join(';', plan.Paths.Select(static path =>
                $"{path.Pointer}|{path.Name}|{path.MediaType}|{path.Sensitivity}|{path.StreamingCapture}"));
            var identity = string.Join('|', sessionId, threadId, preparation.FunctionCall.CallId,
                preparation.Function?.Name, normalized.DeclaredResultType ?? plan.ResultType.FullName,
                projectionContract, bundleDigest);
            var stableName = "tool-output-" + Hash(Encoding.UTF8.GetBytes(identity));
            var scope = ContentScope.Create(sessionId);
            var existing = (await store.QueryAsync(scope, new ContentQuery
                {
                    Name = stableName,
                    Tags = new Dictionary<string, string> { ["kind"] = "tool-output" }
                }, cancellationToken).ConfigureAwait(false)).FirstOrDefault();
            var sensitivity = parts.Count == 0
                ? ContentSensitivity.Internal
                : parts.Max(static part => part.Sensitivity);
            var metadata = new ContentMetadata
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
                    ["sensitivity"] = sensitivity.ToString(),
                    ["parts"] = JsonSerializer.Serialize(parts.Select(static part => part.Name).ToArray(),
                        HPDJsonContext.Default.StringArray),
                    ["part-media-types"] = JsonSerializer.Serialize(
                        parts.ToDictionary(static part => part.Name, static part => part.MediaType,
                            StringComparer.Ordinal),
                        HPDJsonContext.Default.DictionaryStringString),
                    ["part-digests"] = JsonSerializer.Serialize(
                        parts.ToDictionary(static part => part.Name, static part => part.Digest,
                            StringComparer.Ordinal),
                        HPDJsonContext.Default.DictionaryStringString)
                }
            };
            var info = existing ?? await WriteBundleAsync(
                store, scope, bundleBytes, bundleDigest, metadata, stableName, cancellationToken)
                .ConfigureAwait(false);
            if (!DigestEquals(info.Address.Sha256, bundleDigest))
                throw new ToolResultNormalizationException(
                    "content_bundle_publish_failed",
                    "The stored tool-output bundle digest does not match the finalized bundle.");
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
            foreach (var path in projectedPaths)
            {
                if (TryGetNode(root, path.Pointer, out var current) &&
                    current is not null)
                {
                    SetNode(root, path.Pointer, referenceNode?.DeepClone());
                }
            }
            await TryPublishAsync(context, new ToolOutputProjectedEvent(
                preparation.Function?.Name,
                preparation.FunctionCall.CallId,
                info.Address.ContentId,
                "tool-output",
                parts.Count,
                info.SizeBytes,
                store.PersistenceCapability), cancellationToken).ConfigureAwait(false);
            return EnforceInlineBudget(
                new NormalizedToolResult([new NormalizedJsonPart(
                    JsonSerializer.SerializeToElement(root, HPDJsonContext.Default.JsonNode))],
                    normalized.DeclaredResultType),
                policy);
        }
        catch (ToolResultNormalizationException exception)
        {
            await TryPublishAsync(context, new ToolOutputProjectionFailedEvent(
                preparation.Function?.Name,
                preparation.FunctionCall.CallId,
                null,
                exception.Code,
                exception.Message), cancellationToken).ConfigureAwait(false);
            if (policy.FailureMode == ToolOutputFailureMode.PreserveInline &&
                InlineSize(normalized) <= policy.MaxInlineResultBytes)
                return normalized;
            return Failure(exception.Code, exception.Message);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            await TryPublishAsync(context, new ToolOutputProjectionFailedEvent(
                preparation.Function?.Name,
                preparation.FunctionCall.CallId,
                null,
                "content_write_failed",
                exception.Message), cancellationToken).ConfigureAwait(false);
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

    private static (JsonNode Value, string MediaType, string Name, ContentSensitivity Sensitivity) ReadCapture(
        JsonNode value)
    {
        if (value is not JsonObject capture ||
            capture[ContentCaptureJsonConverter.MarkerProperty]?.GetValue<string>() !=
                ContentCaptureJsonConverter.MarkerValue ||
            capture[ContentCaptureJsonConverter.DataProperty] is not JsonValue data ||
            !data.TryGetValue<string>(out var encoded))
            throw new ToolResultNormalizationException(
                "tool_output_projection_failed",
                "A streaming content capture was not finalized into the canonical result.");
        var mediaType = capture[ContentCaptureJsonConverter.MediaTypeProperty]?.GetValue<string>()
            ?? "application/octet-stream";
        var sensitivityText = capture[ContentCaptureJsonConverter.SensitivityProperty]?.GetValue<string>();
        if (!Enum.TryParse<ContentSensitivity>(sensitivityText, ignoreCase: false, out var sensitivity) ||
            !Enum.IsDefined(sensitivity))
            throw new ToolResultNormalizationException(
                "tool_output_projection_failed",
                $"A streaming content capture has invalid sensitivity '{sensitivityText ?? "missing"}'.");
        var name = capture[ContentCaptureJsonConverter.NameProperty]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(name))
            throw new ToolResultNormalizationException(
                "tool_output_projection_failed",
                "A streaming content capture has no part name.");
        return (JsonValue.Create(encoded), mediaType, name, sensitivity);
    }

    private static byte[] PartBytes(JsonNode value) => value switch
    {
        JsonValue scalar when scalar.TryGetValue<string>(out var text) => Encoding.UTF8.GetBytes(text),
        _ => Encoding.UTF8.GetBytes(value.ToJsonString())
    };

    private static async ValueTask<ContentInfo> WriteBundleAsync(
        IContentStore store,
        ContentScope scope,
        byte[] bundleBytes,
        string bundleDigest,
        ContentMetadata metadata,
        string stableName,
        CancellationToken cancellationToken)
    {
        try
        {
            return await store.WriteBytesAsync(
                scope,
                bundleBytes,
                metadata,
                new ContentWriteOptions
                {
                    Mode = ContentWriteMode.Create,
                    FailIfNameExists = true
                },
                cancellationToken).ConfigureAwait(false);
        }
        catch (ContentConflictException)
        {
            var existing = (await store.QueryAsync(scope, new ContentQuery
                {
                    Name = stableName,
                    Tags = new Dictionary<string, string> { ["kind"] = "tool-output" }
                }, cancellationToken).ConfigureAwait(false)).SingleOrDefault()
                ?? throw new ToolResultNormalizationException(
                    "content_bundle_publish_failed",
                    "An idempotent tool-output bundle was reported as existing but could not be found.");
            if (!DigestEquals(existing.Address.Sha256, bundleDigest))
                throw new ToolResultNormalizationException(
                    "content_bundle_publish_failed",
                    "The idempotent tool-output identity resolved to different bytes.");
            return existing;
        }
    }

    private static bool DigestEquals(string? actual, string expected) =>
        string.Equals(
            actual?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true
                ? actual[7..]
                : actual,
            expected.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase)
                ? expected[7..]
                : expected,
            StringComparison.OrdinalIgnoreCase);

    private static async ValueTask TryPublishAsync(
        AgentContext context,
        AgentEvent evt,
        CancellationToken cancellationToken)
    {
        try
        {
            await context.PublishAsync(evt, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Diagnostics must not change model delivery semantics after canonical projection.
        }
    }
}
