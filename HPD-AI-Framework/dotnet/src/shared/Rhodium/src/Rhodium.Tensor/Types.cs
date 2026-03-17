using System.Runtime.InteropServices;

namespace Rhodium.Tensor;

/// <summary>
/// Price value in F64 format for kernel operations.
/// Zero-overhead wrapper around double for semantic correctness.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct PriceF64(double Value);

/// <summary>
/// Size/quantity value in F64 format for kernel operations.
/// Zero-overhead wrapper around double for semantic correctness.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct SizeF64(double Value);

/// <summary>
/// Dimensionless scalar for factors and indicators (splits/dividends/ratios).
/// Zero-overhead wrapper around double for semantic correctness.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public readonly record struct FactorF64(double Value);
