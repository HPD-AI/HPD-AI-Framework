namespace HPD.ML.TimeSeries;

using HPD.ML.Abstractions;

public static class TimeSeriesExtensions
{
    extension(ILearner)
    {
        public static ILearner SsaSpikeDetection(
            string inputColumn = "Value",
            SsaAnomalyOptions? options = null)
            => new SsaAnomalyLearner(inputColumn, options ?? new SsaAnomalyOptions { IsChangePoint = false });

        public static ILearner SsaChangePointDetection(
            string inputColumn = "Value",
            SsaAnomalyOptions? options = null)
            => new SsaAnomalyLearner(inputColumn, options ?? new SsaAnomalyOptions { IsChangePoint = true });

        public static ILearner SsaForecasting(
            string inputColumn = "Value",
            SsaForecastOptions? options = null)
            => new SsaForecastingLearner(inputColumn, options);
    }
}
