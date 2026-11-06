using System.Collections.Immutable;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Agent;

/// <summary>Marks a native tool-result property whose complete value should be stored as retrievable content.</summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field, AllowMultiple = false, Inherited = true)]
public sealed class OffloadAttribute : Attribute
{
    /// <summary>Gets or sets the disclosure classification of the offloaded value.</summary>
    public ContentSensitivity Sensitivity { get; set; } = ContentSensitivity.Internal;
}

/// <summary>Controls how stored content is delivered.</summary>
public enum ContentDisposition
{
    ProviderMaterialized,
    ModelRetrievable,
    InternalOnly
}

/// <summary>Describes the practical lifetime of stored content.</summary>
public enum ContentLifetime
{
    Ephemeral,
    SessionLifetime,
    SessionLifetimeAcrossRestart
}

/// <summary>Classifies the disclosure and handling requirements of content.</summary>
public enum ContentSensitivity
{
    Public,
    Internal,
    Restricted
}

/// <summary>Identifies one independently retrievable member of a content bundle.</summary>
public sealed record ContentPartReference
{
    /// <summary>Gets the part name.</summary>
    public required string Name { get; init; }
    /// <summary>Gets the SHA-256 digest of the exact part bytes.</summary>
    public required string Digest { get; init; }
    /// <summary>Gets the part media type.</summary>
    public required string MediaType { get; init; }
    /// <summary>Gets the part size.</summary>
    public required long SizeBytes { get; init; }
    /// <summary>Gets the part sensitivity.</summary>
    public required ContentSensitivity Sensitivity { get; init; }
}

/// <summary>Records the trusted runtime provenance of stored content.</summary>
public sealed record ContentSourceInfo
{
    /// <summary>Gets the owning session.</summary>
    public required string SessionId { get; init; }
    /// <summary>Gets the owning thread.</summary>
    public required string ThreadId { get; init; }
    /// <summary>Gets the source call identifier.</summary>
    public string? CallId { get; init; }
    /// <summary>Gets the source function name.</summary>
    public string? FunctionName { get; init; }
    /// <summary>Gets the source part name.</summary>
    public string? PartName { get; init; }
}

/// <summary>Identifies one immutable stored content bundle and its retrieval metadata.</summary>
public sealed record ContentReference
{
    /// <summary>Gets the content identifier.</summary>
    public required string ContentId { get; init; }
    /// <summary>Gets the exact content version.</summary>
    public string? Version { get; init; }
    /// <summary>Gets the bundle SHA-256 digest.</summary>
    public required string Digest { get; init; }
    /// <summary>Gets the bundle media type.</summary>
    public required string MediaType { get; init; }
    /// <summary>Gets the bundle size.</summary>
    public required long SizeBytes { get; init; }
    /// <summary>Gets the delivery disposition.</summary>
    public required ContentDisposition Disposition { get; init; }
    /// <summary>Gets the practical lifetime.</summary>
    public required ContentLifetime Lifetime { get; init; }
    /// <summary>Gets the strictest sensitivity across the bundle.</summary>
    public required ContentSensitivity Sensitivity { get; init; }
    /// <summary>Gets the optional expiry.</summary>
    public DateTimeOffset? ExpiresAt { get; init; }
    /// <summary>Gets trusted source metadata.</summary>
    public required ContentSourceInfo Source { get; init; }
    /// <summary>Gets the independently retrievable parts.</summary>
    public IReadOnlyList<ContentPartReference> Parts { get; init; } = [];
}

/// <summary>Controls whether native marked output is stored and replaced.</summary>
public sealed record ToolOutputConfig
{
    /// <summary>Gets whether [Offload] properties are projected.</summary>
    public bool Enabled { get; init; } = true;
    /// <summary>Gets the final model-facing result byte budget.</summary>
    public long MaxInlineResultBytes { get; init; } = 64 * 1024;
    /// <summary>Gets the failure policy for projection and inline overflow.</summary>
    public ToolOutputFailureMode FailureMode { get; init; } = ToolOutputFailureMode.FailClosed;
    /// <summary>Gets whether stored references may be retrieved.</summary>
    public bool ContentRetrievalEnabled { get; init; } = true;
    /// <summary>Gets retrieval limits.</summary>
    public ContentRetrievalLimits RetrievalLimits { get; init; } = new();
}

/// <summary>Controls final result behavior after offload projection.</summary>
public enum ToolOutputFailureMode
{
    FailClosed,
    PreserveInline
}

/// <summary>Bounds content list and read operations.</summary>
public sealed record ContentRetrievalLimits
{
    /// <summary>Gets the maximum list size per call.</summary>
    public int MaxListResultsPerCall { get; init; } = 50;
    /// <summary>Gets the maximum read size per call.</summary>
    public long MaxReadBytesPerCall { get; init; } = 64 * 1024;
    /// <summary>Gets the maximum read bytes per accepted input turn.</summary>
    public long MaxReadBytesPerTurn { get; init; } = 256 * 1024;
    /// <summary>Gets the maximum read calls per accepted input turn.</summary>
    public int MaxReadCallsPerTurn { get; init; } = 8;
    /// <summary>Gets the maximum list calls per accepted input turn.</summary>
    public int MaxListCallsPerTurn { get; init; } = 8;
}

/// <summary>Overrides native tool-output behavior for one execution.</summary>
public sealed record ToolOutputRunPolicy
{
    /// <summary>Gets whether [Offload] is honored.</summary>
    public bool? Enabled { get; init; }
    /// <summary>Gets an inline result budget override.</summary>
    public long? MaxInlineResultBytes { get; init; }
    /// <summary>Gets a failure policy override.</summary>
    public ToolOutputFailureMode? FailureMode { get; init; }
    /// <summary>Gets a retrieval availability override.</summary>
    public bool? ContentRetrievalEnabled { get; init; }
    /// <summary>Gets retrieval limit overrides.</summary>
    public ContentRetrievalLimits? RetrievalLimits { get; init; }

    internal ToolOutputConfig Resolve(ToolOutputConfig defaults)
    {
        ArgumentNullException.ThrowIfNull(defaults);
        return new ToolOutputConfig
        {
            Enabled = Enabled ?? defaults.Enabled,
            MaxInlineResultBytes = MaxInlineResultBytes ?? defaults.MaxInlineResultBytes,
            FailureMode = FailureMode ?? defaults.FailureMode,
            ContentRetrievalEnabled = ContentRetrievalEnabled ?? defaults.ContentRetrievalEnabled,
            RetrievalLimits = RetrievalLimits ?? defaults.RetrievalLimits
        };
    }
}

/// <summary>Declares one serialized result property selected for offloading.</summary>
public sealed record ContentProjectionPath(
    string Pointer,
    string Name,
    ContentSensitivity Sensitivity,
    bool StreamingCapture = false);

/// <summary>Describes generated native tool-result projection metadata.</summary>
public sealed record ContentProjectionPlan(
    Type ResultType,
    IReadOnlyList<ContentProjectionPath> Paths)
{
    /// <summary>Gets whether the plan contains any offloaded properties.</summary>
    public bool HasOffloads => Paths.Count > 0;
}

/// <summary>Deletes all content in a closed store scope.</summary>
public interface IContentStoreLifecycle
{
    /// <summary>Deletes all content in the specified scope.</summary>
    ValueTask DeleteScopeAsync(ContentScope scope, CancellationToken cancellationToken = default);
}

/// <summary>Represents buffered or streamed output captured during tool execution.</summary>
public sealed class ContentCapturePart : IAsyncDisposable
{
    private Stream _stream = new MemoryStream();
    private bool _completed;

    internal ContentCapturePart(string name, string mediaType, ContentSensitivity sensitivity)
    {
        Name = name;
        MediaType = mediaType;
        Sensitivity = sensitivity;
    }

    /// <summary>Gets the part name.</summary>
    public string Name { get; }
    /// <summary>Gets the part media type.</summary>
    public string MediaType { get; }
    /// <summary>Gets the part sensitivity.</summary>
    public ContentSensitivity Sensitivity { get; }
    /// <summary>Gets the captured size.</summary>
    public long SizeBytes => _stream.Length;

    /// <summary>Writes bytes to the capture.</summary>
    public async ValueTask WriteAsync(ReadOnlyMemory<byte> bytes, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_completed, this);
        await _stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    internal async ValueTask<byte[]> CompleteAsync(CancellationToken cancellationToken)
    {
        if (_completed)
            return ((MemoryStream)_stream).ToArray();
        _completed = true;
        _stream.Position = 0;
        await using var copy = new MemoryStream();
        await _stream.CopyToAsync(copy, cancellationToken).ConfigureAwait(false);
        return copy.ToArray();
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _stream.DisposeAsync();
}
