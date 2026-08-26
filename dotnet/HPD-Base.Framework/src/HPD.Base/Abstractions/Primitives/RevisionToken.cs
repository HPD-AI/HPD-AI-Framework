namespace HPD.Base;

/// <summary>
/// Carries an opaque optimistic concurrency token.
/// </summary>
[System.Text.Json.Serialization.JsonConverter(typeof(RevisionTokenJsonConverter))]
public readonly record struct RevisionToken
{
    /// <summary>Gets the maximum canonical UTF-8 byte length.</summary>
    public const int MaximumUtf8Bytes = 512;

    /// <summary>Creates one validated, immutable opaque revision token.</summary>
    /// <param name="value">The nonempty NFC token without control, surrogate, or whitespace characters.</param>
    public RevisionToken(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (!IsCanonical(value)) throw new ArgumentException("The revision token is not canonical.", nameof(value));
        Value = new string(value.AsSpan());
    }

    /// <summary>Gets the canonical opaque value.</summary>
    public string Value { get; }

    /// <summary>Gets whether this value satisfies the closed revision-token grammar.</summary>
    public bool IsValid => IsCanonical(Value);

    private static bool IsCanonical(string? value) => !string.IsNullOrEmpty(value) &&
        value.IsNormalized(System.Text.NormalizationForm.FormC) &&
        !value.Any(static character => char.IsControl(character) || char.IsSurrogate(character) || char.IsWhiteSpace(character)) &&
        BaseStrictUtf8.GetByteCount(value) <= MaximumUtf8Bytes;
}
