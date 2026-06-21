namespace HPD.RAG.Core.DTOs;

/// <summary>
/// Checkpoint-safe representation of a parsed document.
/// Maps from DataIngestion's IngestionDocument at the reader→socket boundary.
/// </summary>
public sealed record MragDocumentDto
{
    public required string Id { get; init; }
    public required MragDocumentElementDto[] Elements { get; init; }
}

/// <summary>
/// A single structural element within a document (paragraph, header, image, table, etc.).
/// </summary>
public sealed record MragDocumentElementDto
{
    /// <summary>Element type: "paragraph", "header", "image", "table", "code", "list".</summary>
    public required string Type { get; init; }

    public string? Text { get; init; }

    /// <summary>Header level (1–6). Populated only when Type == "header".</summary>
    public int? HeaderLevel { get; init; }

    /// <summary>Alt-text for images. Populated by image enricher or extracted from source.</summary>
    public string? AlternativeText { get; init; }

    /// <summary>Base64-encoded binary content (images). Null for text elements.</summary>
    public string? Base64Content { get; init; }

    /// <summary>MIME type for binary content (e.g. "image/png"). Null for text elements.</summary>
    public string? MediaType { get; init; }

    public int? PageNumber { get; init; }
}
