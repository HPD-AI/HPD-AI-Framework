namespace HPD.ML.DeepLearning;

using HPD.ML.Abstractions;
using HPD.ML.Core;
using HPD.ML.DeepLearning.Backends;

public sealed class NeuralNetworkLearner : ILearner
{
    private readonly NeuralNetworkDefinition _definition;
    private readonly TrainingOptions _options;
    private readonly IReadOnlyList<IDeepLearningBackendProvider> _backendProviders;
    private readonly ProgressSubject _progress = new();

    public NeuralNetworkLearner(
        NeuralNetworkDefinition definition,
        TrainingOptions? options = null,
        IEnumerable<IDeepLearningBackendProvider>? backendProviders = null)
    {
        _definition = definition ?? throw new ArgumentNullException(nameof(definition));
        _options = options ?? new TrainingOptions();
        _backendProviders = backendProviders?.ToArray() ?? [new ManagedDeepLearningBackendProvider()];
        if (_backendProviders.Count == 0)
            throw new ArgumentException("At least one deep learning backend provider is required.", nameof(backendProviders));
    }

    public IObservable<ProgressEvent> Progress => _progress;

    public ISchema GetOutputSchema(ISchema inputSchema)
        => new NeuralNetworkScoringTransform(
            new NeuralNetworkParameters(
                _definition,
                _definition.Layers.Select(layer => new float[layer.InputSize * layer.OutputSize]).ToArray(),
                _definition.Layers.Select(layer => new float[layer.OutputSize]).ToArray()))
            .GetOutputSchema(inputSchema);

    public IModel Fit(LearnerInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var (features, labels) = TrainingDataLoader.Load(_definition, input.TrainData);
        var seed = input.Environment?.Seed ?? 0;
        var backend = input.Environment?.Backend ?? BackendSpec.Default();
        var provider = ResolveProvider(backend);
        DeepLearningBackendCompatibility.Validate(_definition, provider.GetCapabilities(backend));
        var trainer = provider.CreateTrainer(new DeepLearningBackendContext(backend, input.Environment));
        NeuralNetworkParameters parameters;
        try
        {
            parameters = trainer.Train(_definition, features, labels, _options, seed);
        }
        finally
        {
            if (trainer is IDisposable disposable)
                disposable.Dispose();
        }

        var transform = new NeuralNetworkScoringTransform(parameters);
        _progress.OnCompleted();
        return new Model(transform, parameters);
    }

    public Task<IModel> FitAsync(LearnerInput input, CancellationToken ct = default)
        => Task.Run(() => Fit(input), ct);

    private IDeepLearningBackendProvider ResolveProvider(BackendSpec backend)
    {
        foreach (var provider in _backendProviders)
        {
            if (provider.CanHandle(backend))
                return provider;
        }

        throw new InvalidOperationException(
            $"No deep learning backend provider is registered for backend '{backend}'.");
    }
}
