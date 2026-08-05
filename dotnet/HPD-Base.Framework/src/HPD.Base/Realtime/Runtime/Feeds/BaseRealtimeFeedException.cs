namespace HPD.Base;

internal sealed class BaseRealtimeFeedException : Exception
{
    /// <summary>Initializes a new instance.</summary>
    public BaseRealtimeFeedException(string code, string safeMessage)
        : base(safeMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(safeMessage);
        Code = code;
        SafeMessage = safeMessage;
    }

    /// <summary>Gets the code.</summary>
    public string Code { get; }

    /// <summary>Gets the safe message.</summary>
    public string SafeMessage { get; }
}
