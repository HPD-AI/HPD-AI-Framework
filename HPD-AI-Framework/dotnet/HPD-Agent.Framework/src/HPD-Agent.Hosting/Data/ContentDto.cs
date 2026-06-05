namespace HPD.Agent.Hosting.Data;

/// <summary>
/// Data transfer object for content metadata.
/// Content items are scoped to a session branch.
/// </summary>
/// <param name="ContentId">Unique identifier for this content</param>
/// <param name="Version">Opaque content version token for conditional writes/deletes</param>
/// <param name="ContentType">MIME type (e.g., "image/png", "application/pdf")</param>
/// <param name="SizeBytes">File size in bytes</param>
/// <param name="CreatedAt">When this content was uploaded (ISO 8601 format)</param>
public record ContentDto(
    string ContentId,
    string Version,
    string ContentType,
    long SizeBytes,
    string CreatedAt);
