namespace HPD.ML.DeepLearning.Backends;

using HPD.ML.Backends.Abstractions.Training;

public sealed class HeliumTrainableNeuralNetworkTrainer<TTensor, TVariable, TTape> : IDeepLearningTrainer
    where TTensor : class, IDisposable
    where TTape : IDisposable
{
    private readonly ITrainableTensorBackend<TTensor, TVariable, TTape> _backend;

    public HeliumTrainableNeuralNetworkTrainer(ITrainableTensorBackend<TTensor, TVariable, TTape> backend)
    {
        _backend = backend ?? throw new ArgumentNullException(nameof(backend));
    }

    public NeuralNetworkParameters Train(
        NeuralNetworkDefinition definition,
        float[][] features,
        float[][] labels,
        TrainingOptions options,
        int seed)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(features);
        ArgumentNullException.ThrowIfNull(labels);
        ArgumentNullException.ThrowIfNull(options);

        options.Validate();
        ValidateTrainingData(definition, features, labels);

        using var model = CreateModel(definition, seed);
        var parameters = model.Parameters.ToArray();
        var optimizer = new TrainableSgdOptimizer<TTensor, TVariable, TTape>(_backend, options.LearningRate);

        for (var epoch = 0; epoch < options.Epochs; epoch++)
        {
            for (var batchStart = 0; batchStart < features.Length; batchStart += options.BatchSize)
            {
                var batchCount = Math.Min(options.BatchSize, features.Length - batchStart);
                var inputSize = definition.Layers[0].InputSize;
                var outputSize = definition.Layers[^1].OutputSize;
                using var featureBatch = _backend.CreateMatrix(batchCount, inputSize, Flatten(features, batchStart, batchCount, inputSize));
                using var labelBatch = _backend.CreateMatrix(batchCount, outputSize, Flatten(labels, batchStart, batchCount, outputSize));

                TrainStep.Run(
                    _backend,
                    parameters,
                    optimizer,
                    (tape, watched) =>
                    {
                        var input = _backend.Watch(tape, featureBatch);
                        var target = _backend.Watch(tape, labelBatch);
                        var predicted = model.Forward(_backend, tape, input, watched);
                        return TrainableLosses.MeanSquaredError(_backend, tape, predicted, target);
                    });
            }
        }

        return MaterializeParameters(definition, model.Layers);
    }

    private TrainableNetwork CreateModel(NeuralNetworkDefinition definition, int seed)
    {
        var random = new Random(seed);
        var layers = new TrainableLayer[definition.Layers.Count];
        for (var i = 0; i < definition.Layers.Count; i++)
        {
            var spec = definition.Layers[i];
            var weights = InitializeWeights(spec.InputSize, spec.OutputSize, random);
            var bias = new float[spec.OutputSize];
            layers[i] = new TrainableLayer(_backend, spec, weights, bias, $"layer{i}");
        }

        return new TrainableNetwork(layers);
    }

    private NeuralNetworkParameters MaterializeParameters(NeuralNetworkDefinition definition, IReadOnlyList<TrainableLayer> layers)
    {
        var weights = new float[layers.Count][];
        var biases = new float[layers.Count][];
        for (var i = 0; i < layers.Count; i++)
        {
            weights[i] = _backend.ToArray(layers[i].Weight.Value);
            biases[i] = _backend.ToArray(layers[i].Bias.Value);
        }

        return new NeuralNetworkParameters(definition, weights, biases);
    }

    private static void ValidateTrainingData(NeuralNetworkDefinition definition, float[][] features, float[][] labels)
    {
        if (features.Length != labels.Length)
            throw new ArgumentException("Feature and label row counts must match.");
        if (features.Length == 0)
            throw new ArgumentException("Training data must contain at least one row.");

        var inputSize = definition.Layers[0].InputSize;
        var outputSize = definition.Layers[^1].OutputSize;
        for (var i = 0; i < features.Length; i++)
        {
            if (features[i].Length != inputSize)
                throw new ArgumentException($"Feature row {i} length must be {inputSize}.", nameof(features));
            if (labels[i].Length != outputSize)
                throw new ArgumentException($"Label row {i} length must be {outputSize}.", nameof(labels));
        }
    }

    private static float[] Flatten(float[][] rows, int start, int count, int width)
    {
        var data = new float[checked(count * width)];
        for (var row = 0; row < count; row++)
            rows[start + row].AsSpan().CopyTo(data.AsSpan(row * width, width));
        return data;
    }

    private static float[] InitializeWeights(int inputSize, int outputSize, Random random)
    {
        var weights = new float[inputSize * outputSize];
        var scale = MathF.Sqrt(2.0f / inputSize);
        for (var i = 0; i < weights.Length; i++)
            weights[i] = ((float)random.NextDouble() * 2.0f - 1.0f) * scale;
        return weights;
    }

    private sealed class TrainableNetwork : IDisposable
    {
        private readonly TrainableLayer[] _layers;

        public TrainableNetwork(TrainableLayer[] layers)
        {
            _layers = layers;
        }

        public IReadOnlyList<TrainableLayer> Layers => _layers;

        public IEnumerable<TrainableParameter<TTensor>> Parameters
        {
            get
            {
                foreach (var layer in _layers)
                {
                    yield return layer.Weight;
                    yield return layer.Bias;
                }
            }
        }

        public TVariable Forward(
            ITrainableTensorBackend<TTensor, TVariable, TTape> backend,
            TTape tape,
            TVariable input,
            IReadOnlyDictionary<TrainableParameter<TTensor>, TVariable> parameters)
        {
            var current = input;
            foreach (var layer in _layers)
                current = layer.Forward(backend, tape, current, parameters);
            return current;
        }

        public void Dispose()
        {
            for (var i = _layers.Length - 1; i >= 0; i--)
                _layers[i].Dispose();
        }
    }

    private sealed class TrainableLayer : IDisposable
    {
        private readonly DenseLayerSpec _spec;

        public TrainableLayer(
            ITrainableTensorBackend<TTensor, TVariable, TTape> backend,
            DenseLayerSpec spec,
            ReadOnlySpan<float> weights,
            ReadOnlySpan<float> bias,
            string name)
        {
            _spec = spec;
            Weight = new TrainableParameter<TTensor>($"{name}.weight", backend.CreateMatrix(spec.InputSize, spec.OutputSize, weights));
            Bias = new TrainableParameter<TTensor>($"{name}.bias", backend.CreateMatrix(1, spec.OutputSize, bias));
        }

        public TrainableParameter<TTensor> Weight { get; }
        public TrainableParameter<TTensor> Bias { get; }

        public TVariable Forward(
            ITrainableTensorBackend<TTensor, TVariable, TTape> backend,
            TTape tape,
            TVariable input,
            IReadOnlyDictionary<TrainableParameter<TTensor>, TVariable> parameters)
        {
            var product = backend.MatMul(tape, input, parameters[Weight]);
            var bias = backend.BroadcastTo(tape, parameters[Bias], backend.Rows(product), backend.Cols(parameters[Bias]));
            var output = backend.Add(tape, product, bias);
            return ApplyActivation(backend, tape, output);
        }

        private TVariable ApplyActivation(
            ITrainableTensorBackend<TTensor, TVariable, TTape> backend,
            TTape tape,
            TVariable value)
        {
            return _spec.Activation switch
            {
                ActivationKind.Identity => value,
                ActivationKind.ReLU when backend is ITrainableActivationBackend<TTensor, TVariable, TTape> activationBackend =>
                    activationBackend.ReLU(tape, value),
                ActivationKind.ReLU => throw new NotSupportedException(
                    "The selected backend does not support ReLU activation through the trainable tensor abstraction."),
                _ => throw new NotSupportedException($"Unsupported activation: {_spec.Activation}.")
            };
        }

        public void Dispose()
        {
            Bias.Dispose();
            Weight.Dispose();
        }
    }
}
