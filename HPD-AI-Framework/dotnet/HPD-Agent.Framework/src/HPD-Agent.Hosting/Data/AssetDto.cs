namespace HPD.Agent.Hosting.Data;

/// <summary>
/// Data transfer object for asset metadata.
/// Assets are session-scoped (shared across all branches).
/// </summary>
/// <param name="AssetId">Unique identifier for this asset</param>
/// <param name="ContentType">MIME type (e.g., "image/png", "application/pdf")</param>
/// <param name="SizeBytes">File size in bytes</param>
/// <param name="CreatedAt">When this asset was uploaded (ISO 8601 format)</param>
public record AssetDto(
    string AssetId,
    string ContentType,
    long SizeBytes,
    string CreatedAt);
