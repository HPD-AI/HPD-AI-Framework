using System.Text.Json.Serialization;

namespace HPD.VCS.Configuration;

/// <summary>
/// Configuration settings for a VCS repository.
/// </summary>
public class RepositoryConfig
{
    private WorkingCopyConfig? _workingCopy;

    /// <summary>
    /// Working copy configuration settings.
    /// </summary>
    [JsonPropertyName("workingCopy")]
    public WorkingCopyConfig? WorkingCopy 
    { 
        get => _workingCopy;
        set => _workingCopy = value ?? throw new ArgumentNullException(nameof(value));
    }
}

/// <summary>
/// Configuration settings for the working copy behavior.
/// </summary>
public class WorkingCopyConfig
{
    private string? _mode;

    /// <summary>
    /// The working copy mode. Valid values are "explicit" or "live".
    /// </summary>
    [JsonPropertyName("mode")]
    public string? Mode 
    { 
        get => _mode;
        set
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Mode cannot be empty or whitespace", nameof(value));
            
            // Don't validate the actual mode value here - let ToWorkingCopyMode() handle that
            _mode = value;
        }
    }
}

/// <summary>
/// Enumeration of supported working copy modes.
/// </summary>
public enum WorkingCopyMode
{
    /// <summary>
    /// Explicit snapshot mode - changes must be explicitly snapshotted.
    /// </summary>
    Explicit,

    /// <summary>
    /// Live mode - changes are automatically tracked and updated.
    /// </summary>
    Live
}

/// <summary>
/// Extension methods for working copy mode operations.
/// </summary>
public static class WorkingCopyModeExtensions
{
    /// <summary>
    /// Converts a string mode value to the corresponding enum.
    /// </summary>
    /// <param name="mode">The mode string ("explicit" or "live")</param>
    /// <returns>The corresponding WorkingCopyMode enum value</returns>
    /// <exception cref="ArgumentException">Thrown when the mode string is invalid</exception>
    public static WorkingCopyMode ToWorkingCopyMode(this string mode)
    {
        return mode switch
        {
            "explicit" => WorkingCopyMode.Explicit,
            "live" => WorkingCopyMode.Live,
            _ => throw new ArgumentException($"Invalid working copy mode: {mode}. Valid values are 'explicit' or 'live'.", nameof(mode))
        };
    }

    /// <summary>
    /// Converts a WorkingCopyMode enum to its string representation.
    /// </summary>
    /// <param name="mode">The WorkingCopyMode enum value</param>
    /// <returns>The string representation of the mode</returns>
    public static string ToModeString(this WorkingCopyMode mode)
    {
        return mode switch
        {
            WorkingCopyMode.Explicit => "explicit",
            WorkingCopyMode.Live => "live",
            _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown working copy mode")
        };
    }
}
