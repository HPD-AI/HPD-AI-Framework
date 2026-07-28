namespace HPD.Base.Realtime.Configuration;

using HPD.Events;

internal static class BaseRealtimeOptionsValidator
{
    public const int MinimumPayloadBytes = 256;

    public static void Validate(BaseRealtimeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var limits = options.Limits
            ?? throw new ArgumentException("Realtime limits must be configured.", nameof(options));

        Positive(limits.MaxConnections, nameof(limits.MaxConnections));
        Positive(limits.MaxChannelsPerConnection, nameof(limits.MaxChannelsPerConnection));
        Positive(limits.StreamCapacity, nameof(limits.StreamCapacity));
        Positive(limits.OutboundCapacity, nameof(limits.OutboundCapacity));
        Positive(limits.MaxMessageBytes, nameof(limits.MaxMessageBytes));
        Positive(limits.ReceiveIdleTimeoutSeconds, nameof(limits.ReceiveIdleTimeoutSeconds));
        Positive(limits.SendTimeoutSeconds, nameof(limits.SendTimeoutSeconds));
        Positive(limits.MaxJoinsPerSecond, nameof(limits.MaxJoinsPerSecond));

        if (options.Backpressure is not (
            AsyncStreamBackpressureMode.DropOldest
            or AsyncStreamBackpressureMode.DropNewest
            or AsyncStreamBackpressureMode.DropWrite))
        {
            throw new ArgumentOutOfRangeException(
                nameof(options.Backpressure),
                options.Backpressure,
                "Realtime backpressure must be DropOldest, DropNewest, or DropWrite.");
        }

        if (limits.MaxPayloadBytes < MinimumPayloadBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limits.MaxPayloadBytes),
                limits.MaxPayloadBytes,
                $"Realtime {nameof(limits.MaxPayloadBytes)} must be at least {MinimumPayloadBytes} bytes so fixed protocol responses can be sent.");
        }
    }

    private static void Positive(int value, string name)
    {
        if (value <= 0)
            throw new ArgumentOutOfRangeException(name, value, $"Realtime {name} must be positive.");
    }
}
