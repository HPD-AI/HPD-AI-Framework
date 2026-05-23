namespace HPD.ML.DeepLearning.Backends;

public interface IDeepLearningTrainer
{
    NeuralNetworkParameters Train(
        NeuralNetworkDefinition definition,
        float[][] features,
        float[][] labels,
        TrainingOptions options,
        int seed);
}
