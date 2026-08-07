namespace HPD.ML.DeepLearning.Backends;

using HPD.ML.DeepLearning;

public interface IDeepLearningTrainer
{
    NeuralNetworkParameters Train(
        NeuralNetworkDefinition definition,
        float[][] features,
        float[][] labels,
        TrainingOptions options,
        int seed);
}
