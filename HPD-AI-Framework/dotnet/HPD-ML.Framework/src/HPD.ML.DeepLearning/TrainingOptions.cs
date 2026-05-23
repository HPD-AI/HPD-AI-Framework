namespace HPD.ML.DeepLearning;

public sealed record TrainingOptions
{
    public int Epochs { get; init; } = 100;
    public float LearningRate { get; init; } = 0.01f;
    public int BatchSize { get; init; } = 32;

    public void Validate()
    {
        if (Epochs <= 0)
            throw new ArgumentOutOfRangeException(nameof(Epochs), "Epoch count must be positive.");
        if (!float.IsFinite(LearningRate) || LearningRate <= 0.0f)
            throw new ArgumentOutOfRangeException(nameof(LearningRate), "Learning rate must be finite and positive.");
        if (BatchSize <= 0)
            throw new ArgumentOutOfRangeException(nameof(BatchSize), "Batch size must be positive.");
    }
}
