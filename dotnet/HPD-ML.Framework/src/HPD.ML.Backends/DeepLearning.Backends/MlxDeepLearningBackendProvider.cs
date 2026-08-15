namespace HPD.ML.DeepLearning.Backends;

using HPD.ML.Abstractions;
using HPD.ML.DeepLearning;

public sealed class MlxDeepLearningBackendProvider : IDeepLearningBackendProvider
{
    public bool CanHandle(BackendSpec backend)
        => string.Equals(backend.Kind, "mlx", StringComparison.OrdinalIgnoreCase);

    public DeepLearningBackendCapabilities GetCapabilities(BackendSpec backend)
        => new()
        {
            Name = "mlx",
            SupportsTraining = true,
            SupportsAutodiff = true,
            SupportsCpu = true,
            SupportsGpu = true,
            SupportsFloat32 = true,
            SupportedActivations = new HashSet<ActivationKind>
            {
                ActivationKind.Identity,
                ActivationKind.ReLU
            }
        };

    public IDeepLearningTrainer CreateTrainer(DeepLearningBackendContext context)
        => throw new NotSupportedException(
            "MLX backend is being migrated to Helium. Use the managed backend or await Helium.MLX.");
}
