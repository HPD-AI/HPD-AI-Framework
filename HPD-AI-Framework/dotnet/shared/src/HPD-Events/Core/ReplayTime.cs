namespace HPD.Events.Core;

internal static class ReplayTime
{
    private static readonly long UnixEpochTicks = DateTimeOffset.UnixEpoch.Ticks;

    public static long GetTimestampNs(Event evt) =>
        evt.ExchangeTimestampNs != 0
            ? evt.ExchangeTimestampNs
            : ToUnixNanoseconds(evt.Timestamp);

    public static long ToUnixNanoseconds(DateTimeOffset timestamp)
    {
        var utc = timestamp.ToUniversalTime();
        return checked((utc.Ticks - UnixEpochTicks) * 100L);
    }

    public static bool Matches(Event evt, ReplayReadOptions options)
    {
        if (options.EventFlowId is not null && evt.EventFlowId != options.EventFlowId)
            return false;

        var timestampNs = GetTimestampNs(evt);
        if (options.From is { } from && timestampNs < ToUnixNanoseconds(from))
            return false;

        if (options.To is { } to && timestampNs >= ToUnixNanoseconds(to))
            return false;

        return true;
    }
}
