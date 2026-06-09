namespace HPD.TextExtract.Models
{
    public enum ExtractionProfile
    {
        Fast,
        Balanced,
        Deep,
        Citation
    }

    public sealed class ExtractionOptions
    {
        public static ExtractionOptions Default { get; } = new();

        public ExtractionProfile Profile { get; init; } = ExtractionProfile.Balanced;
        public int MaxPages { get; init; } = 1000;
        public string? TargetPages { get; init; }
        public string? Password { get; init; }
        public bool IncludeTextItems { get; init; } = true;
        public bool IncludeScreenshots { get; init; }
        public bool IncludeEmbeddedImages { get; init; }
        public bool OcrEnabled { get; init; } = true;
        public Uri? OcrEndpoint { get; init; }
        public string OcrLanguage { get; init; } = "eng";
        public float Dpi { get; init; } = 150f;
    }
}
