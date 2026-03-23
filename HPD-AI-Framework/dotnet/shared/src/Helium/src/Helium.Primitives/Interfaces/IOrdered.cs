using System.Numerics;

namespace Helium.Primitives;

/// <summary>
/// A totally ordered type compatible with ring operations.
/// </summary>
public interface IOrdered<T> : IComparisonOperators<T, T, bool>
    where T : IOrdered<T>
{
}
