namespace HPD.Math.Core;

/// <summary>
/// Executable yes/no result for decidable predicates.
/// </summary>
public readonly record struct Decision(bool IsTrue)
{
    public static Decision True => new(true);
    public static Decision False => new(false);

    public static implicit operator bool(Decision value) => value.IsTrue;
    public static implicit operator Decision(bool value) => new(value);
}
