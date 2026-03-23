namespace HPD.Agent;

/// <summary>
/// Storage interface for binary assets (images, audio, videos, PDFs).
/// Extends IContentStore for unified content operations.
/// </summary>
/// <remarks>
/// <para>
/// IAssetStore provides a simple abstraction for storing large binary objects
/// that don't fit well in conversation history (images, audio files, documents).
/// </para>
/// <para><b>Key Concepts:</b></para>
/// <list type="bullet">
/// <item>Assets are stored separately from conversation messages</item>
/// <item>Messages reference assets via URI (asset://assetId)</item>
/// <item>Enables efficient storage (e.g., S3, blob storage, local files)</item>
/// <item>Supports multi-tenant scenarios (per-session isolation via scope parameter)</item>
/// </list>
/// <para><b>Usage:</b></para>
/// <para>
/// This interface is typically implemented by session stores that support
/// binary asset storage. The AssetUploadMiddleware automatically uploads
/// DataContent bytes to the asset store before LLM processing.
/// </para>
/// <para><b>IContentStore Integration (V2):</b></para>
/// <para>
/// IAssetStore extends IContentStore, providing unified Put/Get/Delete/Query operations.
/// All operations use the scope parameter (sessionId) for per-session isolation.
/// </para>
/// <para><b>Example Usage:</b></para>
/// <code>
/// // Store asset in session scope
/// var assetId = await assetStore.PutAsync(
///     scope: sessionId,
///     data: imageBytes,
///     contentType: "image/jpeg");
///
/// // Retrieve asset from session scope
/// var content = await assetStore.GetAsync(sessionId, assetId);
///
/// // Query all assets in session
/// var assets = await assetStore.QueryAsync(scope: sessionId);
/// </code>
/// </remarks>
public interface IAssetStore : IContentStore
{
    // Clean interface - all methods inherited from IContentStore
    // Use PutAsync(scope, data, contentType) to upload assets
    // Use GetAsync(scope, assetId) to download assets
    // Use DeleteAsync(scope, assetId) to delete assets
    // Use QueryAsync(scope) to list assets
}
