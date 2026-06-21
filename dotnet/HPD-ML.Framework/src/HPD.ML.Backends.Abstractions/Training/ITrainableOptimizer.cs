namespace HPD.ML.Backends.Abstractions.Training;

public interface ITrainableOptimizer<TTensor>
    where TTensor : class, IDisposable
{
    void Step(TTensor parameter, TTensor gradient);
}
