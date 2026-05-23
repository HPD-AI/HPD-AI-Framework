namespace Rhodium.Platform;

/// <summary>
/// Public marker for generated tick-frequency strategy context.
/// Concrete strategies receive a nested <c>TickContext</c> ref struct with
/// generated tick indicator accessors and order helpers.
/// </summary>
public readonly ref struct TickContext
{
}
