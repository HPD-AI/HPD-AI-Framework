using System.Collections.Concurrent;
using System.Text.Json;

namespace HPD.Agent.Client;

/// <summary>Responder identity and capabilities advertised by one client instance.</summary>
public sealed record HpdAgentResponderOptions
{
    public string? ResponderId { get; init; }
    public IReadOnlyList<string> ResponderGroups { get; init; } = [];
    public IReadOnlyList<string> Capabilities { get; init; } = [];
}

/// <summary>The application-neutral portion of one client-tool invocation.</summary>
public sealed record HpdAgentClientToolInvocation(
    string RequestId,
    string ToolName,
    string CallId,
    JsonElement Arguments,
    string? Description,
    JsonElement? Target,
    int? ResponsePolicy);

/// <summary>A normalized result from a client-tool handler.</summary>
public sealed record HpdAgentClientToolResult(
    string Outcome,
    List<JsonElement>? Content,
    string? ErrorMessage,
    string? ClientOperationId,
    int? OperationKind,
    int? OperationCapabilities,
    JsonElement? Augmentation)
{
    public static HpdAgentClientToolResult Text(string text) =>
        Completed([TextContent(text)]);

    public static HpdAgentClientToolResult Json(JsonElement value) =>
        Completed([JsonContent(value)]);

    public static HpdAgentClientToolResult WithContent(IEnumerable<JsonElement> content) =>
        Completed(content.ToList());

    public static HpdAgentClientToolResult Failed(string message) =>
        new("Failed", null, message, null, null, null, null);

    public static HpdAgentClientToolResult Completed(List<JsonElement>? content = null) =>
        new("Completed", content, null, null, null, null, null);

    /// <summary>Creates a standard text content item.</summary>
    public static JsonElement TextContent(string text)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "text");
            writer.WriteString("text", text);
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }

    /// <summary>Creates a standard structured JSON content item.</summary>
    public static JsonElement JsonContent(JsonElement value)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("type", "json");
            writer.WritePropertyName("value");
            value.WriteTo(writer);
            writer.WriteEndObject();
        }
        using var document = JsonDocument.Parse(stream.ToArray());
        return document.RootElement.Clone();
    }
}

/// <summary>Executes one registered client tool.</summary>
public delegate ValueTask<HpdAgentClientToolResult> HpdAgentClientToolHandler(
    HpdAgentClientToolInvocation invocation,
    CancellationToken cancellationToken);

/// <summary>Application-owned client tools selected by normalized tool name.</summary>
public sealed class HpdAgentClientToolRegistry
{
    private readonly ConcurrentDictionary<string, HpdAgentClientToolHandler> _handlers =
        new(StringComparer.Ordinal);
    private HpdAgentClientToolHandler? _fallback;
    private readonly ConcurrentDictionary<string, HpdAgentClientToolHarness> _harnesses =
        new(StringComparer.Ordinal);

    public HpdAgentClientToolRegistry Register(string name, HpdAgentClientToolHandler handler)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(handler);
        _handlers[NormalizeName(name)] = handler;
        return this;
    }

    public HpdAgentClientToolRegistry RegisterFallback(HpdAgentClientToolHandler handler)
    {
        ArgumentNullException.ThrowIfNull(handler);
        _fallback = handler;
        return this;
    }

    public HpdAgentClientToolRegistry RegisterHarness(
        HpdAgentClientToolHarness harness,
        HpdAgentClientToolHandler handler)
    {
        ArgumentNullException.ThrowIfNull(harness);
        ArgumentNullException.ThrowIfNull(handler);
        _harnesses[harness.Name] = harness;
        foreach (var tool in harness.Tools)
            Register(tool.Name, handler);
        return this;
    }

    public bool Unregister(string name) => _handlers.TryRemove(NormalizeName(name), out _);

    public void UnregisterFallback() => _fallback = null;

    public void Clear()
    {
        _handlers.Clear();
        _harnesses.Clear();
        _fallback = null;
    }

    public bool CanHandle(string name) =>
        _handlers.ContainsKey(NormalizeName(name)) || Volatile.Read(ref _fallback) is not null;

    public IReadOnlyList<string> Capabilities =>
        _handlers.Keys
            .OrderBy(static name => name, StringComparer.Ordinal)
            .Select(static name => "client-tool:" + name)
            .ToArray();

    /// <summary>Gets the registered harness snapshots for inclusion in an input.</summary>
    public IReadOnlyList<HpdAgentClientToolHarness> Harnesses => _harnesses.Values.ToArray();

    public async ValueTask<HpdAgentClientToolResult> InvokeAsync(
        HpdAgentClientToolInvocation invocation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invocation);
        if (!_handlers.TryGetValue(NormalizeName(invocation.ToolName), out var handler))
            handler = Volatile.Read(ref _fallback);
        if (handler is null)
            return HpdAgentClientToolResult.Failed(
                $"Unknown client tool: {invocation.ToolName}");

        try
        {
            return await handler(invocation, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return HpdAgentClientToolResult.Failed(
                string.IsNullOrWhiteSpace(exception.Message)
                    ? "Client tool failed."
                    : exception.Message);
        }
    }

    public static string NormalizeName(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var trimmed = value.Trim();
        var separator = trimmed.LastIndexOf('.');
        return separator >= 0 && separator < trimmed.Length - 1
            ? trimmed[(separator + 1)..]
            : trimmed;
    }
}

/// <summary>A declarative group of tools advertised to the agent.</summary>
public sealed record HpdAgentClientToolHarness(
    string Name,
    IReadOnlyList<HpdAgentClientToolDefinition> Tools,
    string? Description = null,
    bool StartCollapsed = false);

/// <summary>A client-owned tool definition.</summary>
public sealed record HpdAgentClientToolDefinition(
    string Name,
    string Description,
    JsonElement ParametersSchema);

/// <summary>Runtime subscription handle returned by generated event observers.</summary>
public sealed class HpdAgentEventSubscription : IDisposable
{
    private Action? _dispose;

    public HpdAgentEventSubscription(Action dispose)
    {
        _dispose = dispose;
    }

    public void Dispose() => Interlocked.Exchange(ref _dispose, null)?.Invoke();
}
