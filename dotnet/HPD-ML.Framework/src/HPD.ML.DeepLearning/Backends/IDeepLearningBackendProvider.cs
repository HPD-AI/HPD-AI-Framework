namespace HPD.ML.DeepLearning.Backends;

using HPD.ML.Abstractions;

public interface IDeepLearningBackendProvider
{
    bool CanHandle(BackendSpec backend);
    DeepLearningBackendCapabilities GetCapabilities(BackendSpec backend);
    IDeepLearningTrainer CreateTrainer(DeepLearningBackendContext context);
}
