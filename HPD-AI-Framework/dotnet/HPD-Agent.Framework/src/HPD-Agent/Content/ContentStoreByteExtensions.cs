using System.Text;

namespace HPD.Agent;

/// <summary>
/// Buffering convenience helpers for <see cref="IContentStore"/>.
/// Core content storage is stream-first; these helpers make intentional byte/text buffering explicit.
/// </summary>
public static class ContentStoreByteExtensions
{
    public static Task<ContentInfo> WriteBytesAsync(
        this IContentStore store,
        string? scope,
        byte[] data,
        string contentType,
        ContentMetadata? metadata = null,
        ContentWriteOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var resolvedMetadata = metadata is null
            ? new ContentMetadata { ContentType = contentType }
            : metadata with { ContentType = contentType };

        return store.WriteBytesAsync(scope, data, resolvedMetadata, options, cancellationToken);
    }

    public static Task<ContentInfo> WriteBytesAsync(
        this IContentStore store,
        string? scope,
        byte[] data,
        ContentMetadata metadata,
        ContentWriteOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (store == null) throw new ArgumentNullException(nameof(store));
        if (data == null) throw new ArgumentNullException(nameof(data));

        return store.WriteAsync(
            scope,
            new MemoryStream(data, writable: false),
            metadata,
            options ?? new ContentWriteOptions { Mode = ContentWriteMode.Create },
            cancellationToken);
    }

    public static async Task<byte[]?> ReadBytesAsync(
        this IContentStore store,
        string? scope,
        string contentId,
        CancellationToken cancellationToken = default)
    {
        if (store == null) throw new ArgumentNullException(nameof(store));

        await using var stream = await store.OpenReadAsync(scope, contentId, cancellationToken).ConfigureAwait(false);
        if (stream == null)
            return null;

        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, cancellationToken).ConfigureAwait(false);
        return memory.ToArray();
    }

    public static Task<byte[]?> ReadBytesAsync(
        this IContentStore store,
        string? scope,
        ContentInfo content,
        CancellationToken cancellationToken = default)
    {
        if (content == null) throw new ArgumentNullException(nameof(content));

        return store.ReadBytesAsync(scope, content.Id, cancellationToken);
    }

    public static Task DeleteAsync(
        this IContentStore store,
        string? scope,
        ContentInfo content,
        CancellationToken cancellationToken = default)
    {
        if (content == null) throw new ArgumentNullException(nameof(content));

        return store.DeleteAsync(scope, content.Id, null, cancellationToken);
    }

    public static bool Contains(this InMemoryContentStore store, string? scope, ContentInfo content)
    {
        if (content == null) throw new ArgumentNullException(nameof(content));

        return store.Contains(scope ?? "global", content.Id);
    }

    public static Task<ContentInfo> WriteTextAsync(
        this IContentStore store,
        string? scope,
        string text,
        ContentMetadata metadata,
        ContentWriteOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (text == null) throw new ArgumentNullException(nameof(text));

        var data = Encoding.UTF8.GetBytes(text);
        return store.WriteBytesAsync(scope, data, metadata, options, cancellationToken);
    }

    public static async Task<string?> ReadTextAsync(
        this IContentStore store,
        string? scope,
        string contentId,
        CancellationToken cancellationToken = default)
    {
        var data = await store.ReadBytesAsync(scope, contentId, cancellationToken).ConfigureAwait(false);
        return data == null ? null : Encoding.UTF8.GetString(data);
    }
}
