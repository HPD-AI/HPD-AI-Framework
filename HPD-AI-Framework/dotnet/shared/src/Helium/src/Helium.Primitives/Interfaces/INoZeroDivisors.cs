namespace Helium.Primitives;

/// <summary>
/// Marker: a * b == 0 implies a == 0 or b == 0.
/// </summary>
public interface INoZeroDivisors<T>
    where T : INoZeroDivisors<T>
{
}
