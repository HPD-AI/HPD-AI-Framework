namespace Rhodium.Platform;

/// <summary>
/// Public marker for generated trade-frequency strategy context.
/// Concrete strategies receive a nested <c>TradeContext</c> ref struct with
/// generated field accessors, trade payload, and order helpers.
/// </summary>
public readonly ref struct TradeContext
{
}
