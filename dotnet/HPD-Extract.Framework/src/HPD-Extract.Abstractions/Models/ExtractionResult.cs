using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json.Serialization;

namespace HPD.Extract.Models
{
    public enum TextExtractionSource
    {
        Native,
        Ocr,
        Derived,
        Unknown
    }

    public enum ExtractedAssetKind
    {
        PageScreenshot,
        EmbeddedImage,
        Attachment,
        Other
    }

    public sealed class ExtractionResult
    {
        public FileContent Content { get; init; }
        public IReadOnlyList<ExtractedPage> Pages { get; init; }
        public IReadOnlyList<ExtractedAsset> Assets { get; init; }
        public ExtractionDiagnostics Diagnostics { get; init; }
        public Dictionary<string, object?> Metadata { get; init; }
        public object? RichResult { get; init; }

        public ExtractionResult(FileContent content)
        {
            Content = content;
            Pages = Array.Empty<ExtractedPage>();
            Assets = Array.Empty<ExtractedAsset>();
            Diagnostics = new ExtractionDiagnostics();
            Metadata = new Dictionary<string, object?>();
        }
    }

    public sealed class ExtractedPage
    {
        public int Number { get; init; }
        public PageSize Size { get; init; }
        public string Text { get; init; } = string.Empty;
        public IReadOnlyList<ExtractedTextItem> TextItems { get; init; } = Array.Empty<ExtractedTextItem>();
        public Dictionary<string, object?> Metadata { get; init; } = new();
    }

    public sealed class ExtractedTextItem
    {
        public string Text { get; init; } = string.Empty;
        public BoundingBox BoundingBox { get; init; }
        public float Rotation { get; init; }
        public string? FontName { get; init; }
        public float? FontSize { get; init; }
        public float? Confidence { get; init; }
        public TextExtractionSource Source { get; init; } = TextExtractionSource.Unknown;
        public Dictionary<string, object?> Metadata { get; init; } = new();
    }

    public sealed class ExtractedAsset
    {
        public ExtractedAssetKind Kind { get; init; }
        public string? Name { get; init; }
        public string? MimeType { get; init; }
        public int? PageNumber { get; init; }
        public BoundingBox? BoundingBox { get; init; }
        public ReadOnlyMemory<byte> Data { get; init; }
        public Dictionary<string, object?> Metadata { get; init; } = new();
    }

    public sealed class ExtractionDiagnostics
    {
        public bool OcrPlanned { get; set; }
        public bool OcrRendered { get; set; }
        public bool OcrAttempted { get; set; }
        public bool OcrSucceeded { get; set; }
        public bool OcrUsed { get; set; }
        public bool OcrFailed { get; set; }
        public int OcrCandidatePageCount { get; set; }
        public int OcrRenderedPageCount { get; set; }
        public int OcrRenderFailedPageCount { get; set; }
        public int OcrAttemptedPageCount { get; set; }
        public int OcrSucceededPageCount { get; set; }
        public int OcrUsedPageCount { get; set; }
        public int OcrFailedPageCount { get; set; }
        public int OcrStrictRequiredPageCount { get; set; }
        public int OcrStrictFailurePageCount { get; set; }
        public bool HasWarnings => Warnings.Count > 0;
        public List<string> Warnings { get; } = new();
        public List<ExtractionTiming> Timings { get; } = new();
        public Dictionary<string, object?> Metrics { get; } = new();
    }

    public readonly record struct ExtractionTiming(
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("elapsed")] TimeSpan Elapsed)
    {
        public static ExtractionTiming FromStopwatch(string name, Stopwatch stopwatch) =>
            new(name, stopwatch.Elapsed);
    }
}
