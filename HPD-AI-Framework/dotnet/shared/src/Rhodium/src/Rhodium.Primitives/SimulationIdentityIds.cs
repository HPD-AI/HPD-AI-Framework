namespace Rhodium.Primitives;

/// <summary>
/// Simulated venue-assigned order identifier.
/// </summary>
public readonly record struct VenueOrderId(long Value)
{
    public override string ToString() => Value == 0 ? string.Empty : Value.ToString();
}

/// <summary>
/// Simulated venue execution identifier.
/// </summary>
public readonly record struct ExecutionId(long Value)
{
    public override string ToString() => Value == 0 ? string.Empty : Value.ToString();
}

/// <summary>
/// Simulated position identifier.
/// </summary>
public readonly record struct PositionId(long Value)
{
    public override string ToString() => Value == 0 ? string.Empty : Value.ToString();
}
