using HPD.Extract.Models;

namespace HPD.Extract.Pdf
{
    public sealed class PdfExtractionOptions
    {
        public ExtractionProfile Profile { get; init; } = ExtractionProfile.Balanced;
        public int MaxPages { get; init; } = 1000;
        public string? TargetPages { get; init; }
        public string? Password { get; init; }
        public bool NativeTextEnabled { get; init; } = true;
        public bool OcrEnabled { get; init; } = true;
        public bool IncludeTextItems { get; init; } = true;
        public bool IncludeScreenshots { get; init; }
        public bool IncludeEmbeddedImages { get; init; }
        public PdfRenderImageFormat ScreenshotFormat { get; init; } = PdfRenderImageFormat.Png;
        public Uri? OcrEndpoint { get; init; }
        public string OcrLanguage { get; init; } = "eng";
        public float Dpi { get; init; } = 150f;

        public static PdfExtractionOptions FromExtractionOptions(ExtractionOptions options) =>
            new()
            {
                Profile = options.Profile,
                MaxPages = options.MaxPages,
                TargetPages = options.TargetPages,
                Password = options.Password,
                OcrEnabled = options.OcrEnabled,
                IncludeTextItems = options.IncludeTextItems,
                IncludeScreenshots = options.IncludeScreenshots,
                IncludeEmbeddedImages = options.IncludeEmbeddedImages,
                OcrEndpoint = options.OcrEndpoint,
                OcrLanguage = options.OcrLanguage,
                Dpi = options.Dpi
            };
    }
}
