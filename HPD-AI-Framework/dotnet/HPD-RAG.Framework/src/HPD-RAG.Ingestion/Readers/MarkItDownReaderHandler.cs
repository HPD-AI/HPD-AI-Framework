using HPD.RAG.Core.Context;
using HPD.RAG.Core.DTOs;
using HPD.RAG.Core.Pipeline;
using HPDAgent.Graph.Abstractions.Attributes;
using Microsoft.Extensions.DataIngestion;

namespace HPD.RAG.Ingestion.Readers;

/// <summary>
/// Reads documents (Office, PDF, HTML, etc.) by converting them to Markdown
/// via the MarkItDown tool, then mapping the result to <see cref="MragDocumentElementDto"/> elements.
/// </summary>
[GraphNodeHandler(NodeName = "ReadDocuments")]
public sealed partial class MarkItDownReaderHandler
{
    /// <summary>Default retry: 3 attempts, JitteredExponential, 1–30s.</summary>
    public static MragRetryPolicy DefaultRetry { get; } = new()
    {
        MaxAttempts = 3,
        InitialDelay = TimeSpan.FromSeconds(1),
        Strategy = MragBackoffStrategy.JitteredExponential,
        MaxDelay = TimeSpan.FromSeconds(30)
    };

    /// <summary>Default propagation: StopPipeline.</summary>
    public static MragErrorPropagation DefaultPropagation { get; } = MragErrorPropagation.StopPipeline;

    public async Task<Output> ExecuteAsync(
        [InputSocket(Description = "Absolute file paths to convert")] string[] FilePaths,
        MragPipelineContext context,
        CancellationToken cancellationToken = default)
    {
        var config = GetNodeConfig();

        if (FilePaths == null || FilePaths.Length == 0)
            return new Output { Documents = [] };

        var exePath = config.ExecutablePath is { Length: > 0 } p ? new System.IO.FileInfo(p) : null;
        var reader = new MarkItDownReader(exePath, config.ExtractImages);
        var documents = new List<MragDocumentDto>(FilePaths.Length);

        foreach (var path in FilePaths)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var fileInfo = new System.IO.FileInfo(path);
            var doc = await reader.ReadAsync(fileInfo, null, null, cancellationToken).ConfigureAwait(false);
            documents.Add(MapDocument(path, doc));
        }

        return new Output { Documents = documents.ToArray() };
    }

    private static MragDocumentDto MapDocument(string path, IngestionDocument doc)
    {
        var elements = new List<MragDocumentElementDto>();
        foreach (var section in doc.Sections)
        {
            foreach (var el in section.Elements)
            {
                elements.Add(new MragDocumentElementDto
                {
                    Type = el.GetType().Name.Replace("IngestionDocument", "").ToLowerInvariant(),
                    Text = el is IngestionDocumentParagraph p ? p.Text :
                           el is IngestionDocumentHeader h ? h.Text : null,
                    HeaderLevel = el is IngestionDocumentHeader hdr ? (int?)hdr.Level : null,
                    AlternativeText = el is IngestionDocumentImage img ? img.AlternativeText : null
                });
            }
        }

        return new MragDocumentDto
        {
            Id = path,
            Elements = elements.ToArray()
        };
    }

    public sealed class Config
    {
        /// <summary>Include image elements in output. Default: false.</summary>
        public bool ExtractImages { get; set; } = false;

        /// <summary>Path to the MarkItDown executable. Null = resolve from PATH.</summary>
        public string? ExecutablePath { get; set; }
    }

    public sealed record Output
    {
        [OutputSocket(Description = "Parsed document DTOs, one per input file")]
        public MragDocumentDto[] Documents { get; init; } = [];
    }
}
