using System;
using System.Collections.Generic;

namespace HPD.VCS.WorkingCopy;

/// <summary>
/// Options for controlling checkout operation behavior.
/// </summary>
/// <remarks>
/// For V1, this is primarily a placeholder for future extensibility.
/// Future versions may include options like ForceOverwriteUntracked.
/// </remarks>
public record CheckoutOptions
{
    // For V1, this is primarily a placeholder.
    // Future: public bool ForceOverwriteUntracked { get; init; } = false;
}

/// <summary>
/// Statistics reported after a checkout operation completes.
/// Provides detailed information about what files were affected during the checkout.
/// </summary>
/// <param name="FilesUpdated">Number of files whose content or type changed during checkout</param>
/// <param name="FilesAdded">Number of files newly created on disk during checkout</param>
/// <param name="FilesRemoved">Number of files deleted from disk during checkout</param>
/// <param name="FilesSkipped">Number of files that couldn't be written due to untracked conflicts or file locks</param>
public readonly record struct CheckoutStats(
    int FilesUpdated,      // Files content/type changed
    int FilesAdded,        // Files newly created on disk
    int FilesRemoved,      // Files deleted from disk
    int FilesSkipped,      // Files that couldn't be written due to untracked conflicts or locks
    int ConflictsMaterialized  // Conflicts that were materialized to disk with conflict markers
)
{
    /// <summary>
    /// Gets the total number of files that were successfully processed (updated, added, or removed).
    /// </summary>
    public int TotalProcessed => FilesUpdated + FilesAdded + FilesRemoved;

    /// <summary>
    /// Gets the total number of files that were affected by the checkout operation.
    /// </summary>
    public int TotalAffected => TotalProcessed + FilesSkipped;

    /// <summary>
    /// Indicates whether the checkout was completely successful (no files were skipped).
    /// </summary>
    public bool IsCompleteSuccess => FilesSkipped == 0;    /// <summary>
    /// Returns a default CheckoutStats instance with all counts set to zero.
    /// </summary>
    public static CheckoutStats Empty => new(0, 0, 0, 0, 0);    /// <summary>
    /// Returns a string representation of the checkout statistics.
    /// </summary>
    public override string ToString()
    {
        if (TotalAffected == 0)
        {
            return "No files affected";
        }

        var parts = new List<string>();
        
        if (FilesUpdated > 0)
            parts.Add($"{FilesUpdated} updated");
        if (FilesAdded > 0)
            parts.Add($"{FilesAdded} added");
        if (FilesRemoved > 0)
            parts.Add($"{FilesRemoved} removed");
        if (FilesSkipped > 0)
            parts.Add($"{FilesSkipped} skipped");
        if (ConflictsMaterialized > 0)
            parts.Add($"{ConflictsMaterialized} conflicts materialized");

        return string.Join(", ", parts);
    }
}

/// <summary>
/// Builder class for constructing CheckoutStats during a checkout operation.
/// Provides mutable counters that can be incremented as files are processed.
/// </summary>
internal class CheckoutStatsBuilder
{
    /// <summary>
    /// Number of files whose content or type changed during checkout.
    /// </summary>
    public int FilesUpdated { get; set; }

    /// <summary>
    /// Number of files newly created on disk during checkout.
    /// </summary>
    public int FilesAdded { get; set; }

    /// <summary>
    /// Number of files deleted from disk during checkout.
    /// </summary>
    public int FilesRemoved { get; set; }    /// <summary>
    /// Number of files that couldn't be written due to untracked conflicts or file locks.
    /// </summary>
    public int FilesSkipped { get; set; }

    /// <summary>
    /// Number of conflicts that were materialized to disk with conflict markers.
    /// </summary>
    public int ConflictsMaterialized { get; set; }

    /// <summary>
    /// Creates a new CheckoutStatsBuilder with all counters initialized to zero.
    /// </summary>
    public CheckoutStatsBuilder()
    {
        FilesUpdated = 0;
        FilesAdded = 0;
        FilesRemoved = 0;
        FilesSkipped = 0;
        ConflictsMaterialized = 0;
    }    /// <summary>
    /// Builds an immutable CheckoutStats instance from the current counter values.
    /// </summary>
    /// <returns>A CheckoutStats instance containing the current statistics</returns>
    public CheckoutStats ToImmutableStats()
    {
        return new CheckoutStats(FilesUpdated, FilesAdded, FilesRemoved, FilesSkipped, ConflictsMaterialized);
    }/// <summary>
    /// Resets all counters to zero.
    /// </summary>
    public void Reset()
    {
        FilesUpdated = 0;
        FilesAdded = 0;
        FilesRemoved = 0;
        FilesSkipped = 0;
        ConflictsMaterialized = 0;
    }    /// <summary>
    /// Adds the values from another CheckoutStatsBuilder to this one.
    /// </summary>
    /// <param name="other">The other stats builder to add</param>
    public void Add(CheckoutStatsBuilder other)
    {
        ArgumentNullException.ThrowIfNull(other);
        
        FilesUpdated += other.FilesUpdated;
        FilesAdded += other.FilesAdded;
        FilesRemoved += other.FilesRemoved;
        FilesSkipped += other.FilesSkipped;
        ConflictsMaterialized += other.ConflictsMaterialized;
    }

    /// <summary>
    /// Returns a string representation of the current statistics.
    /// </summary>
    public override string ToString()
    {
        return ToImmutableStats().ToString();
    }
}
