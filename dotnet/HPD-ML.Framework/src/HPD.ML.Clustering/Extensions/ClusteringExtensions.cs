namespace HPD.ML.Clustering;

using HPD.ML.Abstractions;

public static class ClusteringExtensions
{
    extension(ILearner)
    {
        public static ILearner KMeans(
            string featureColumn = "Features",
            KMeansOptions? options = null)
            => new KMeansLearner(featureColumn, options);

        public static ILearner MiniBatchKMeans(
            string featureColumn = "Features",
            MiniBatchKMeansOptions? options = null)
            => new MiniBatchKMeansLearner(featureColumn, options);
    }
}
