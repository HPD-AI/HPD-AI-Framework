namespace HPD.ML.Regression;

using HPD.ML.Abstractions;

public static class RegressionExtensions
{
    extension(ILearner)
    {
        public static ILearner OrdinaryLeastSquares(
            string labelColumn = "Label",
            string featureColumn = "Features",
            OlsOptions? options = null)
            => new OrdinaryLeastSquaresLearner(labelColumn, featureColumn, options);

        public static ILearner SdcaRegression(
            string labelColumn = "Label",
            string featureColumn = "Features",
            SdcaRegressionOptions? options = null)
            => new SdcaRegressionLearner(labelColumn, featureColumn, options);

        public static ILearner OnlineGradientDescent(
            string labelColumn = "Label",
            string featureColumn = "Features",
            OnlineGradientDescentOptions? options = null)
            => new OnlineGradientDescentLearner(labelColumn, featureColumn, options);

        public static ILearner PoissonRegression(
            string labelColumn = "Label",
            string featureColumn = "Features",
            PoissonRegressionOptions? options = null)
            => new PoissonRegressionLearner(labelColumn, featureColumn, options);
    }
}
