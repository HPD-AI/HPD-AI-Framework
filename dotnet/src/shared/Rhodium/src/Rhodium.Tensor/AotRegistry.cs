namespace Rhodium.Tensor;

/// <summary>
/// AOT registration for generic tensor fields and kernels.
/// This ensures Native AOT compiler includes all required generic instantiations.
/// If your runtime does not require registration, this can be a no-op.
/// </summary>
public static class AotRegistry
{
    /// <summary>
    /// Registers all tensor fields and kernels for Native AOT compilation.
    /// Call this once during application startup if using Native AOT.
    /// </summary>
    public static void Register()
    {
        // Register standard strategy fields (VectorField<T> instances)
        RegisterField(Field.OpenRaw);
        RegisterField(Field.HighRaw);
        RegisterField(Field.LowRaw);
        RegisterField(Field.CloseRaw);
        RegisterField(Field.VolumeRaw);

        RegisterField(Field.SplitFactor);
        RegisterField(Field.DividendScale);
        RegisterField(Field.PriceScale);
        RegisterField(Field.VolumeScale);

        RegisterField(Field.Open);
        RegisterField(Field.High);
        RegisterField(Field.Low);
        RegisterField(Field.Close);
        RegisterField(Field.Volume);

        // Register kernel types
        RegisterKernel<AdjustmentKernel>();

        // Note: Custom indicator fields and kernels should be registered
        // by the consuming application or via source generators
    }

    private static void RegisterField<T>(VectorField<T> field) where T : unmanaged
    {
        // This method exists to force the compiler to include the generic instantiation
        // The field reference ensures T is known at compile time
        _ = field.Name;
    }

    private static void RegisterKernel<TKernel>() where TKernel : struct, IComputeKernel
    {
        // This method exists to force the compiler to include the kernel type
        // No runtime action needed - the generic constraint is sufficient
    }
}
