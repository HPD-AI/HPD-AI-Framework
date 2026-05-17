namespace HPD.Sandbox.Local.Policy;

/// <summary>
/// Normalized filesystem policy used by platform-specific sandbox emitters.
/// </summary>
internal sealed record FilesystemPolicy
{
    /// <summary>
    /// Paths denied for reads. Empty means reads are unrestricted.
    /// </summary>
    public IReadOnlyList<string> DenyRead { get; init; } = [];

    /// <summary>
    /// Paths re-allowed within denied read regions.
    /// </summary>
    public IReadOnlyList<string> AllowRead { get; init; } = [];

    /// <summary>
    /// Paths allowed for writes. Empty means no user paths are writable.
    /// Platform defaults may still be added by emitters.
    /// </summary>
    public IReadOnlyList<string> AllowWrite { get; init; } = [];

    /// <summary>
    /// Paths denied for writes within allowed write regions.
    /// </summary>
    public IReadOnlyList<string> DenyWrite { get; init; } = [];

    public bool AllowGitConfig { get; init; }
}
