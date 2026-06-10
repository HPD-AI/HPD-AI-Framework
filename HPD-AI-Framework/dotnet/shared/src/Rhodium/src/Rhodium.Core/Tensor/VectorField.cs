namespace Rhodium.Tensor;

/// <summary>
/// Typed column identifier for tensor fields.
/// </summary>
/// <typeparam name="T">The unmanaged type stored in this field.</typeparam>
public readonly record struct VectorField<T>(string Name) where T : unmanaged;
