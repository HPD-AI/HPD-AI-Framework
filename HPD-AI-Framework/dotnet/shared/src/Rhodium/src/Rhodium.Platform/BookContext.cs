namespace Rhodium.Platform;

/// <summary>
/// Public marker for generated book-frequency strategy context.
/// Concrete strategies receive a nested <c>BookContext</c> ref struct with
/// generated field accessors, book payload, and order helpers.
/// </summary>
public readonly ref struct BookContext
{
}
