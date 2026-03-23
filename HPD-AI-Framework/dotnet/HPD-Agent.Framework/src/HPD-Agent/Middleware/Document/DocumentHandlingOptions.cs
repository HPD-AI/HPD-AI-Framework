using HPD.Agent.TextExtraction;
using HPD.Agent.TextExtraction.Decoders;

namespace HPD.Agent.Middleware.Document;

/// <summary>
/// Configuration options for document handling middleware.
/// </summary>
public class DocumentHandlingOptions
{
    /// <summary>
    /// Custom tag format for document injection.
    /// Format string with {0} = filename, {1} = extracted text.
    /// Default: "\n\n[ATTACHED_DOCUMENT[{0}]]\n{1}\n[/ATTACHED_DOCUMENT]\n\n"
    /// </summary>
    public string? CustomTagFormat { get; set; }

    /// <summary>
    /// Maximum document size in bytes (default: 10MB).
    /// Documents exceeding this size will be rejected.
    /// </summary>
    public long MaxDocumentSizeBytes { get; set; } = 10 * 1024 * 1024;

    /// <summary>
    /// Whether to include page metadata in chunks.
    /// </summary>
    public bool IncludePageMetadata { get; set; } = true;

    /// <summary>
    /// OCR engine for image processing (optional).
    /// If not provided, images will not have OCR performed.
    /// </summary>
    public IOcrEngine? OcrEngine { get; set; }
}
