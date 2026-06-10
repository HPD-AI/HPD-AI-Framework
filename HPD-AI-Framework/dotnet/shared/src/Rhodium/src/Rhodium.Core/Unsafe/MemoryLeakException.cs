namespace Rhodium.Unsafe;

public sealed class MemoryLeakException : Exception
{
    public MemoryLeakException(string message) : base(message)
    {
    }
}
