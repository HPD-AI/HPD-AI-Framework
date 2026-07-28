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
        Positive(limits.ReplayBatchSize, nameof(limits.ReplayBatchSize));
        Positive(limits.CursorLifetimeSeconds, nameof(limits.CursorLifetimeSeconds));
        Positive(limits.DurablePollIntervalMilliseconds, nameof(limits.DurablePollIntervalMilliseconds));

        if (options.CursorSigningKey is not null
            && System.Text.Encoding.UTF8.GetByteCount(options.CursorSigningKey) < 32)
        {
            throw new ArgumentException(
                "Realtime cursor signing key must contain at least 32 UTF-8 bytes.",
                nameof(options));
        }

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
