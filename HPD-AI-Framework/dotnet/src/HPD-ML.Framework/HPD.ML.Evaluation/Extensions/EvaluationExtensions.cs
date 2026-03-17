namespace HPD.ML.Evaluation;

using HPD.ML.Abstractions;

/// <summary>
/// C# 14 extension members for metric discovery.
/// </summary>
public static class EvaluationExtensions
{
    extension(ITransform)
    {
        public static ITransform BinaryClassificationMetrics(
            string labelColumn = "Label",
            string scoreColumn = "Score",
            double threshold = 0.5)
            => new BinaryClassificationMetricsTransform(labelColumn, scoreColumn, threshold);

        public static ITransform MulticlassMetrics(
            string labelColumn = "Label",
            string predictedLabelColumn = "PredictedLabel",
            string scoreColumn = "Score")
            => new MulticlassMetricsTransform(labelColumn, predictedLabelColumn, scoreColumn);

        public static ITransform RegressionMetrics(
            string labelColumn = "Label",
            string scoreColumn = "Score",
            int? featureCount = null)
            => new RegressionMetricsTransform(labelColumn, scoreColumn, featureCount);

        public static ITransform RankingMetrics(
            string labelColumn = "Label",
            string scoreColumn = "Score",
            string groupColumn = "GroupId",
            int[]? truncationLevels = null)
            => new RankingMetricsTransform(labelColumn, scoreColumn, groupColumn, truncationLevels);

        public static ITransform ClusteringMetrics(
            string labelColumn = "Label",
            string predictedLabelColumn = "PredictedLabel",
            string scoreColumn = "Score",
            string featuresColumn = "Features")
            => new ClusteringMetricsTransform(labelColumn, predictedLabelColumn, scoreColumn, featuresColumn);

        public static ITransform ConfusionMatrix(
            string labelColumn = "Label",
            string predictedLabelColumn = "PredictedLabel")
            => new ConfusionMatrixTransform(labelColumn, predictedLabelColumn);
    }
}
