/// <summary>
/// Configuration options for skills.
/// Skills are ALWAYS Collapsed - functions are hidden until the skill is activated.
/// This prevents token bloat when you have many skills.
/// </summary>
public class SkillOptions
{
    /// <summary>
    /// Document references for this skill (document IDs from store)
    /// </summary>
    internal List<DocumentReference> DocumentReferences { get; set; } = new();

    /// <summary>
    /// Document uploads for this skill (files to auto-upload)
    /// </summary>
    internal List<DocumentUpload> DocumentUploads { get; set; } = new();

    /// <summary>
    /// Reference a document that already exists in the global store.
    /// Documents must be uploaded first (either via another skill's AddDocumentFromFile or external upload).
    /// </summary>
    /// <param name="documentId">Document ID (must exist in store)</param>
    /// <param name="description">
    /// Optional skill-specific description override.
    /// If null, uses the default description from the store.
    /// The description helps the agent understand what the document contains and decide whether to read it.
    /// Provide a description if you want to give skill-specific context (e.g., same document used differently in different skills).
    /// </param>
    /// <exception cref="ArgumentException">Thrown if documentId is empty</exception>
    public SkillOptions AddDocument(
        string documentId,
        string? description = null)
    {
        if (string.IsNullOrWhiteSpace(documentId))
            throw new ArgumentException("Document ID cannot be empty.", nameof(documentId));

        // Validation only if description provided
        if (description != null && string.IsNullOrWhiteSpace(description))
            throw new ArgumentException(
                $"Document '{documentId}' description cannot be empty if provided.",
                nameof(description));

        DocumentReferences.Add(new DocumentReference
        {
            DocumentId = documentId,
            DescriptionOverride = description
        });

        return this;
    }

    /// <summary>
    /// Upload a document directly from file path.
    /// Document is automatically uploaded to store at application startup.
    /// Uses content hash to avoid re-uploading unchanged documents.
    /// </summary>
    /// <param name="filePath">Path to document file (relative to project root)</param>
    /// <param name="description">
    /// Document description (REQUIRED).
    /// This description helps the agent understand what the document contains and decide whether to read it.
    /// Be specific about the content (e.g., "Step-by-step debugging methodology" instead of just "Debugging").
    /// </param>
    /// <param name="documentId">Document ID (optional, auto-derived from filename if null)</param>
    /// <exception cref="ArgumentException">Thrown if filePath or description is empty</exception>
    public SkillOptions AddDocumentFromFile(
        string filePath,
        string description,
        string? documentId = null)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be empty.", nameof(filePath));

        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException(
                "Description is required when uploading documents. " +
                "It helps the agent understand what the document contains and decide whether to read it.",
                nameof(description));

        // Auto-derive document ID from filename if not provided
        var effectiveDocumentId = documentId ?? DeriveDocumentId(filePath);

        DocumentUploads.Add(new DocumentUpload
        {
            FilePath = filePath,
            DocumentId = effectiveDocumentId,
            Description = description
        });

        return this;
    }

    /// <summary>
    /// Upload a document directly from URL.
    /// Document is automatically downloaded and extracted at skill activation.
    /// TextExtractionUtility handles the download and text extraction.
    /// </summary>
    /// <param name="url">URL to document (must be http or https)</param>
    /// <param name="description">
    /// Document description (REQUIRED).
    /// This description helps the agent understand what the document contains and decide whether to read it.
    /// Be specific about the content (e.g., "Company policy documentation" instead of just "Policy").
    /// </param>
    /// <param name="documentId">Document ID (optional, auto-derived from URL if null)</param>
    /// <exception cref="ArgumentException">Thrown if url or description is empty</exception>
    public SkillOptions AddDocumentFromUrl(
        string url,
        string description,
        string? documentId = null)
    {
        if (string.IsNullOrWhiteSpace(url))
            throw new ArgumentException("URL cannot be empty.", nameof(url));

        if (string.IsNullOrWhiteSpace(description))
            throw new ArgumentException(
                "Description is required when uploading documents. " +
                "It helps the agent understand what the document contains and decide whether to read it.",
                nameof(description));

        // Validate URL format
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new ArgumentException(
                "URL must be a valid HTTP or HTTPS URL.",
                nameof(url));
        }

        // Auto-derive document ID from URL if not provided
        var effectiveDocumentId = documentId ?? DeriveDocumentIdFromUrl(url);

        DocumentUploads.Add(new DocumentUpload
        {
            Url = url,
            DocumentId = effectiveDocumentId,
            Description = description
        });

        return this;
    }

    private static string DeriveDocumentId(string filePath)
    {
        // "./docs/debugging-workflow.md" -> "debugging-workflow"
        var fileName = Path.GetFileNameWithoutExtension(filePath);

        // Normalize to lowercase-kebab-case
        return fileName.ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("_", "-");
    }

    private static string DeriveDocumentIdFromUrl(string url)
    {
        // "https://docs.company.com/sops/financial-health.md" -> "financial-health"
        var uri = new Uri(url);
        var fileName = Path.GetFileNameWithoutExtension(uri.LocalPath);

        if (string.IsNullOrWhiteSpace(fileName))
        {
            // If no filename, use host: "https://example.com" -> "example-com"
            fileName = uri.Host.Replace(".", "-");
        }

        // Normalize to lowercase-kebab-case
        return fileName.ToLowerInvariant()
            .Replace(" ", "-")
            .Replace("_", "-");
    }
}

/// <summary>
/// Reference to a document in the instruction store
/// </summary>
public record DocumentReference
{
    public required string DocumentId { get; init; }
    public string? DescriptionOverride { get; init; }  // null = use default from store
}

/// <summary>
/// Document to be uploaded from file path or URL at application startup
/// </summary>
public record DocumentUpload
{
    /// <summary>
    /// File path (for file-based documents). Mutually exclusive with Url.
    /// </summary>
    public string? FilePath { get; init; }

    /// <summary>
    /// URL (for URL-based documents). Mutually exclusive with FilePath.
    /// </summary>
    public string? Url { get; init; }

    public required string DocumentId { get; init; }
    public required string Description { get; init; }

    /// <summary>
    /// Returns true if this is a file-based document
    /// </summary>
    public bool IsFilePath => FilePath != null;

    /// <summary>
    /// Returns true if this is a URL-based document
    /// </summary>
    public bool IsUrl => Url != null;
}

