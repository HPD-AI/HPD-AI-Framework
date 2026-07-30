using HPD.Base.Application.Records;
using HPD.Base.Records;

namespace HPD.Base.Application.Queries;

/// <summary>
/// Represents one bounded typed query page.
/// </summary>
public sealed record BasePage<T>
{
    /// <summary>Gets the policy-projected typed records.</summary>
    public required BaseRecord<T>[] Items { get; init; }

    /// <summary>Gets canonical continuation metadata.</summary>
    public required PageInfo Page { get; init; }

    /// <summary>Gets count metadata when requested and available.</summary>
    public CountInfo? Count { get; init; }
}
