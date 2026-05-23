namespace Rhodium.Platform;

public sealed class TensorAccessException : InvalidOperationException
{
    public TensorAccessException(string message)
        : base(message)
    {
    }
}
