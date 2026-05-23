namespace HPD.ML.DeepLearning.Tests;

using HPD.ML.Abstractions;
using HPD.ML.Core;
using HPD.ML.DeepLearning;
using HPD.ML.DeepLearning.Backends;

public sealed class NeuralNetworkLearnerTests
{
    [Fact]
    public void Fit_LinearRegression_ReturnsModelThatScores()
    {
        var data = LinearData(n: 40);
        var definition = new NeuralNetworkDefinition(
            "Features",
            "Label",
            [new DenseLayerSpec(1, 1)]);
        var learner = new NeuralNetworkLearner(
            definition,
            new TrainingOptions { Epochs = 160, LearningRate = 0.03f, BatchSize = 8 });
        var env = new DefaultExecutionEnvironment(seed: 7, backend: BackendSpec.Cpu());

        var model = learner.Fit(new LearnerInput(data, Environment: env));
        var predictions = model.Transform.Apply(data);

        Assert.IsType<NeuralNetworkParameters>(model.Parameters);
        Assert.True(Mse(predictions) < 0.05);
    }

    [Fact]
    public void Fit_UsesEnvironmentSeedForDeterministicInitialization()
    {
        var data = LinearData(n: 16);
        var definition = new NeuralNetworkDefinition(
            "Features",
            "Label",
            [new DenseLayerSpec(1, 1)]);
        var learner = new NeuralNetworkLearner(
            definition,
            new TrainingOptions { Epochs = 4, LearningRate = 0.01f });
        var env = new DefaultExecutionEnvironment(seed: 123, backend: BackendSpec.Cpu());

        var first = (NeuralNetworkParameters)learner.Fit(new LearnerInput(data, Environment: env)).Parameters;
        var second = (NeuralNetworkParameters)learner.Fit(new LearnerInput(data, Environment: env)).Parameters;

        Assert.Equal(first.Weights[0], second.Weights[0]);
        Assert.Equal(first.Biases[0], second.Biases[0]);
    }

    [Fact]
    public void Fit_UsesRegisteredProviderForRequestedBackend()
    {
        var data = LinearData(n: 4);
        var definition = new NeuralNetworkDefinition(
            "Features",
            "Label",
            [new DenseLayerSpec(1, 1)]);
        var provider = new CapturingBackendProvider("custom", new NeuralNetworkParameters(definition, [[42.0f]], [[3.0f]]));
        var learner = new NeuralNetworkLearner(definition, backendProviders: [provider]);
        var env = new DefaultExecutionEnvironment(seed: 9, backend: new BackendSpec("custom"));

        var model = learner.Fit(new LearnerInput(data, Environment: env));
        var parameters = Assert.IsType<NeuralNetworkParameters>(model.Parameters);

        Assert.True(provider.WasCreated);
        Assert.True(provider.Trainer.WasCalled);
        Assert.Equal("custom", provider.Context?.Backend.Kind);
        Assert.Equal([42.0f], parameters.Weights[0]);
        Assert.Equal([3.0f], parameters.Biases[0]);
    }

    [Fact]
    public void Fit_ThrowsForExplicitBackendWithoutRegisteredProvider()
    {
        var data = LinearData(n: 4);
        var definition = new NeuralNetworkDefinition(
            "Features",
            "Label",
            [new DenseLayerSpec(1, 1)]);
        var learner = new NeuralNetworkLearner(definition);
        var env = new DefaultExecutionEnvironment(seed: 9, backend: BackendSpec.Mlx());

        var ex = Assert.Throws<InvalidOperationException>(() =>
            learner.Fit(new LearnerInput(data, Environment: env)));
        Assert.Contains("mlx:gpu", ex.Message);
    }

    [Fact]
    public void Fit_ThrowsBeforeTrainerCreationWhenBackendCannotTrain()
    {
        var data = LinearData(n: 4);
        var definition = new NeuralNetworkDefinition(
            "Features",
            "Label",
            [new DenseLayerSpec(1, 1)]);
        var provider = new CapturingBackendProvider(
            "custom",
            new NeuralNetworkParameters(definition, [[42.0f]], [[3.0f]]),
            capabilities: new DeepLearningBackendCapabilities
            {
                Name = "custom",
                SupportsTraining = false,
                SupportsFloat32 = true,
                SupportedActivations = new HashSet<ActivationKind> { ActivationKind.Identity }
            });
        var learner = new NeuralNetworkLearner(definition, backendProviders: [provider]);
        var env = new DefaultExecutionEnvironment(seed: 9, backend: new BackendSpec("custom"));

        var ex = Assert.Throws<NotSupportedException>(() =>
            learner.Fit(new LearnerInput(data, Environment: env)));

        Assert.False(provider.WasCreated);
        Assert.Contains("does not support training", ex.Message);
    }

    [Fact]
    public void Definition_RejectsDisconnectedLayers()
    {
        Assert.Throws<ArgumentException>(() =>
            new NeuralNetworkDefinition(
                "Features",
                "Label",
                [
                    new DenseLayerSpec(2, 3),
                    new DenseLayerSpec(4, 1)
                ]));
    }

    private static IDataHandle LinearData(int n)
    {
        var features = new float[n][];
        var labels = new float[n];
        for (var i = 0; i < n; i++)
        {
            var x = -1.0f + i * (2.0f / Math.Max(1, n - 1));
            features[i] = [x];
            labels[i] = 2.0f * x + 1.0f;
        }

        return InMemoryDataHandle.FromColumns(("Features", features), ("Label", labels));
    }

    private static double Mse(IDataHandle predictions)
    {
        var sum = 0.0;
        var count = 0;
        using var cursor = predictions.GetCursor(["Score", "Label"]);
        while (cursor.MoveNext())
        {
            var score = cursor.Current.GetValue<float>("Score");
            var label = cursor.Current.GetValue<float>("Label");
            var diff = score - label;
            sum += diff * diff;
            count++;
        }

        return sum / count;
    }

    private sealed class CapturingBackendProvider : IDeepLearningBackendProvider
    {
        private readonly string _kind;
        private readonly DeepLearningBackendCapabilities _capabilities;

        public CapturingBackendProvider(
            string kind,
            NeuralNetworkParameters parameters,
            DeepLearningBackendCapabilities? capabilities = null)
        {
            _kind = kind;
            Trainer = new CapturingTrainer(parameters);
            _capabilities = capabilities ?? new DeepLearningBackendCapabilities
            {
                Name = kind,
                SupportsTraining = true,
                SupportsAutodiff = true,
                SupportsCpu = true,
                SupportsFloat32 = true,
                SupportedActivations = new HashSet<ActivationKind> { ActivationKind.Identity }
            };
        }

        public CapturingTrainer Trainer { get; }
        public bool WasCreated { get; private set; }
        public DeepLearningBackendContext? Context { get; private set; }

        public bool CanHandle(BackendSpec backend)
            => string.Equals(backend.Kind, _kind, StringComparison.OrdinalIgnoreCase);

        public DeepLearningBackendCapabilities GetCapabilities(BackendSpec backend)
            => _capabilities;

        public IDeepLearningTrainer CreateTrainer(DeepLearningBackendContext context)
        {
            WasCreated = true;
            Context = context;
            return Trainer;
        }
    }

    private sealed class CapturingTrainer(NeuralNetworkParameters parameters) : IDeepLearningTrainer
    {
        public bool WasCalled { get; private set; }

        public NeuralNetworkParameters Train(
            NeuralNetworkDefinition definition,
            float[][] features,
            float[][] labels,
            TrainingOptions options,
            int seed)
        {
            WasCalled = true;
            Assert.NotEmpty(features);
            Assert.Equal(features.Length, labels.Length);
            Assert.Same(definition, parameters.Definition);
            return parameters;
        }
    }
}
