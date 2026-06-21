using System;
using System.Collections.Generic;
using HPD.TextExtract.Models;

namespace HPD.TextExtract.Pdf
{
    public enum PdfTextLayerKind
    {
        Native,
        InvisibleOcrLayer,
        Ocr,
        Projected
    }

    public enum PdfOcrDecisionReason
    {
        None,
        SparseNativeText,
        LowNativeTextCoverage,
        EmbeddedImages,
        GarbledNativeText,
        Forced
    }

    public enum PdfOcrFailurePolicy
    {
        None,
        FailIfAllOcrFails,
        BestEffortEnrichment
    }

    public sealed class PdfExtractionResult
    {
        public IReadOnlyList<PdfPage> Pages { get; init; } = Array.Empty<PdfPage>();
        public IReadOnlyList<ExtractedAsset> Assets { get; init; } = Array.Empty<ExtractedAsset>();
        public string Text { get; init; } = string.Empty;
        public ExtractionDiagnostics Diagnostics { get; init; } = new();
    }

    public sealed class PdfPage
    {
        public int Number { get; init; }
        public PageSize Size { get; init; }
        public string Text { get; init; } = string.Empty;
        public IReadOnlyList<PdfTextItem> TextItems { get; init; } = Array.Empty<PdfTextItem>();
        public IReadOnlyList<ExtractedAsset> Assets { get; init; } = Array.Empty<ExtractedAsset>();
        public PdfPageQuality Quality { get; init; } = new();
        public PdfOcrDecision OcrDecision { get; init; } = PdfOcrDecision.NoOcr;
        public Dictionary<string, object?> Metadata { get; init; } = new();
    }

    public sealed class PdfBackendCapabilities
    {
        public string Name { get; init; } = "unknown";
        public string? Version { get; init; }
        public bool CanExtractNativeText { get; init; }
        public bool CanExtractGlyphBounds { get; init; }
        public bool CanExtractImageRegions { get; init; }
        public bool CanExtractEmbeddedImages { get; init; }
        public bool CanRenderPages { get; init; }
        public bool CanReportFontMetadata { get; init; }
        public bool CanReportMarkedContent { get; init; }
        public bool CanReportTextRenderMode { get; init; }
        public bool CanReportTextColors { get; init; }
        public IReadOnlySet<PdfRenderImageFormat> SupportedRenderFormats { get; init; } =
            new HashSet<PdfRenderImageFormat>();
        public Dictionary<string, object?> Metadata { get; init; } = new();
    }

    public sealed class PdfPageSnapshot
    {
        public int Number { get; init; }
        public PageSize Size { get; init; }
        public int Rotation { get; init; }
        public IReadOnlyList<PdfTextItem> NativeTextItems { get; init; } = Array.Empty<PdfTextItem>();
        public IReadOnlyList<PdfImageRegion> ImageRegions { get; init; } = Array.Empty<PdfImageRegion>();
        public IReadOnlyList<ExtractedAsset> Assets { get; init; } = Array.Empty<ExtractedAsset>();
        public Dictionary<string, object?> Metadata { get; init; } = new();
    }

    public sealed class PdfTextItem
    {
        public string Text { get; init; } = string.Empty;
        public BoundingBox BoundingBox { get; init; }
        public float Rotation { get; init; }
        public PdfTextLayerKind Layer { get; init; } = PdfTextLayerKind.Native;
        public PdfFontInfo? Font { get; init; }
        public float? TextWidth { get; init; }
        public bool? HasUnicodeMapError { get; init; }
        public float? Confidence { get; init; }
        public int? MarkedContentId { get; init; }
        public string? RenderMode { get; init; }
        public string? FillColorArgb { get; init; }
        public string? StrokeColorArgb { get; init; }
        public Dictionary<string, object?> Metadata { get; init; } = new();
    }

    public sealed class PdfFontInfo
    {
        public string? Name { get; init; }
        public string? BaseName { get; init; }
        public string? FamilyName { get; init; }
        public float? Size { get; init; }
        public float? Height { get; init; }
        public float? Ascent { get; init; }
        public float? Descent { get; init; }
        public int? Weight { get; init; }
        public int? Flags { get; init; }
        public bool? IsEmbedded { get; init; }
        public bool LooksCorrupt { get; init; }
        public Dictionary<string, object?> Metadata { get; init; } = new();
    }

    public sealed class PdfPageQuality
    {
        public int NativeTextLength { get; init; }
        public int NonGarbledNativeTextLength { get; init; }
        public int CorruptNativeTextLength { get; init; }
        public int UnicodeMapErrorTextLength { get; init; }
        public float NativeTextCoverage { get; init; }
        public float InvisibleTextRatio { get; init; }
        public float GarbledScore { get; init; }
        public int EmbeddedImageCount { get; init; }
        public int OcrRelevantImageCount { get; init; }
        public bool HasEmbeddedImages { get; init; }
        public bool HasOcrRelevantImages { get; init; }
        public bool LooksScanned { get; init; }
        public bool LooksGarbled { get; init; }
        public bool NeedsOcr { get; init; }
    }

    public sealed class PdfOcrDecision
    {
        public static PdfOcrDecision NoOcr { get; } = new();

        public bool ShouldRun { get; init; }
        public IReadOnlyList<PdfOcrDecisionReason> Reasons { get; init; } = Array.Empty<PdfOcrDecisionReason>();
        public PdfOcrFailurePolicy FailurePolicy { get; init; }
    }

    public sealed class PdfTextMatch
    {
        public int PageNumber { get; init; }
        public string Text { get; init; } = string.Empty;
        public BoundingBox BoundingBox { get; init; }
        public IReadOnlyList<PdfTextItem> Items { get; init; } = Array.Empty<PdfTextItem>();
    }

    public sealed class PdfPipelineContext
    {
        public required PdfExtractionOptions Options { get; init; }
        public required ExtractionDiagnostics Diagnostics { get; init; }
        public CancellationToken CancellationToken { get; init; }
        public Dictionary<string, object?> Metadata { get; init; } = new();
    }

    public sealed class PdfImageRegion
    {
        public BoundingBox BoundingBox { get; init; }
        public int? WidthInSamples { get; init; }
        public int? HeightInSamples { get; init; }
        public int? BitsPerComponent { get; init; }
        public bool? IsInline { get; init; }
        public float PageCoverage { get; init; }
        public bool IsOcrRelevant { get; init; }
        public Dictionary<string, object?> Metadata { get; init; } = new();
    }

    public sealed class PdfPageQualityInput
    {
        public PageSize PageSize { get; init; }
        public IReadOnlyList<PdfTextItem> TextItems { get; init; } = Array.Empty<PdfTextItem>();
        public IReadOnlyList<PdfImageRegion> ImageRegions { get; init; } = Array.Empty<PdfImageRegion>();
    }

    public sealed class PdfLayoutProjectionInput
    {
        public int PageNumber { get; init; }
        public PageSize PageSize { get; init; }
        public int Rotation { get; init; }
        public IReadOnlyList<PdfTextItem> TextItems { get; init; } = Array.Empty<PdfTextItem>();
        public PdfExtractionOptions Options { get; init; } = new();
    }

    public sealed class PdfLayoutProjectionResult
    {
        public string Text { get; init; } = string.Empty;
        public IReadOnlyList<PdfTextItem> ProjectedItems { get; init; } = Array.Empty<PdfTextItem>();
        public Dictionary<string, object?> Metrics { get; init; } = new();
    }

    public sealed class PdfRenderedPage
    {
        public int PageNumber { get; init; }
        public ImageFrame Image { get; init; }
        public PdfRenderImageFormat EncodedFormat { get; init; }
        public float Dpi { get; init; }
        public PdfRenderGeometry Geometry { get; init; } = PdfRenderGeometry.Identity;
        public Dictionary<string, object?> Metadata { get; init; } = new();
    }

    public readonly record struct PdfAffineTransform(
        float A,
        float B,
        float C,
        float D,
        float E,
        float F)
    {
        public static PdfAffineTransform Identity { get; } = new(1, 0, 0, 1, 0, 0);

        public (float X, float Y) Transform(float x, float y) =>
            (A * x + B * y + E, C * x + D * y + F);

        public BoundingBox Transform(BoundingBox box)
        {
            var (x1, y1) = Transform(box.X, box.Y);
            var (x2, y2) = Transform(box.Right, box.Y);
            var (x3, y3) = Transform(box.Right, box.Bottom);
            var (x4, y4) = Transform(box.X, box.Bottom);
            var left = MathF.Min(MathF.Min(x1, x2), MathF.Min(x3, x4));
            var top = MathF.Min(MathF.Min(y1, y2), MathF.Min(y3, y4));
            var right = MathF.Max(MathF.Max(x1, x2), MathF.Max(x3, x4));
            var bottom = MathF.Max(MathF.Max(y1, y2), MathF.Max(y3, y4));
            return new BoundingBox(left, top, right - left, bottom - top);
        }

        public PdfAffineTransform Invert()
        {
            var determinant = A * D - B * C;
            if (MathF.Abs(determinant) < 0.000001f)
            {
                throw new InvalidOperationException("PDF affine transform is not invertible.");
            }

            var inverseA = D / determinant;
            var inverseB = -B / determinant;
            var inverseC = -C / determinant;
            var inverseD = A / determinant;
            return new PdfAffineTransform(
                inverseA,
                inverseB,
                inverseC,
                inverseD,
                -(inverseA * E + inverseB * F),
                -(inverseC * E + inverseD * F));
        }
    }

    public sealed class PdfRenderGeometry
    {
        public static PdfRenderGeometry Identity { get; } = new();

        public PageSize ViewportSize { get; init; }
        public int Rotation { get; init; }
        public float Dpi { get; init; }
        public int PixelWidth { get; init; }
        public int PixelHeight { get; init; }
        public PdfAffineTransform PageToViewport { get; init; } = PdfAffineTransform.Identity;
        public PdfAffineTransform ViewportToPage { get; init; } = PdfAffineTransform.Identity;
        public PdfAffineTransform ViewportToPixel { get; init; } = PdfAffineTransform.Identity;
        public PdfAffineTransform PixelToViewport { get; init; } = PdfAffineTransform.Identity;

        public BoundingBox PixelTopLeftToViewportBox(BoundingBox box) =>
            PixelToViewport.Transform(box);

        public BoundingBox NormalizedTopLeftToViewportBox(BoundingBox box) =>
            new(
                box.X * ViewportSize.Width,
                box.Y * ViewportSize.Height,
                box.Width * ViewportSize.Width,
                box.Height * ViewportSize.Height);
    }

    public enum PdfRenderImageFormat
    {
        Bmp,
        Png,
        Jpeg
    }

    public enum PdfRenderPurpose
    {
        Ocr,
        Screenshot
    }

    public enum PdfBackendFailureKind
    {
        Unknown,
        FileAccess,
        InvalidFormat,
        PasswordRequired,
        Security,
        PageLoad,
        Unsupported
    }

    public sealed class PdfBackendException : InvalidOperationException
    {
        public PdfBackendException(PdfBackendFailureKind kind, string message, int? backendErrorCode = null)
            : base(message)
        {
            Kind = kind;
            BackendErrorCode = backendErrorCode;
        }

        public PdfBackendFailureKind Kind { get; }
        public int? BackendErrorCode { get; }
    }

    public sealed class PdfPageRenderRequest
    {
        public PdfRenderPurpose Purpose { get; init; } = PdfRenderPurpose.Ocr;
        public float? Dpi { get; init; }
        public PdfRenderImageFormat Format { get; init; } = PdfRenderImageFormat.Bmp;
    }

    public sealed class PdfRenderPageResult
    {
        public int PageNumber { get; init; }
        public PdfRenderedPage? RenderedPage { get; init; }
        public Exception? Error { get; init; }
        public bool Succeeded => RenderedPage is not null && Error is null;
        public Dictionary<string, object?> Metadata { get; init; } = new();
    }

    public sealed class PdfOcrPageResult
    {
        public int PageNumber { get; init; }
        public IReadOnlyList<OcrTextRegion> Regions { get; init; } = Array.Empty<OcrTextRegion>();
        public OcrCoordinateSpace CoordinateSpace { get; init; } = OcrCoordinateSpace.RenderPixelsTopLeft;
        public Exception? Error { get; init; }
        public bool Succeeded => Error is null;
        public Dictionary<string, object?> Metadata { get; init; } = new();
    }

    public sealed class PdfOcrMergeInput
    {
        public int PageNumber { get; init; }
        public PageSize PageSize { get; init; }
        public IReadOnlyList<PdfTextItem> NativeItems { get; init; } = Array.Empty<PdfTextItem>();
        public PdfPageQuality Quality { get; init; } = new();
        public PdfOcrDecision Decision { get; init; } = PdfOcrDecision.NoOcr;
        public PdfRenderedPage? RenderedPage { get; init; }
        public PdfOcrPageResult OcrResult { get; init; } = new();
    }

    public sealed class PdfOcrMergeResult
    {
        public IReadOnlyList<PdfTextItem> TextItems { get; init; } = Array.Empty<PdfTextItem>();
        public Dictionary<string, object?> Metrics { get; init; } = new();
    }
}
