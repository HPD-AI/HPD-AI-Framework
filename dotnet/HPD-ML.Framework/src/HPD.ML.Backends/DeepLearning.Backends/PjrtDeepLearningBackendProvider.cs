namespace HPD.ML.DeepLearning.Backends;

using global::HPD.ML.Backends.Pjrt;
using global::HPD.ML.Backends.Pjrt.Training;
using HPD.ML.Abstractions;

public sealed class PjrtDeepLearningBackendProvider : IDeepLearningBackendProvider
{
    private readonly PjrtPluginResolverOptions? _options;

    public PjrtDeepLearningBackendProvider(PjrtPluginResolverOptions? options = null)
    {
        _options = options;
    }

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
    {
        ArgumentNullException.ThrowIfNull(context);
        var backend = PjrtFloatBackend.Create(OptionsFor(context.Backend));
        return new DisposableDeepLearningTrainer(
            new HeliumTrainableNeuralNetworkTrainer<PjrtFloatTensor, PjrtFloatTensorVar, PjrtTensorTape>(
                new PjrtTrainableBackend(backend)),
            backend);
    }

    private PjrtPluginResolverOptions OptionsFor(BackendSpec backend)
    {
        var options = _options ?? new PjrtPluginResolverOptions();
        options = options with { Backend = string.IsNullOrWhiteSpace(backend.Device) ? options.Backend : backend.Device };

        if (backend.Options is null)
            return options;

        if (backend.Options.TryGetValue("explicitPath", out var explicitPath))
            options = options with { ExplicitPath = explicitPath };
        if (backend.Options.TryGetValue("searchRoot", out var searchRoot))
            options = options with { SearchRoot = searchRoot };

        return options;
    }
}
