namespace Helium.Primitives;

/// <summary>
/// Executable yes/no result for Helium decidable predicates.
/// </summary>
public readonly record struct Decision(bool IsTrue)
{
    public static Decision True => new(true);
    public static Decision False => new(false);

    public static implicit operator bool(Decision decision) => decision.IsTrue;
    public static implicit operator Decision(bool value) => new(value);
}
