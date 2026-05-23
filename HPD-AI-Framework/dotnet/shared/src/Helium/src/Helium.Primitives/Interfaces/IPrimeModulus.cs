namespace Helium.Primitives;

/// <summary>
/// A static witness for an exact prime modulus.
/// </summary>
public interface IPrimeModulus
{
    static abstract Integer Value { get; }
}
