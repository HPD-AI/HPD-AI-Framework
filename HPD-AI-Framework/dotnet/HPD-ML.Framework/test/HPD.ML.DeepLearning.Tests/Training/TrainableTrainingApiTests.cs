namespace HPD.ML.DeepLearning.Tests.Training;

using HPD.ML.Backends.Abstractions.Training;
using HPD.ML.DeepLearning;
using HPD.ML.DeepLearning.Backends;
using HPD.ML.DeepLearning.Training;

public sealed class TrainableTrainingApiTests
{
    [Fact]
    public void DenseLayer_ValidatesShapeAndExposesParameters()
    {
        using var backend = new FakeBackend();
        using var layer = new TrainableDenseLayer<FakeTensor, FakeVariable, FakeTape>(
            backend,
            inputSize: 2,
            outputSize: 3,
            weights: [1, 2, 3, 4, 5, 6],
            bias: [0, 0, 0],
            name: "layer0");

        var parameters = layer.Parameters.ToArray();
        Assert.Equal(2, parameters.Length);
        Assert.Equal("layer0.weight", parameters[0].Name);
        Assert.Equal("layer0.bias", parameters[1].Name);
        Assert.Equal([1, 2, 3, 4, 5, 6], parameters[0].Value.Data);
    }

    [Fact]
    public void DenseLayer_RejectsInvalidWeightLength()
    {
        using var backend = new FakeBackend();

        Assert.Throws<ArgumentException>(() =>
            new TrainableDenseLayer<FakeTensor, FakeVariable, FakeTape>(
                backend,
                inputSize: 2,
                outputSize: 3,
                weights: [1, 2],
                bias: [0, 0, 0]));
    }

    [Fact]
    public void Sequential_RejectsEmptyModel()
    {
        Assert.Throws<ArgumentException>(() =>
            new TrainableSequential<FakeTensor, FakeVariable, FakeTape>());
    }

    [Fact]
    public void SgdStep_UpdatesParameter()
    {
        using var backend = new FakeBackend();
        using var parameter = new FakeTensor(1, 3, [1, 2, 3]);
        using var gradient = new FakeTensor(1, 3, [0.5f, 1.0f, -2.0f]);
        var optimizer = new TrainableSgdOptimizer<FakeTensor, FakeVariable, FakeTape>(backend, learningRate: 0.1f);

        optimizer.Step(parameter, gradient);

        Assert.Equal([0.95f, 1.9f, 3.2f], parameter.Data);
    }

    [Fact]
    public void AdamStep_UpdatesParameterAndKeepsStateByReference()
    {
        using var backend = new FakeBackend();
        using var parameter = new FakeTensor(1, 1, [1]);
        using var gradient = new FakeTensor(1, 1, [0.5f]);
        using var optimizer = new TrainableAdamOptimizer<FakeTensor, FakeVariable, FakeTape>(
            backend,
            learningRate: 0.1f);

        optimizer.Step(parameter, gradient);
        var afterFirst = parameter.Data[0];
        optimizer.Step(parameter, gradient);

        Assert.True(afterFirst < 1.0f);
        Assert.True(parameter.Data[0] < afterFirst);
    }

    [Fact]
    public void HeliumTrainableTrainer_ReturnsMaterializedParameters()
    {
        using var backend = new FakeBackend();
        var definition = new NeuralNetworkDefinition(
            "Features",
            "Label",
            [new DenseLayerSpec(1, 1)]);
        var trainer = new HeliumTrainableNeuralNetworkTrainer<FakeTensor, FakeVariable, FakeTape>(backend);

        var parameters = trainer.Train(
            definition,
            [[0.0f], [1.0f]],
            [[1.0f], [3.0f]],
            new TrainingOptions { Epochs = 1, LearningRate = 0.1f, BatchSize = 2 },
            seed: 4);

        Assert.Equal(definition, parameters.Definition);
        Assert.Single(parameters.Weights);
        Assert.Single(parameters.Biases);
        Assert.Equal(1, parameters.Weights[0].Length);
        Assert.Equal(1, parameters.Biases[0].Length);
    }

    [Fact]
    public void HeliumTrainableTrainer_RejectsReLUWhenBackendDoesNotExposeActivation()
    {
        using var backend = new FakeBackend();
        var definition = new NeuralNetworkDefinition(
            "Features",
            "Label",
            [new DenseLayerSpec(1, 1, ActivationKind.ReLU)]);
        var trainer = new HeliumTrainableNeuralNetworkTrainer<FakeTensor, FakeVariable, FakeTape>(backend);

        Assert.Throws<NotSupportedException>(() =>
            trainer.Train(
                definition,
                [[0.0f]],
                [[1.0f]],
                new TrainingOptions { Epochs = 1, LearningRate = 0.1f, BatchSize = 1 },
                seed: 4));
    }

    private sealed class FakeTensor : IDisposable
    {
        public FakeTensor(int rows, int cols, ReadOnlySpan<float> data)
        {
            Rows = rows;
            Cols = cols;
            Data = data.ToArray();
        }

        public int Rows { get; }
        public int Cols { get; }
        public float[] Data { get; private set; }

        public void Update(ReadOnlySpan<float> data) => Data = data.ToArray();
        public void Dispose() { }
    }

    private sealed class FakeVariable(FakeTensor value)
    {
        public FakeTensor Value { get; } = value;
    }

    private sealed class FakeTape : IDisposable
    {
        public void Dispose() { }
    }

    private sealed class FakeBackend : ITrainableTensorBackend<FakeTensor, FakeVariable, FakeTape>, IDisposable
    {
        public FakeTensor CreateMatrix(int rows, int cols, ReadOnlySpan<float> data = default) =>
            new(rows, cols, data.IsEmpty ? new float[rows * cols] : data);

        public FakeTape CreateTape() => new();
        public FakeVariable Watch(FakeTape tape, FakeTensor value) => new(value);
        public FakeVariable MatMul(FakeTape tape, FakeVariable left, FakeVariable right) => new(new FakeTensor(left.Value.Rows, right.Value.Cols, new float[left.Value.Rows * right.Value.Cols]));
        public FakeVariable Add(FakeTape tape, FakeVariable left, FakeVariable right) => left;
        public FakeVariable Subtract(FakeTape tape, FakeVariable left, FakeVariable right) => left;
        public FakeVariable Multiply(FakeTape tape, FakeVariable left, FakeVariable right) => left;
        public FakeVariable Mean(FakeTape tape, FakeVariable value) => value;
        public FakeVariable Scale(FakeTape tape, FakeVariable value, float scalar) => value;
        public FakeVariable BroadcastTo(FakeTape tape, FakeVariable value, int rows, int cols) => new(new FakeTensor(rows, cols, value.Value.Data));
        public FakeTensor Gradient(FakeTape tape, FakeVariable output, FakeVariable input) => new(input.Value.Rows, input.Value.Cols, Enumerable.Repeat(1.0f, input.Value.Rows * input.Value.Cols).ToArray());
        public FakeTensor Value(FakeVariable variable) => variable.Value;
        public FakeTensor Scale(FakeTensor value, float scalar) => new(value.Rows, value.Cols, value.Data.Select(x => x * scalar).ToArray());
        public FakeTensor Subtract(FakeTensor left, FakeTensor right) => new(left.Rows, left.Cols, left.Data.Zip(right.Data, (l, r) => l - r).ToArray());
        public float[] ToArray(FakeTensor value) => [.. value.Data];
        public float ReadScalar(FakeTensor value) => value.Data[0];
        public int Rows(FakeTensor value) => value.Rows;
        public int Cols(FakeTensor value) => value.Cols;
        public int Rows(FakeVariable value) => value.Value.Rows;
        public int Cols(FakeVariable value) => value.Value.Cols;
        public void Update(FakeTensor value, ReadOnlySpan<float> data) => value.Update(data);
        public void Dispose() { }
    }
}
