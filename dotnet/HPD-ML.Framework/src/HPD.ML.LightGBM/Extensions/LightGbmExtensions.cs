namespace HPD.ML.LightGBM;

using HPD.ML.Abstractions;

/// <summary>
/// C# 14 extension members for discoverable LightGBM API.
/// </summary>
public static class LightGbmExtensions
{
    extension(ILearner)
    {
        public static ILearner LightGbm(
            string labelColumn = "Label",
            string featureColumn = "Features",
            LightGbmOptions? options = null)
            => new LightGbmLearner(labelColumn, featureColumn, options);

        public static ILearner LightGbmBinaryClassification(
            string labelColumn = "Label",
            string featureColumn = "Features",
            LightGbmOptions? options = null)
            => new LightGbmLearner(labelColumn, featureColumn,
                (options ?? new LightGbmOptions()) with { Objective = LightGbmObjective.Binary });

        public static ILearner LightGbmRegression(
            string labelColumn = "Label",
            string featureColumn = "Features",
            LightGbmOptions? options = null)
            => new LightGbmLearner(labelColumn, featureColumn,
                (options ?? new LightGbmOptions()) with { Objective = LightGbmObjective.Regression });

        public static ILearner LightGbmMulticlass(
            int numberOfClasses,
            string labelColumn = "Label",
            string featureColumn = "Features",
            LightGbmOptions? options = null)
            => new LightGbmLearner(labelColumn, featureColumn,
                (options ?? new LightGbmOptions()) with
                {
                    Objective = LightGbmObjective.Multiclass,
                    NumberOfClasses = numberOfClasses
                });

        public static ILearner LightGbmRanking(
            string labelColumn = "Label",
            string featureColumn = "Features",
            LightGbmOptions? options = null)
            => new LightGbmLearner(labelColumn, featureColumn,
                (options ?? new LightGbmOptions()) with { Objective = LightGbmObjective.Ranking });
    }
}
