namespace HPD.ML.Transforms;

using HPD.ML.Abstractions;

/// <summary>
/// C# 14 extension members for transform and learner discovery.
/// </summary>
public static class TransformExtensions
{
    extension(ITransform)
    {
        // ── Categorical ────────────────────────────────────────
        public static ITransform OneHotEncode(
            string columnName, IReadOnlyDictionary<string, int> mapping, string? outputColumn = null)
            => new OneHotEncodeTransform(columnName, mapping, outputColumn);

        public static ITransform ValueToKey(
            string columnName, IReadOnlyDictionary<string, int> mapping, string? outputColumn = null)
            => new ValueToKeyTransform(columnName, mapping, outputColumn);

        public static ITransform KeyToValue(
            string columnName, IReadOnlyList<string> keyValues, string? outputColumn = null)
            => new KeyToValueTransform(columnName, keyValues, outputColumn);

        // ── Missing Values ─────────────────────────────────────
        public static ITransform ReplaceMissing(
            string columnName, ReplacementValue replacement)
            => new MissingValueReplaceTransform(columnName, replacement);

        public static ITransform IndicateMissing(
            string columnName, string? indicatorColumn = null)
            => new MissingValueIndicateTransform(columnName, indicatorColumn);

        public static ITransform DropMissing(params string[] columnNames)
            => new MissingValueDropTransform(columnNames);

        // ── Conversion ─────────────────────────────────────────
        public static ITransform Hash(string columnName, int numBits = 16, string? outputColumn = null)
            => new HashTransform(columnName, numBits, outputColumnName: outputColumn);

        public static ITransform ConvertType(string columnName, Type targetType)
            => new TypeConvertTransform(columnName, targetType);

        // ── Image ──────────────────────────────────────────────
        public static ITransform LoadImage(string pathColumn, string outputColumn = "Image")
            => new ImageLoadTransform(pathColumn, outputColumn);

        public static ITransform ResizeImage(
            string imageColumn, int width, int height, ResizeMode mode = ResizeMode.ScaleToFit)
            => new ImageResizeTransform(imageColumn, width, height, mode);

        public static ITransform ExtractPixels(
            string imageColumn, int channels, int height, int width,
            bool normalize = true, string outputColumn = "Pixels")
            => new ImagePixelExtractTransform(imageColumn, channels, height, width, normalize, outputColumn);
    }

    extension(ILearner)
    {
        // ── Normalization Learners ─────────────────────────────
        public static ILearner MinMaxNormalize(
            string columnName, float scaleMin = 0f, float scaleMax = 1f, string? outputColumn = null)
            => new MinMaxNormalizeLearner(columnName, scaleMin, scaleMax, outputColumn);

        public static ILearner MeanVarianceNormalize(
            string columnName, string? outputColumn = null)
            => new MeanVarianceNormalizeLearner(columnName, outputColumn);

        public static ILearner BinNormalize(
            string columnName, int numBins = 10, string? outputColumn = null)
            => new BinNormalizeLearner(columnName, numBins, outputColumn);

        // ── Categorical Learners ───────────────────────────────
        public static ILearner OneHotEncode(
            string columnName, int maxCategories = 1000, string? outputColumn = null)
            => new OneHotEncodeLearner(columnName, maxCategories, outputColumn);

        // ── Missing Value Learners ─────────────────────────────
        public static ILearner ReplaceMissing(
            string columnName, ReplacementStrategy strategy = ReplacementStrategy.Mean)
            => new MissingValueReplaceLearner(columnName, strategy);

        // ── Text Learners ──────────────────────────────────────
        public static ILearner TextFeaturize(
            string columnName, string? outputColumn = null, TextFeaturizeOptions? options = null)
            => new TextFeaturizeLearner(columnName, outputColumn, options);

        // ── Feature Selection Learners ──────────────────────────
        public static ILearner MutualInfoFeatureSelection(
            string labelColumn, string[] featureColumns, int topK = 10, int numBins = 32)
            => new MutualInfoFeatureSelectionLearner(labelColumn, featureColumns, topK, numBins);
    }
}
