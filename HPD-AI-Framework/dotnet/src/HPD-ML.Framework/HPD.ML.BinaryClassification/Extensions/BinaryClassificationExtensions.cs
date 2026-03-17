namespace HPD.ML.BinaryClassification;

using HPD.ML.Abstractions;

/// <summary>
/// C# 14 extension members for binary classification learner discovery.
/// </summary>
public static class BinaryClassificationExtensions
{
    extension(ILearner)
    {
        public static ILearner LogisticRegression(
            string labelColumn = "Label",
            string featureColumn = "Features",
            LogisticRegressionOptions? options = null)
            => new LogisticRegressionLearner(labelColumn, featureColumn, options);

        public static ILearner Sdca(
            string labelColumn = "Label",
            string featureColumn = "Features",
            SdcaOptions? options = null)
            => new SdcaLearner(labelColumn, featureColumn, options);

        public static ILearner AveragedPerceptron(
            string labelColumn = "Label",
            string featureColumn = "Features",
            AveragedPerceptronOptions? options = null)
            => new AveragedPerceptronLearner(labelColumn, featureColumn, options);

        public static ILearner LinearSvm(
            string labelColumn = "Label",
            string featureColumn = "Features",
            LinearSvmOptions? options = null)
            => new LinearSvmLearner(labelColumn, featureColumn, options);
    }
}
