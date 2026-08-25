namespace HPD.Base;

/// <summary>
/// Customizes one property in a generated BASE collection contract.
/// </summary>
[AttributeUsage(AttributeTargets.Property, Inherited = false)]
public sealed class BaseFieldAttribute(string id) : Attribute
{
    /// <summary>
    /// Gets the stable logical field identifier.
    /// </summary>
    public string Id { get; } = id;

    /// <summary>
    /// Gets or sets whether the field is omitted from the generated contract.
    /// </summary>
    public bool Ignore { get; set; }

    /// <summary>
    /// Gets or sets the supported query operations.
    /// </summary>
    public BaseFieldOperator Operators { get; set; } = BaseFieldOperator.Equal;

    /// <summary>Gets or sets explicit canonical field presence.</summary>
    public BaseFieldPresence Presence { get; set; }

    /// <summary>Gets or sets explicit canonical field nullability.</summary>
    public BaseFieldNullability Nullability { get; set; }

    /// <summary>Gets or sets the minimum canonical UTF-8 byte count.</summary>
    public int MinimumUtf8Bytes { get; set; }

    /// <summary>Gets or sets the maximum canonical UTF-8 byte count.</summary>
    public int MaximumUtf8Bytes { get; set; } = -1;

    /// <summary>Gets or sets the validation-only normalization requirement.</summary>
    public BaseStringNormalizationRequirement StringNormalization { get; set; }

    /// <summary>Gets or sets the minimum signed 64-bit value.</summary>
    public long MinimumInt64 { get; set; } = long.MinValue;

    /// <summary>Gets or sets whether <see cref="MinimumInt64"/> is present.</summary>
    public bool HasMinimumInt64 { get; set; }

    /// <summary>Gets or sets the maximum signed 64-bit value.</summary>
    public long MaximumInt64 { get; set; } = long.MinValue;

    /// <summary>Gets or sets whether <see cref="MaximumInt64"/> is present.</summary>
    public bool HasMaximumInt64 { get; set; }

    /// <summary>Gets or sets the minimum signed 32-bit value.</summary>
    public int MinimumInt32 { get; set; } = int.MinValue;

    /// <summary>Gets or sets whether <see cref="MinimumInt32"/> is present.</summary>
    public bool HasMinimumInt32 { get; set; }

    /// <summary>Gets or sets the maximum signed 32-bit value.</summary>
    public int MaximumInt32 { get; set; } = int.MinValue;

    /// <summary>Gets or sets whether <see cref="MaximumInt32"/> is present.</summary>
    public bool HasMaximumInt32 { get; set; }

    /// <summary>Gets or sets the minimum unsigned 32-bit value.</summary>
    public uint MinimumUInt32 { get; set; } = uint.MaxValue;

    /// <summary>Gets or sets whether <see cref="MinimumUInt32"/> is present.</summary>
    public bool HasMinimumUInt32 { get; set; }

    /// <summary>Gets or sets the maximum unsigned 32-bit value.</summary>
    public uint MaximumUInt32 { get; set; } = uint.MaxValue;

    /// <summary>Gets or sets whether <see cref="MaximumUInt32"/> is present.</summary>
    public bool HasMaximumUInt32 { get; set; }

    /// <summary>Gets or sets the minimum unsigned 64-bit value.</summary>
    public ulong MinimumUInt64 { get; set; } = ulong.MaxValue;

    /// <summary>Gets or sets whether <see cref="MinimumUInt64"/> is present.</summary>
    public bool HasMinimumUInt64 { get; set; }

    /// <summary>Gets or sets the maximum unsigned 64-bit value.</summary>
    public ulong MaximumUInt64 { get; set; } = ulong.MaxValue;

    /// <summary>Gets or sets whether <see cref="MaximumUInt64"/> is present.</summary>
    public bool HasMaximumUInt64 { get; set; }

    /// <summary>Gets or sets the minimum canonical reduced decimal token.</summary>
    public string? MinimumDecimal { get; set; }

    /// <summary>Gets or sets the maximum canonical reduced decimal token.</summary>
    public string? MaximumDecimal { get; set; }

    /// <summary>Gets or sets the exact ordinal enum wire literals.</summary>
    public string[] AllowedEnumLiterals { get; set; } = [];

    /// <summary>Gets or sets the minimum collection item count.</summary>
    public int MinimumCollectionItems { get; set; }

    /// <summary>Gets or sets the maximum collection item count.</summary>
    public int MaximumCollectionItems { get; set; } = -1;

    /// <summary>Gets or sets the maximum canonical JSON byte count.</summary>
    public int MaximumCanonicalJsonBytes { get; set; } = -1;

    /// <summary>Gets or sets the admitted canonical JSON root shape.</summary>
    public BaseJsonShape JsonShape { get; set; }

    /// <summary>Gets or sets the maximum canonical JSON depth.</summary>
    public int MaximumJsonDepth { get; set; } = -1;

    /// <summary>Gets or sets the maximum items in each canonical JSON array.</summary>
    public int MaximumJsonArrayItems { get; set; } = -1;

    /// <summary>Gets or sets the maximum properties in each canonical JSON object.</summary>
    public int MaximumJsonObjectProperties { get; set; } = -1;

    /// <summary>Gets or sets the maximum total canonical JSON node count.</summary>
    public int MaximumJsonTotalNodes { get; set; } = -1;

    /// <summary>Gets or sets the maximum total UTF-8 bytes in canonical JSON string values.</summary>
    public int MaximumJsonTotalStringUtf8Bytes { get; set; } = -1;

    /// <summary>Gets or sets the maximum total UTF-8 bytes in canonical JSON property names.</summary>
    public int MaximumJsonTotalNameUtf8Bytes { get; set; } = -1;

    /// <summary>Gets or sets the mandatory decoded-byte maximum for a binary field.</summary>
    public int MaximumBytes { get; set; }

}
