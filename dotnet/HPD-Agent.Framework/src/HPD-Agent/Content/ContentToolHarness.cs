using System.ComponentModel;
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
    [Description("Lists, inspects, or reads durable content available to the current agent thread. Select exactly one action.")]
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
        if (request.Limit is <= 0) throw new ArgumentOutOfRangeException(nameof(request), "Limit must be positive.");
        var budget = context.RunConfig.ContentRetrievalBudget ??= new();
        budget.ReserveList(policy.RetrievalLimits.MaxListCallsPerTurn);
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
        return new ContentListResult(page.Select(Info).ToArray(), next);
    }

    private static async Task<ContentStatResult> StatAsync(
        ContentStatOperation request,
        FunctionExecutionContext context,
        IContentStore store,
        CancellationToken cancellationToken)
    {
        var info = await AuthorizedInfoAsync(request.ContentId, context, store, cancellationToken).ConfigureAwait(false);
        var bundle = await ReadBundleAsync(info, store, cancellationToken).ConfigureAwait(false);
        var names = bundle.EnumerateObject().Select(static property => property.Name).ToArray();
        if (!string.IsNullOrEmpty(request.Part) && !names.Contains(request.Part, StringComparer.Ordinal))
            throw new FileNotFoundException($"Content part '{request.Part}' was not found.");
        return new ContentStatResult(Info(info), names);
    }

    private static async Task<ContentToolReadResult> ReadAsync(
        ContentReadOperation request,
        FunctionExecutionContext context,
        IContentStore store,
        ToolOutputConfig policy,
        CancellationToken cancellationToken)
    {
        if (request.StartLine is not null && request.Offset is not null)
            throw new ArgumentException("Line and byte ranges cannot be mixed.");
        if (request.StartLine is < 0 || request.EndLine is < 0 || request.Offset is < 0)
            throw new ArgumentOutOfRangeException(nameof(request), "Offsets and lines cannot be negative.");
        var budget = context.RunConfig.ContentRetrievalBudget ??= new();
        budget.ReserveRead(policy.RetrievalLimits.MaxReadCallsPerTurn);
        var info = await AuthorizedInfoAsync(
            request.ContentId, context, store, cancellationToken, request.Version, request.Digest).ConfigureAwait(false);
        if (info.Tags?.GetValueOrDefault("sensitivity") == nameof(ContentSensitivity.Restricted))
            throw new UnauthorizedAccessException("Restricted content is not model-retrievable.");
        var bundle = await ReadBundleAsync(info, store, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(request.Part) || !bundle.TryGetProperty(request.Part, out var value))
            throw new FileNotFoundException("An existing content part name is required.");
        var text = value.GetRawText();
        var bytes = Encoding.UTF8.GetBytes(text);
        var maxBytes = Math.Min(
            request.MaxBytes ?? policy.RetrievalLimits.MaxReadBytesPerCall,
            policy.RetrievalLimits.MaxReadBytesPerCall);
        if (bytes.Length > maxBytes)
            throw new InvalidOperationException("content_retrieval_budget_exceeded");
        budget.ReserveBytes(bytes.Length, policy.RetrievalLimits.MaxReadBytesPerTurn);
        if (request.StartLine is { } start)
        {
            var lines = text.Replace("\r\n", "\n").Split('\n');
            var end = Math.Min(request.EndLine ?? start + 199, lines.Length - 1);
            text = string.Join('\n', lines.Skip(start).Take(Math.Max(0, end - start + 1)));
        }
        else if (request.Offset is { } offset)
        {
            if (offset >= bytes.Length) throw new ArgumentOutOfRangeException(nameof(request), "Offset exceeds content.");
            text = Encoding.UTF8.GetString(bytes[(int)offset..]);
        }
        return new ContentToolReadResult(Info(info), value.ValueKind.ToString(), text);
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
        if (info.Tags?.GetValueOrDefault("thread-id") != context.ThreadId)
            throw new UnauthorizedAccessException("Content is outside the current thread.");
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

    private static ContentInfoProjection Info(ContentInfo info) => new(
        info.Address.ContentId,
        info.Address.Version,
        info.Address.Sha256,
        info.ContentType,
        info.SizeBytes,
        info.Tags?.GetValueOrDefault("sensitivity") ?? nameof(ContentSensitivity.Internal),
        info.Tags?.GetValueOrDefault("function"),
        info.Tags?.GetValueOrDefault("call-id"),
        info.CreatedAt);

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

[JsonPolymorphic(TypeDiscriminatorPropertyName = "action")]
[JsonDerivedType(typeof(ContentListOperation), "list")]
[JsonDerivedType(typeof(ContentStatOperation), "stat")]
[JsonDerivedType(typeof(ContentReadOperation), "read")]
public abstract record ContentOperation;

[AIFunctionAction("list", InvocationModePolicy = AIFunctionActionInvocationModePolicy.SynchronousOnly)]
public sealed record ContentListOperation(string? Kind = null, string? Function = null, string? CallId = null, int Limit = 50, string? Cursor = null) : ContentOperation;

[AIFunctionAction("stat", InvocationModePolicy = AIFunctionActionInvocationModePolicy.SynchronousOnly)]
public sealed record ContentStatOperation(string ContentId, string? Part = null) : ContentOperation;

[AIFunctionAction("read", InvocationModePolicy = AIFunctionActionInvocationModePolicy.SynchronousOnly)]
public sealed record ContentReadOperation(string ContentId, string? Version = null, string? Digest = null, string? Part = null, int? Offset = null, int? MaxBytes = null, int? StartLine = null, int? EndLine = null) : ContentOperation;

public abstract record ContentResult;
public sealed record ContentInfoProjection(string ContentId, string? Version, string? Digest, string MediaType, long SizeBytes, string Sensitivity, string? Function, string? CallId, DateTime CreatedAt);
public sealed record ContentListResult(IReadOnlyList<ContentInfoProjection> Items, string? NextCursor) : ContentResult;
public sealed record ContentStatResult(ContentInfoProjection Content, IReadOnlyList<string> Parts) : ContentResult;
public sealed record ContentToolReadResult(ContentInfoProjection Content, string Encoding, string Text) : ContentResult;
