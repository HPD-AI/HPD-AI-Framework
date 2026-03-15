using System.Collections.Immutable;

namespace HPD.VCS.Core;

/// <summary>
/// Generic representation of a merge result that can be either resolved or conflicted.
/// Follows the jj model where conflicts are represented as alternating removes/adds.
/// </summary>
/// <typeparam name="T">The type of values being merged (can be value or reference types)</typeparam>
public readonly record struct Merge<T>(
    IReadOnlyList<T?> Removes,
    IReadOnlyList<T?> Adds
)
{
    /// <summary>
    /// Creates a resolved (non-conflicted) merge with a single value.
    /// </summary>
    public static Merge<T> Resolved(T value) => new(
        ImmutableArray<T?>.Empty,
        ImmutableArray.Create<T?>(value)
    );

    /// <summary>
    /// Creates a conflicted merge from the common ancestor and conflicting sides.
    /// </summary>
    public static Merge<T> Conflicted(T? ancestor, T? left, T? right) => new(
        ImmutableArray.Create<T?>(ancestor),
        ImmutableArray.Create<T?>(left, right)
    );

    /// <summary>
    /// Returns true if this merge represents a conflict (has removes).
    /// </summary>
    public bool IsConflicted => Removes.Count > 0;

    /// <summary>
    /// Returns true if this merge is resolved to a single value.
    /// </summary>
    public bool IsResolved => !IsConflicted && Adds.Count == 1;    /// <summary>
    /// Gets the resolved value if this merge is resolved.
    /// Throws InvalidOperationException if the merge is conflicted.
    /// </summary>
    public T ResolvedValue => IsResolved 
        ? Adds[0]! 
        : throw new InvalidOperationException("Cannot get resolved value from conflicted merge");

    /// <summary>
    /// Implements proper equality comparison for Merge instances.
    /// Compares the content of Removes and Adds lists element by element.
    /// </summary>
    public bool Equals(Merge<T> other)
    {
        if (Removes.Count != other.Removes.Count || Adds.Count != other.Adds.Count)
            return false;

        for (int i = 0; i < Removes.Count; i++)
        {
            if (!EqualityComparer<T?>.Default.Equals(Removes[i], other.Removes[i]))
                return false;
        }

        for (int i = 0; i < Adds.Count; i++)
        {
            if (!EqualityComparer<T?>.Default.Equals(Adds[i], other.Adds[i]))
                return false;
        }

        return true;
    }

    /// <summary>
    /// Generates hash code based on the content of Removes and Adds lists.
    /// </summary>
    public override int GetHashCode()
    {
        var hash = new HashCode();
        
        foreach (var remove in Removes)
        {
            hash.Add(remove);
        }
        
        foreach (var add in Adds)
        {
            hash.Add(add);
        }
        
        return hash.ToHashCode();
    }
}
