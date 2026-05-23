namespace HPD.ML.Backends.Abstractions.Training;

public interface ITrainableActivationBackend<TTensor, TVariable, TTape>
    where TTensor : class, IDisposable
    where TTape : IDisposable
{
    TVariable ReLU(TTape tape, TVariable value);
}
