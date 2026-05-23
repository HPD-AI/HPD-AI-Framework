namespace HPD.ML.DeepLearning.Backends;

using HPD.ML.Abstractions;

public sealed record DeepLearningBackendContext(
    BackendSpec Backend,
    IExecutionEnvironment? Environment);
