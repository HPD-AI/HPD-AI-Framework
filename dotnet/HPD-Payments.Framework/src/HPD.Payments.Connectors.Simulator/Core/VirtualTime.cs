namespace HPD.Payments.Connectors.Simulator.Core;

/// <summary>Provides deterministic monotone UTC time without consulting ambient wall-clock state.</summary>
public sealed class SimulatorVirtualTime
{
    /// <summary>Gets the fixed scenario epoch.</summary>
    public DateTimeOffset EpochUtc { get; }
    /// <summary>Gets current virtual UTC time.</summary>
    public DateTimeOffset UtcNow { get; private set; }

    /// <summary>Creates virtual time at an explicit UTC epoch.</summary>
    /// <exception cref="ArgumentException">The epoch is not UTC.</exception>
    public SimulatorVirtualTime(DateTimeOffset epochUtc)
    {
        if (epochUtc.Offset != TimeSpan.Zero) throw new ArgumentException("Virtual-time epoch must be UTC.", nameof(epochUtc));
        EpochUtc = epochUtc; UtcNow = epochUtc;
    }

    /// <summary>Advances to an offset from the epoch and rejects time reversal.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The offset is negative or precedes current virtual time.</exception>
    public void AdvanceTo(TimeSpan offset)
    {
        if (offset < TimeSpan.Zero || EpochUtc + offset < UtcNow) throw new ArgumentOutOfRangeException(nameof(offset));
        UtcNow = EpochUtc + offset;
    }
}
