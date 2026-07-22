using System.Collections.Immutable;
using HPD.Agent;

namespace HPD.Agent.ToolHarness.Coding.Debugging;

public enum DebugArtifactWriteStatus
{
    Stored,
    ContentStoreUnavailable,
    ContentStoreFailed,
    OutputTooLarge
}

public sealed record DebugArtifactWriteResult(
    DebugArtifactWriteStatus Status,
    ContentAddress? Address = null,
    long SizeBytes = 0,
    string? Preview = null);

/// <summary>Tree-owned narrow content-store facade with explicit scope and bounded fallback.</summary>
internal sealed class DebugArtifactWriter
{
    private const int MaximumPreviewCharacters = 4096;
    private readonly IContentStore? _store;
    private readonly ContentScope _scope;
    private readonly ImmutableDictionary<string, string> _ownershipTags;

    public DebugArtifactWriter(
        IContentStore? store,
        ContentScope scope,
        IReadOnlyDictionary<string, string> ownershipTags)
    {
        _store = store;
        if (string.IsNullOrWhiteSpace(scope.Value)) throw new ArgumentException("A content scope is required.", nameof(scope));
        _scope = scope;
        _ownershipTags = (ownershipTags ?? throw new ArgumentNullException(nameof(ownershipTags)))
            .ToImmutableDictionary(StringComparer.Ordinal);
    }

    public async ValueTask<DebugArtifactWriteResult> WriteTextAsync(
        string text,
        string artifactKind,
        string? category,
        string adapterId,
        string debugSessionId,
        long maximumBytes,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactKind);
        ArgumentException.ThrowIfNullOrWhiteSpace(adapterId);
        ArgumentException.ThrowIfNullOrWhiteSpace(debugSessionId);
        if (maximumBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maximumBytes));
        var bytes = System.Text.Encoding.UTF8.GetBytes(text);
        var preview = text[^Math.Min(text.Length, MaximumPreviewCharacters)..];
        if (bytes.LongLength > maximumBytes)
            return new(DebugArtifactWriteStatus.OutputTooLarge, SizeBytes: bytes.LongLength, Preview: preview);
        if (_store is null)
            return new(DebugArtifactWriteStatus.ContentStoreUnavailable, SizeBytes: bytes.LongLength, Preview: preview);
        try
        {
            var tags = _ownershipTags.SetItem("artifact-kind", artifactKind)
                .SetItem("adapter", adapterId)
                .SetItem("debug-session", debugSessionId);
            if (!string.IsNullOrWhiteSpace(category)) tags = tags.SetItem("category", category);
            await using var stream = new MemoryStream(bytes, writable: false);
            var info = await _store.WriteAsync(_scope, stream, new ContentMetadata
            {
                ContentType = "text/plain; charset=utf-8",
                Name = $"debug-{artifactKind}.txt",
                Description = "Bounded debugger artifact",
                Origin = ContentSource.Agent,
                Tags = tags
            }, new ContentWriteOptions { Mode = ContentWriteMode.Create }, cancellationToken).ConfigureAwait(false);
            return new(DebugArtifactWriteStatus.Stored, info.Address, info.SizeBytes);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            return new(DebugArtifactWriteStatus.ContentStoreFailed, SizeBytes: bytes.LongLength, Preview: preview);
        }
    }
}
