namespace HPD.ML.Backends.Pjrt;

/// <summary>
/// Error raised by Helium's PJRT interop boundary.
/// </summary>
public sealed class PjrtException : Exception
{
    public PjrtException(string message)
        : base(message)
    {
    }

    public PjrtException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

