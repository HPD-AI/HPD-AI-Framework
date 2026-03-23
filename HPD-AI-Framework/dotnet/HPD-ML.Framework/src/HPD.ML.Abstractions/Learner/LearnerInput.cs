namespace HPD.ML.Abstractions;

public sealed record LearnerInput(
    IDataHandle TrainData,
    IDataHandle? ValidationData = null,
    IModel? InitialModel = null);
