namespace Rhodium.Platform;

/// <summary>
/// Public marker for generated quote-frequency strategy context.
/// Concrete strategies receive a nested <c>QuoteContext</c> ref struct with
/// generated field accessors, quote payload, and order helpers.
/// </summary>
public readonly ref struct QuoteContext
{
}
