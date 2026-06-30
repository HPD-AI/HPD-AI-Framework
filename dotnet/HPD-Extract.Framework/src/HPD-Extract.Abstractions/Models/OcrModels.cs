using System;
using System.Collections.Generic;

namespace HPD.Extract.Models
{
    public enum OcrCoordinateSpace
    {
        Unknown,
        PdfPointsTopLeft,
        RenderPixelsTopLeft,
        NormalizedTopLeft
    }

    public readonly record struct ImageFrame(
        ReadOnlyMemory<byte> Data,
        int Width,
        int Height,
        string MimeType);

    public sealed class OcrOptions
    {
        public string Language { get; init; } = "eng";
        public float MinimumConfidence { get; init; } = 0.1f;
        public Dictionary<string, object?> Metadata { get; init; } = new();
    }

    public sealed class OcrTextRegion
    {
        public string Text { get; init; } = string.Empty;
        public BoundingBox BoundingBox { get; init; }
        public OcrCoordinateSpace CoordinateSpace { get; init; } = OcrCoordinateSpace.Unknown;
        public float Confidence { get; init; }
        public string? Language { get; init; }
        public Dictionary<string, object?> Metadata { get; init; } = new();
    }
}
