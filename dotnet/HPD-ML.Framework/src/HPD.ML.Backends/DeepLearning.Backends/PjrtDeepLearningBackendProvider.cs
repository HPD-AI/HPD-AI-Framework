namespace HPD.ML.DeepLearning.Backends;

using HPD.ML.Abstractions;
using HPD.ML.DeepLearning;

public sealed class PjrtDeepLearningBackendProvider : IDeepLearningBackendProvider
{
    public bool CanHandle(BackendSpec backend)
        => string.Equals(backend.Kind, "pjrt", StringComparison.OrdinalIgnoreCase);

    public DeepLearningBackendCapabilities GetCapabilities(BackendSpec backend)
        => new()
        {
            Name = "pjrt",
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
            "PJRT backend is being migrated to Helium. Use the managed backend or await Helium.PJRT.");
}
