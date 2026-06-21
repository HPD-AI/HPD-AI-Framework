namespace HPD.ML.Backends.Mlx;

public sealed class MlxException : Exception
{
    public MlxException(string message)
        : base(message)
    {
    }

    public MlxException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

