using System;
using HPD.VCS.Core;

namespace HPD.VCS.WorkingCopy;

/// <summary>
/// Represents the type of a file in the working copy.
/// Based on jj's FileType but simplified for initial implementation.
/// </summary>
public enum FileType
{
    /// <summary>
    /// A normal file (regular file on disk).
    /// </summary>
    NormalFile,
    
    /// <summary>
    /// A symbolic link (symlink).
    /// </summary>
    Symlink
}

/// <summary>
/// Represents the state of a file in the working copy for change detection.
/// This captures the essential metadata needed to determine if a file has changed
/// since the last snapshot operation.
/// 
/// Based on jj's FileState but simplified for initial implementation.
/// We track file type, modification time, and size as the primary change detection signals.
/// </summary>
public readonly record struct FileState
{
    /// <summary>
    /// The type of file (normal file or symlink).
    /// </summary>
    public FileType Type { get; init; }
    /// <summary>
    /// True if this FileState is a placeholder (needs to be re-stat'ed on next snapshot).
    /// </summary>
    public bool IsPlaceholder { get; init; }
    
    /// <summary>
    /// The last modification time of the file in UTC.
    /// Used as a fast heuristic for detecting file changes.
    /// </summary>
    public DateTimeOffset MTimeUtc { get; init; }
      /// <summary>
    /// The size of the file in bytes.
    /// For symlinks, this represents the size of the symlink target string.
    /// Used in combination with mtime for change detection.
    /// </summary>
    public long Size { get; init; }
    
    /// <summary>
    /// The active conflict ID if this file is currently in a conflicted state.
    /// When present, the file on disk contains conflict markers and represents
    /// a materialized conflict that needs to be resolved.
    /// </summary>
    public ConflictId? ActiveConflictId { get; init; }    /// <summary>
    /// Creates a new FileState with the specified properties.
    /// </summary>
    /// <param name="type">The type of file</param>
    /// <param name="mTimeUtc">The last modification time in UTC</param>
    /// <param name="size">The size of the file in bytes</param>
    /// <param name="isPlaceholder">Whether this is a placeholder that needs re-stating</param>
    /// <param name="activeConflictId">The active conflict ID if this file is in a conflicted state</param>
    public FileState(FileType type, DateTimeOffset mTimeUtc, long size, bool isPlaceholder = false, ConflictId? activeConflictId = null)
    {
        Type = type;
        MTimeUtc = mTimeUtc;
        Size = size;
        IsPlaceholder = isPlaceholder;
        ActiveConflictId = activeConflictId;
    }
      /// <summary>
    /// Checks whether this file state appears clean (unchanged) compared to a previous file state.
    /// This can perform a thorough check with granularity and content hashing when needed.
    /// </summary>
    /// <param name="oldFileState">The previous file state to compare against</param>
    /// <param name="granularityMs">The time granularity in milliseconds (default is 2000ms)</param>
    /// <param name="currentBytes">Optional function to get current file bytes for hash comparison</param>
    /// <param name="oldBytes">Optional function to get previous file bytes for hash comparison</param>
    /// <returns>True if the file appears unchanged</returns>
    public bool IsClean(FileState oldFileState, int granularityMs = 2000,
                       Func<byte[]>? currentBytes = null,
                       Func<byte[]>? oldBytes = null)
    {
        // Placeholders are never clean compared to non-placeholders
        if (IsPlaceholder != oldFileState.IsPlaceholder)
            return false;
            
        // File type must always match
        if (Type != oldFileState.Type)
            return false;

        // If content delegates are provided, always compare content and size (ignore mtime granularity)
        if (currentBytes != null && oldBytes != null)
        {
            var curr = currentBytes();
            var old = oldBytes();
            if (curr.Length != old.Length)
                return false;
            for (int i = 0; i < curr.Length; i++)
            {
                if (curr[i] != old[i])
                    return false;
            }
            // If content matches, require type and size to match (mtime is ignored)
            return Size == oldFileState.Size;
        }
        else
        {
            // Strict: require exact match for mtime and size
            return MTimeUtc == oldFileState.MTimeUtc && Size == oldFileState.Size;
        }
    }
    
    /// <summary>
    /// Creates a placeholder file state that indicates a file exists in the tree
    /// but needs to be re-stat'ed on the next snapshot.
    /// This is useful for marking files that need fresh metadata collection.
    /// </summary>
    /// <returns>A placeholder FileState with default values</returns>
    public static FileState Placeholder()
    {
        return new FileState(
            type: FileType.NormalFile,
            mTimeUtc: DateTimeOffset.UnixEpoch,
            size: 0,
            isPlaceholder: true
        );
    }
    
    /// <summary>
    /// Creates a FileState for a normal file based on its metadata.
    /// </summary>
    /// <param name="mTimeUtc">The modification time in UTC</param>
    /// <param name="size">The file size in bytes</param>
    /// <returns>A FileState representing a normal file</returns>
    public static FileState ForFile(DateTimeOffset mTimeUtc, long size)
    {
        return new FileState(
            type: FileType.NormalFile,
            mTimeUtc: mTimeUtc,
            size: size,
            isPlaceholder: false
        );
    }
    
    /// <summary>
    /// Creates a FileState for a symlink based on its metadata.
    /// </summary>
    /// <param name="mTimeUtc">The modification time in UTC</param>
    /// <param name="size">The size of the symlink target string in bytes</param>
    /// <returns>A FileState representing a symlink</returns>
    public static FileState ForSymlink(DateTimeOffset mTimeUtc, long size)
    {
        return new FileState(
            type: FileType.Symlink,
            mTimeUtc: mTimeUtc,
            size: size,
            isPlaceholder: false
        );
    }
    
    /// <summary>
    /// Returns a string representation of this file state for debugging.
    /// </summary>
    public override string ToString()
    {
        var placeholderStr = IsPlaceholder ? ", Placeholder" : string.Empty;
        return $"FileState({Type}, {MTimeUtc:yyyy-MM-dd HH:mm:ss}, {Size} bytes{placeholderStr})";
    }
}
