namespace HPD.ML.Backends.Mlx.Training;

public abstract class MlxModule : IDisposable
{
    public abstract MlxFloatTensorVar Forward(MlxTensorTape tape, MlxFloatTensorVar input);

    public abstract IEnumerable<MlxParameter> Parameters { get; }

    public virtual void Dispose()
    {
        foreach (var parameter in Parameters)
            parameter.Dispose();
    }
}
