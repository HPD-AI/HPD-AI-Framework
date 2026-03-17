using System.Runtime.CompilerServices;

namespace Rhodium.Platform;

/// <summary>
/// Type-safe wrapper for virtual index in strategy code.
/// Prevents accidental misuse of raw integers.
/// Topology-bound handle for the current BatchMap layout.
/// </summary>
public readonly record struct AssetId(int VirtualIndex)
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator int(AssetId id) => id.VirtualIndex;

    /// <summary>
    /// Creates a new AssetId with a variant offset applied.
    /// Useful for grid search or parameter optimization.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public AssetId WithVariant(int offset) => new(VirtualIndex + offset);

    public override string ToString() => $"AssetId({VirtualIndex})";
}
