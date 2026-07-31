namespace HPD.Base;

internal sealed class BaseRealtimeFeedException : Exception
{
    public BaseRealtimeFeedException(string code, string safeMessage)
        : base(safeMessage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(safeMessage);
        Code = code;
        SafeMessage = safeMessage;
    }

    public string Code { get; }

    public string SafeMessage { get; }
}
