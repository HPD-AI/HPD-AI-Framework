namespace HPD.Payments.Primitives.Identity;

/// <summary>Represents the bounded immutable <c>OwnerGeneration</c> vocabulary value; its default form is invalid unless explicitly documented otherwise.</summary>
public readonly record struct OwnerGeneration
{
    /// <summary>Gets the validated <c>Value</c> component; it does not imply ambient context or mutation authority.</summary>
    public ulong Value { get; }
    /// <summary>Gets whether the value satisfies its required-field and bound invariants; a default value is invalid.</summary>
    public bool IsValid => Value > 0;
    private OwnerGeneration(ulong value) => Value = value;
    /// <summary>Validates the supplied components and returns a value without throwing for invalid input.</summary>
    public static bool TryCreate(ulong value, out OwnerGeneration generation) { generation = value == 0 ? default : new(value); return generation.IsValid; }
    /// <summary>Creates a validated value and rejects missing, unknown, or out-of-bound components.</summary>
    public static OwnerGeneration Create(ulong value) => TryCreate(value, out var result) ? result : throw new ArgumentOutOfRangeException(nameof(value));
    /// <summary>Returns the next monotone generation when incrementing does not overflow.</summary>
    public bool TryNext(out OwnerGeneration next)
    {
        next = default;
        return Value != ulong.MaxValue && TryCreate(Value + 1, out next);
    }
}

/// <summary>Represents the bounded immutable <c>Revision</c> vocabulary value; its default form is invalid unless explicitly documented otherwise.</summary>
public readonly record struct Revision
{
    /// <summary>Gets the validated <c>Kind</c> component; it does not imply ambient context or mutation authority.</summary>
    public string Kind { get; }
    /// <summary>Gets the validated <c>Value</c> component; it does not imply ambient context or mutation authority.</summary>
    public ulong Value { get; }
    /// <summary>Gets whether the value satisfies its required-field and bound invariants; a default value is invalid.</summary>
    public bool IsValid => Kind is not null && Value > 0;
    private Revision(string kind, ulong value) => (Kind, Value) = (kind, value);
    /// <summary>Validates the supplied components and returns a value without throwing for invalid input.</summary>
    public static bool TryCreate(string? kind, ulong value, out Revision revision)
    {
        revision = default;
        if (!ScopeId.TryComponent(kind, out var k) || value == 0) return false;
        revision = new(k, value); return true;
    }
    /// <summary>Creates a validated value and rejects missing, unknown, or out-of-bound components.</summary>
    public static Revision Create(string kind, ulong value) => TryCreate(kind, value, out var result) ? result : throw new ArgumentException("Invalid revision.");
}

/// <summary>Represents the bounded immutable <c>ContractVersion</c> vocabulary value; its default form is invalid unless explicitly documented otherwise.</summary>
public readonly record struct ContractVersion(ushort Major, ushort Minor)
{
    /// <summary>Gets whether the value satisfies its required-field and bound invariants; a default value is invalid.</summary>
    public bool IsValid => Major > 0;
    /// <summary>Creates a validated value and rejects missing, unknown, or out-of-bound components.</summary>
    public static ContractVersion Create(ushort major, ushort minor) => major > 0 ? new(major, minor) : throw new ArgumentOutOfRangeException(nameof(major));
}
