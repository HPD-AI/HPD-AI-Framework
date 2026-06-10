namespace HPD.Math.Managed;

/// <summary>
/// Marker for owned heap-backed convenience wrappers. Hot-path kernels live in Core, Finite, and Algebra.
/// </summary>
public static class ManagedLayer
{
    public const string Name = "HPD.Math.Managed";
}
