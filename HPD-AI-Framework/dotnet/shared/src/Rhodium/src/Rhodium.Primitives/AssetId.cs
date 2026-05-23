using System.Runtime.CompilerServices;

namespace Rhodium.Primitives;

/// <summary>
/// Type-safe handle for a virtual asset slot in the active BatchMap layout.
/// </summary>
public readonly record struct AssetId(int VirtualIndex)
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static implicit operator int(AssetId id) => id.VirtualIndex;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public AssetId WithVariant(int offset) => new(VirtualIndex + offset);

    public override string ToString() => $"AssetId({VirtualIndex})";
}
