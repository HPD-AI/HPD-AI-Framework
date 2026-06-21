namespace HPD.ML.Backends.Pjrt.Training;

public abstract class PjrtModule : IDisposable
{
    public abstract IEnumerable<PjrtParameter> Parameters { get; }

    public abstract PjrtFloatTensorVar Forward(PjrtTensorTape tape, PjrtFloatTensorVar input);

    public virtual void Dispose()
    {
        foreach (var parameter in Parameters)
            parameter.Dispose();
    }
}
