namespace HPD.ML.DeepLearning.Backends;

using global::HPD.ML.Backends.Mlx;
using global::HPD.ML.Backends.Mlx.Training;
using HPD.ML.Abstractions;

public sealed class MlxDeepLearningBackendProvider : IDeepLearningBackendProvider
{
    private readonly MlxRuntimeOptions? _options;

    public MlxDeepLearningBackendProvider(MlxRuntimeOptions? options = null)
    {
        _options = options;
    }

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
    {
        ArgumentNullException.ThrowIfNull(context);
        var backend = MlxFloatBackend.Create(OptionsFor(context.Backend));
        return new DisposableDeepLearningTrainer(
            new HeliumTrainableNeuralNetworkTrainer<MlxFloatTensor, MlxFloatTensorVar, MlxTensorTape>(
                new MlxTrainableBackend(backend)),
            backend);
    }

    private MlxRuntimeOptions? OptionsFor(BackendSpec backend)
    {
        var options = _options ?? new MlxRuntimeOptions();
        if (!string.IsNullOrWhiteSpace(backend.Device))
        {
            options = options with
            {
                Device = string.Equals(backend.Device, "cpu", StringComparison.OrdinalIgnoreCase)
                    ? MlxDeviceKind.Cpu
                    : MlxDeviceKind.Gpu
            };
        }

        if (backend.Options is null)
            return options;

        if (backend.Options.TryGetValue("nativeLibraryPath", out var nativeLibraryPath))
            options = options with { NativeLibraryPath = nativeLibraryPath };
        if (backend.Options.TryGetValue("searchRoot", out var searchRoot))
            options = options with { SearchRoot = searchRoot };
        if (backend.Options.TryGetValue("allowCpuFallback", out var allowCpuFallback)
            && bool.TryParse(allowCpuFallback, out var parsedFallback))
            options = options with { AllowCpuFallback = parsedFallback };

        return options;
    }
}
