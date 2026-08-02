
namespace HPD.Base;

/// <summary>
/// Represents one bounded typed query page.
/// </summary>
public sealed record BasePage<T>
{
    /// <summary>Gets the complete typed values in this page.</summary>
    public required T[] Items { get; init; }

    /// <summary>Gets canonical continuation metadata.</summary>
    public required PageInfo Page { get; init; }

    /// <summary>Gets count metadata when requested and available.</summary>
    public CountInfo? Count { get; init; }
}
