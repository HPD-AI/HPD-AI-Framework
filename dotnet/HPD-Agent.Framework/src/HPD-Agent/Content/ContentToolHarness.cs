using System.ComponentModel;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using HPD.Agent.Middleware;

namespace HPD.Agent;

/// <summary>Lists, inspects, or reads session-owned content.</summary>
public sealed class ContentToolHarness
{
    [AIFunction(Name = "Content", InvocationModePolicy = AgentInvocationModePolicy.SynchronousOnly)]
    [Description("Lists, inspects, or reads stored content available to the current agent thread. Persistence metadata identifies whether content survives a restart. Select exactly one action.")]
    public async Task<ContentResult> ContentAsync(
        ContentOperation request,
        FunctionExecutionContext context,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(context);
        var store = context.ContentStore
            ?? throw new InvalidOperationException("content_store_unavailable");
        var policy = ResolvePolicy(context);
        if (!policy.ContentRetrievalEnabled)
            throw new InvalidOperationException("content_retrieval_disabled");
        return request switch
        {
            ContentListOperation list => await ListAsync(list, context, store, policy, cancellationToken).ConfigureAwait(false),
            ContentStatOperation stat => await StatAsync(stat, context, store, cancellationToken).ConfigureAwait(false),
            ContentReadOperation read => await ReadAsync(read, context, store, policy, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(request))
        };
    }

    private static async Task<ContentListResult> ListAsync(
        ContentListOperation request,
        FunctionExecutionContext context,
        IContentStore store,
        ToolOutputConfig policy,
        CancellationToken cancellationToken)
    {
        var budget = context.ContentRetrievalBudget;
        budget.ReserveList(policy.RetrievalLimits.MaxListCallsPerTurn);
        if (request.Limit is <= 0) throw new ArgumentOutOfRangeException(nameof(request), "Limit must be positive.");
        var scope = Scope(context);
        var query = new ContentQuery
        {
            Limit = null,
            Name = null,
            Tags = new Dictionary<string, string>
            {
                ["kind"] = request.Kind ?? "tool-output",
                ["thread-id"] = context.ThreadId!
            }
        };
        var all = (await store.QueryAsync(scope, query, cancellationToken).ConfigureAwait(false))
            .OrderBy(static info => info.CreatedAt)
            .ThenBy(static info => info.Address.ContentId, StringComparer.Ordinal)
            .ToList();
        if (!string.IsNullOrEmpty(request.Function))
            all = all.Where(info => info.Tags?.GetValueOrDefault("function") == request.Function).ToList();
        if (!string.IsNullOrEmpty(request.CallId))
            all = all.Where(info => info.Tags?.GetValueOrDefault("call-id") == request.CallId).ToList();
        var offset = DecodeCursor(request.Cursor);
        if (offset is { } cursor)
            all = all.Where(info => info.CreatedAt.Ticks > cursor.Ticks ||
                info.CreatedAt.Ticks == cursor.Ticks &&
                string.CompareOrdinal(info.Address.ContentId, cursor.Id) > 0).ToList();
        var limit = Math.Min(request.Limit, policy.RetrievalLimits.MaxListResultsPerCall);
        var page = all.Take(limit).ToArray();
        var next = all.Count > limit && page.Length > 0
            ? EncodeCursor(page[^1].CreatedAt, page[^1].Address.ContentId)
            : null;
        return new ContentListResult(
            page.Select(info => Info(info, store.PersistenceCapability)).ToArray(),
            next);
    }

    private static async Task<ContentStatResult> StatAsync(
        ContentStatOperation request,
        FunctionExecutionContext context,
        IContentStore store,
        CancellationToken cancellationToken)
    {
        var info = await AuthorizedInfoAsync(request.ContentId, context, store, cancellationToken).ConfigureAwait(false);
        var names = PartNames(info);
        if (!string.IsNullOrEmpty(request.Part) && !names.Contains(request.Part, StringComparer.Ordinal))
            throw new FileNotFoundException($"Content part '{request.Part}' was not found.");
        return new ContentStatResult(Info(info, store.PersistenceCapability), names);
    }

    private static async Task<ContentToolReadResult> ReadAsync(
        ContentReadOperation request,
        FunctionExecutionContext context,
        IContentStore store,
        ToolOutputConfig policy,
        CancellationToken cancellationToken)
    {
        var budget = context.ContentRetrievalBudget;
        budget.ReserveRead(policy.RetrievalLimits.MaxReadCallsPerTurn);
        if (request.StartLine is not null && request.Offset is not null)
            throw new ArgumentException("Line and byte ranges cannot be mixed.");
        if (request.StartLine is < 0 || request.EndLine is < 0 || request.Offset is < 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Offsets and lines cannot be negative.");
        if (request.EndLine is { } requestedEnd && request.StartLine is { } requestedStart && requestedEnd < requestedStart)
            throw new ArgumentOutOfRangeException(nameof(request), "EndLine cannot precede StartLine.");
        if (request.MaxBytes is <= 0)
            throw new ArgumentOutOfRangeException(nameof(request), "MaxBytes must be positive.");
        var info = await AuthorizedInfoAsync(
            request.ContentId,
            context,
            store,
            cancellationToken,
            request.Version,
            string.IsNullOrWhiteSpace(request.Part) ? request.Digest : null).ConfigureAwait(false);
        RejectExpired(info);
        if (info.Tags?.GetValueOrDefault("sensitivity") == nameof(ContentSensitivity.Restricted))
            throw new UnauthorizedAccessException("Restricted content is not model-retrievable.");
        var bundle = await ReadBundleAsync(info, store, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(request.Part) || !bundle.TryGetProperty(request.Part, out var value))
            throw new FileNotFoundException("An existing content part name is required.");
        if (!string.IsNullOrWhiteSpace(request.Digest) &&
            !DigestEquals(PartDigest(info, request.Part), request.Digest))
            throw new ContentConflictException(
                "The content part digest does not match the requested exact part.",
                request.ContentId,
                request.Digest,
                PartDigest(info, request.Part));
        var maxBytes = Math.Min(
            request.MaxBytes ?? policy.RetrievalLimits.MaxReadBytesPerCall,
            policy.RetrievalLimits.MaxReadBytesPerCall);
        var mediaType = PartMediaType(info, request.Part, value);
        var projection = Info(info, store.PersistenceCapability);
        if (request.StartLine is not null ||
            request.EndLine is not null ||
            request.Offset is null && IsTextMediaType(mediaType))
        {
            var text = value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.GetRawText();
            var lines = text.Replace("\r\n", "\n").Split('\n');
            var start = request.StartLine ?? 0;
            if (start >= lines.Length)
                throw new ArgumentOutOfRangeException(nameof(request), "StartLine exceeds the content.");
            var end = Math.Min(request.EndLine ?? start + 199, lines.Length - 1);
            var selected = lines[start..(end + 1)];
            var selectedBytes = Encoding.UTF8.GetByteCount(string.Join('\n', selected));
            if (selectedBytes <= maxBytes)
            {
                budget.ReserveBytes(selectedBytes, policy.RetrievalLimits.MaxReadBytesPerTurn);
                var lineResult = new ContentToolReadResult(
                    projection,
                    "utf-8",
                    string.Join('\n', selected),
                    null,
                    end + 1 < lines.Length ? end + 1 : null);
                await TryPublishAsync(context, new ContentReadEvent(
                    info.Address.ContentId,
                    info.Address.Version,
                    request.Part,
                    lineResult.Encoding,
                    selectedBytes), cancellationToken).ConfigureAwait(false);
                return lineResult;
            }

            var lineByteOffset = Encoding.UTF8.GetByteCount(string.Join('\n', lines.Take(start))) +
                (start > 0 ? 1 : 0);
            var bytes = Encoding.UTF8.GetBytes(string.Join('\n', selected));
            var count = checked((int)Math.Min(maxBytes, bytes.Length));
            budget.ReserveBytes(count, policy.RetrievalLimits.MaxReadBytesPerTurn);
            var truncatedLineResult = new ContentToolReadResult(
                projection,
                "base64",
                Convert.ToBase64String(bytes.AsSpan(0, count)),
                lineByteOffset + count,
                null);
            await TryPublishAsync(context, new ContentReadEvent(
                info.Address.ContentId,
                info.Address.Version,
                request.Part,
                truncatedLineResult.Encoding,
                count), cancellationToken).ConfigureAwait(false);
            return truncatedLineResult;
        }

        var originalBytes = value.ValueKind == JsonValueKind.String &&
            !IsTextMediaType(mediaType)
                ? Convert.FromBase64String(value.GetString() ?? "")
                : Encoding.UTF8.GetBytes(
                    value.ValueKind == JsonValueKind.String ? value.GetString() ?? "" : value.GetRawText());
        var offset = request.Offset ?? 0;
        if (offset >= originalBytes.Length)
            throw new ArgumentOutOfRangeException(nameof(request), "Offset exceeds content.");
        var length = checked((int)Math.Min(maxBytes, originalBytes.Length - offset));
        budget.ReserveBytes(length, policy.RetrievalLimits.MaxReadBytesPerTurn);
        long? nextOffset = offset + length < originalBytes.Length ? offset + length : null;
        var byteResult = new ContentToolReadResult(
            projection,
            "base64",
            Convert.ToBase64String(originalBytes, checked((int)offset), length),
            nextOffset,
            null);
        await TryPublishAsync(context, new ContentReadEvent(
            info.Address.ContentId,
            info.Address.Version,
            request.Part,
            byteResult.Encoding,
            length), cancellationToken).ConfigureAwait(false);
        return byteResult;
    }

    private static ToolOutputConfig ResolvePolicy(FunctionExecutionContext context) =>
        context.RunConfig.ToolOutputs?.Resolve(context.ParentConfig?.ToolOutputs ?? new())
        ?? context.ParentConfig?.ToolOutputs
        ?? new();

    private static ContentScope Scope(FunctionExecutionContext context) =>
        ContentScope.Create(context.SessionId ?? throw new UnauthorizedAccessException("content_session_required"));

    private static async ValueTask<ContentInfo> AuthorizedInfoAsync(
        string contentId,
        FunctionExecutionContext context,
        IContentStore store,
        CancellationToken cancellationToken,
        string? version = null,
        string? digest = null)
    {
        var info = await store.StatAsync(
            ContentAddress.Create(Scope(context), contentId, version, NormalizeDigest(digest)),
            cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException($"Content '{contentId}' was not found.");
        if (info.Tags?.GetValueOrDefault("thread-id") is not { } ownerThreadId ||
            !context.CanAccessContentOwnedBy(ownerThreadId))
            throw new UnauthorizedAccessException("Content is outside the current thread.");
        if (version is not null &&
            !string.Equals(info.Address.Version, version, StringComparison.Ordinal))
            throw new ContentConflictException(
                "The content version does not match the requested exact version.",
                contentId,
                version,
                info.Address.Version);
        return info;
    }

    private static async ValueTask<JsonElement> ReadBundleAsync(
        ContentInfo info,
        IContentStore store,
        CancellationToken cancellationToken)
    {
        await using var opened = await store.OpenReadAsync(info.Address, cancellationToken).ConfigureAwait(false)
            ?? throw new FileNotFoundException("Content was not found.");
        using var document = await JsonDocument.ParseAsync(opened.Content, cancellationToken: cancellationToken).ConfigureAwait(false);
        return document.RootElement.GetProperty("parts").Clone();
    }

    private static ContentInfoProjection Info(
        ContentInfo info,
        ContentStorePersistenceCapability persistence) => new(
        info.Address.ContentId,
        info.Address.Version,
        info.Address.Sha256 is null ? null : "sha256:" + info.Address.Sha256,
        info.ContentType,
        info.SizeBytes,
        info.Tags?.GetValueOrDefault("sensitivity") ?? nameof(ContentSensitivity.Internal),
        info.Tags?.GetValueOrDefault("function"),
        info.Tags?.GetValueOrDefault("call-id"),
        info.CreatedAt,
        persistence.ToString(),
        info.Tags?.GetValueOrDefault("expires-at") is { } expiresAt &&
            DateTimeOffset.TryParse(expiresAt, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var parsedExpiresAt)
            ? parsedExpiresAt
            : null);

    private static string[] PartNames(ContentInfo info)
    {
        var value = info.Tags?.GetValueOrDefault("parts");
        if (string.IsNullOrWhiteSpace(value))
            return [];
        try
        {
            return JsonSerializer.Deserialize(value, HPDJsonContext.Default.StringArray) ?? [];
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The content part manifest is invalid.", exception);
        }
    }

    private static string PartMediaType(ContentInfo info, string part, JsonElement value)
    {
        var manifest = info.Tags?.GetValueOrDefault("part-media-types");
        if (!string.IsNullOrWhiteSpace(manifest))
        {
            try
            {
                var mediaTypes = JsonSerializer.Deserialize(
                    manifest, HPDJsonContext.Default.DictionaryStringString);
                if (mediaTypes?.TryGetValue(part, out var mediaType) == true)
                    return mediaType;
            }
            catch (JsonException exception)
            {
                throw new InvalidDataException("The content part media manifest is invalid.", exception);
            }
        }
        return value.ValueKind == JsonValueKind.String ? "text/plain" : "application/json";
    }

    private static string? PartDigest(ContentInfo info, string part)
    {
        var manifest = info.Tags?.GetValueOrDefault("part-digests");
        if (string.IsNullOrWhiteSpace(manifest))
            return null;
        try
        {
            var digests = JsonSerializer.Deserialize(
                manifest, HPDJsonContext.Default.DictionaryStringString);
            return digests?.GetValueOrDefault(part);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException("The content part digest manifest is invalid.", exception);
        }
    }

    private static bool DigestEquals(string? actual, string expected) =>
        string.Equals(NormalizeDigest(actual), NormalizeDigest(expected), StringComparison.OrdinalIgnoreCase);

    private static async ValueTask TryPublishAsync(
        FunctionExecutionContext context,
        AgentEvent evt,
        CancellationToken cancellationToken)
    {
        try
        {
            await context.TryPublishAsync(evt, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Content reads remain successful when optional diagnostics are unavailable.
        }
    }

    private static bool IsTextMediaType(string mediaType) =>
        mediaType.StartsWith("text/", StringComparison.OrdinalIgnoreCase) ||
        mediaType.Contains("json", StringComparison.OrdinalIgnoreCase) ||
        mediaType.Contains("xml", StringComparison.OrdinalIgnoreCase) ||
        mediaType.EndsWith("+text", StringComparison.OrdinalIgnoreCase);

    private static void RejectExpired(ContentInfo info)
    {
        if (info.Tags?.GetValueOrDefault("expires-at") is not { } value)
            return;
        if (!DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture,
                DateTimeStyles.RoundtripKind, out var expiresAt))
            throw new InvalidDataException("The content expiry metadata is invalid.");
        if (expiresAt <= DateTimeOffset.UtcNow)
            throw new InvalidOperationException("content_reference_expired");
    }

    private static string? NormalizeDigest(string? digest) =>
        digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true ? digest[7..] : digest;

    private static (long Ticks, string Id)? DecodeCursor(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor)) return null;
        try
        {
            var value = Encoding.UTF8.GetString(Convert.FromBase64String(cursor));
            var parts = value.Split('|', 2);
            return long.TryParse(parts[0], out var ticks) ? (ticks, parts[1]) : throw new FormatException();
        }
        catch { throw new InvalidOperationException("content_cursor_invalid"); }
    }

    private static string EncodeCursor(DateTime created, string id) =>
        Convert.ToBase64String(Encoding.UTF8.GetBytes($"{created.Ticks}|{id}"));
}

internal sealed class ContentRetrievalBudget
{
    private int _lists;
    private int _reads;
    private long _bytes;
    internal void ReserveList(int maximum)
    {
        if (Interlocked.Increment(ref _lists) > maximum) throw new InvalidOperationException("content_retrieval_turn_budget_exceeded");
    }
    internal void ReserveRead(int maximum)
    {
        if (Interlocked.Increment(ref _reads) > maximum) throw new InvalidOperationException("content_retrieval_turn_budget_exceeded");
    }
    internal void ReserveBytes(long count, long maximum)
    {
        if (Interlocked.Add(ref _bytes, count) > maximum) throw new InvalidOperationException("content_retrieval_turn_budget_exceeded");
    }
}

/// <summary>Selects one bounded content retrieval operation.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "action")]
[JsonDerivedType(typeof(ContentListOperation), "list")]
[JsonDerivedType(typeof(ContentStatOperation), "stat")]
[JsonDerivedType(typeof(ContentReadOperation), "read")]
public abstract record ContentOperation;

/// <summary>Lists session content metadata in deterministic order.</summary>
[AIFunctionAction("list", InvocationModePolicy = AIFunctionActionInvocationModePolicy.SynchronousOnly)]
public sealed record ContentListOperation(string? Kind = null, string? Function = null, string? CallId = null, int Limit = 50, string? Cursor = null) : ContentOperation;

/// <summary>Inspects stored content metadata without opening its body.</summary>
[AIFunctionAction("stat", InvocationModePolicy = AIFunctionActionInvocationModePolicy.SynchronousOnly)]
public sealed record ContentStatOperation(string ContentId, string? Part = null) : ContentOperation;

/// <summary>Reads one bounded line or byte range from a stored part.</summary>
[AIFunctionAction("read", InvocationModePolicy = AIFunctionActionInvocationModePolicy.SynchronousOnly)]
public sealed record ContentReadOperation(string ContentId, string? Version = null, string? Digest = null, string? Part = null, int? Offset = null, int? MaxBytes = null, int? StartLine = null, int? EndLine = null) : ContentOperation;

/// <summary>Closed result family returned by the Content tool.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "resultType")]
[JsonDerivedType(typeof(ContentListResult), "list")]
[JsonDerivedType(typeof(ContentStatResult), "stat")]
[JsonDerivedType(typeof(ContentToolReadResult), "read")]
public abstract record ContentResult;
/// <summary>Projects bounded content metadata.</summary>
public sealed record ContentInfoProjection(
    string ContentId,
    string? Version,
    string? Digest,
    string MediaType,
    long SizeBytes,
    string Sensitivity,
    string? Function,
    string? CallId,
    DateTime CreatedAt,
    string Persistence,
    DateTimeOffset? ExpiresAt);
/// <summary>Returns one deterministic metadata page.</summary>
public sealed record ContentListResult(IReadOnlyList<ContentInfoProjection> Items, string? NextCursor) : ContentResult;
/// <summary>Returns content metadata and retrievable part names.</summary>
public sealed record ContentStatResult(ContentInfoProjection Content, IReadOnlyList<string> Parts) : ContentResult;
/// <summary>Returns one bounded content range and continuation metadata.</summary>
public sealed record ContentToolReadResult(
    ContentInfoProjection Content,
    string Encoding,
    string Text,
    long? NextOffset,
    int? NextLine) : ContentResult;
