using System.Buffers;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Base;

/// <summary>Defines all independent resource ceilings for canonical BASE JSON.</summary>
public sealed record BaseCanonicalJsonLimits
{
    /// <summary>Gets the maximum encoded canonical byte count.</summary>
    public required int MaximumCanonicalBytes { get; init; }
    /// <summary>Gets the maximum container nesting depth.</summary>
    public required int MaximumDepth { get; init; }
    /// <summary>Gets the maximum number of values and property names.</summary>
    public required int MaximumTotalNodes { get; init; }
    /// <summary>Gets the maximum total UTF-8 bytes in string values.</summary>
    public required int MaximumTotalStringUtf8Bytes { get; init; }
    /// <summary>Gets the maximum total UTF-8 bytes in property names.</summary>
    public required int MaximumTotalNameUtf8Bytes { get; init; }
    /// <summary>Gets the maximum items in each array.</summary>
    public required int MaximumArrayItemsPerContainer { get; init; }
    /// <summary>Gets the maximum properties in each object.</summary>
    public required int MaximumObjectPropertiesPerContainer { get; init; }
}

/// <summary>Contains immutable bytes admitted by <c>hpd.base.canonical-json.v1</c>.</summary>
[JsonConverter(typeof(BaseCanonicalJsonConverter))]
public readonly struct BaseCanonicalJson : IEquatable<BaseCanonicalJson>
{
    private readonly byte[]? _utf8;
    private BaseCanonicalJson(byte[] utf8) => _utf8 = utf8;

    /// <summary>Gets a defensive copy of the canonical UTF-8 bytes.</summary>
    public ReadOnlyMemory<byte> Utf8 => _utf8?.ToArray() ?? throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);
    /// <summary>Gets the SHA-256 checksum of the canonical bytes.</summary>
    public BaseSchemaAuthorityChecksum Checksum => BaseSchemaAuthorityChecksum.Create(SHA256.HashData(_utf8 ?? throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid)));
    /// <summary>Gets whether this value was constructed successfully.</summary>
    public bool IsValid => _utf8 is not null;

    /// <summary>Parses canonical JSON and rejects any semantically equivalent noncanonical spelling.</summary>
    public static BaseCanonicalJson ParseAndValidate(ReadOnlySpan<byte> utf8, BaseCanonicalJsonLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ValidateLimits(limits);
        if (utf8.Length > limits.MaximumCanonicalBytes) throw new FormatException(BaseSchemaErrorCodes.ScalarConstraintViolated);
        JsonDocumentOptions options = new() { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = limits.MaximumDepth };
        using JsonDocument document = JsonDocument.Parse(utf8.ToArray(), options);
        var writer = new ArrayBufferWriter<byte>(Math.Min(utf8.Length, limits.MaximumCanonicalBytes));
        var accounting = new Accounting(limits);
        Write(document.RootElement, writer, 1, accounting);
        if (!writer.WrittenSpan.SequenceEqual(utf8)) throw new FormatException(BaseSchemaErrorCodes.ScalarConstraintViolated);
        return new BaseCanonicalJson(writer.WrittenSpan.ToArray());
    }

    internal static byte[] Canonicalize(ReadOnlySpan<byte> utf8, BaseCanonicalJsonLimits limits)
    {
        ArgumentNullException.ThrowIfNull(limits);
        ValidateLimits(limits);
        if (utf8.Length > limits.MaximumCanonicalBytes) throw new FormatException(BaseSchemaErrorCodes.ScalarConstraintViolated);
        JsonDocumentOptions options = new() { AllowTrailingCommas = false, CommentHandling = JsonCommentHandling.Disallow, MaxDepth = limits.MaximumDepth };
        using JsonDocument document = JsonDocument.Parse(utf8.ToArray(), options);
        var writer = new ArrayBufferWriter<byte>(Math.Min(utf8.Length, limits.MaximumCanonicalBytes));
        var accounting = new Accounting(limits);
        Write(document.RootElement, writer, 1, accounting);
        if (writer.WrittenCount > limits.MaximumCanonicalBytes) throw new FormatException(BaseSchemaErrorCodes.ScalarConstraintViolated);
        return writer.WrittenSpan.ToArray();
    }

    /// <inheritdoc />
    public bool Equals(BaseCanonicalJson other) => IsValid && other.IsValid && _utf8!.AsSpan().SequenceEqual(other._utf8);
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is BaseCanonicalJson other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => !IsValid ? 0 : System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(SHA256.HashData(_utf8!));
    /// <summary>Compares two canonical JSON values.</summary>
    public static bool operator ==(BaseCanonicalJson left, BaseCanonicalJson right) => left.Equals(right);
    /// <summary>Compares two canonical JSON values.</summary>
    public static bool operator !=(BaseCanonicalJson left, BaseCanonicalJson right) => !left.Equals(right);

    private static void Write(JsonElement value, IBufferWriter<byte> writer, int depth, Accounting accounting)
    {
        accounting.Node(); if (depth > accounting.Limits.MaximumDepth) throw Invalid();
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                JsonProperty[] properties = value.EnumerateObject().ToArray();
                if (properties.Length > accounting.Limits.MaximumObjectPropertiesPerContainer || properties.Select(static item => item.Name).Distinct(StringComparer.Ordinal).Count() != properties.Length) throw Invalid();
                Array.Sort(properties, static (left, right) => CompareUtf8(left.Name, right.Name));
                Byte(writer, (byte)'{');
                for (int index = 0; index < properties.Length; index++)
                {
                    if (index != 0) Byte(writer, (byte)','); accounting.Name(properties[index].Name); String(writer, properties[index].Name); Byte(writer, (byte)':'); Write(properties[index].Value, writer, depth + 1, accounting);
                }
                Byte(writer, (byte)'}'); return;
            case JsonValueKind.Array:
                JsonElement[] items = value.EnumerateArray().ToArray(); if (items.Length > accounting.Limits.MaximumArrayItemsPerContainer) throw Invalid();
                Byte(writer, (byte)'['); for (int index = 0; index < items.Length; index++) { if (index != 0) Byte(writer, (byte)','); Write(items[index], writer, depth + 1, accounting); } Byte(writer, (byte)']'); return;
            case JsonValueKind.String:
                string text = value.GetString()!; accounting.String(text); String(writer, text); return;
            case JsonValueKind.Number: Bytes(writer, CanonicalNumber(value.GetRawText())); return;
            case JsonValueKind.True: Bytes(writer, "true"u8); return;
            case JsonValueKind.False: Bytes(writer, "false"u8); return;
            case JsonValueKind.Null: Bytes(writer, "null"u8); return;
            default: throw Invalid();
        }
    }

    private static byte[] CanonicalNumber(string text)
    {
        if (text.Contains('e', StringComparison.OrdinalIgnoreCase) || text.StartsWith('+')) throw Invalid();
        bool negative = text.StartsWith('-'); int start = negative ? 1 : 0; int dot = text.IndexOf('.', start);
        string whole = dot < 0 ? text[start..] : text[start..dot]; string fraction = dot < 0 ? string.Empty : text[(dot + 1)..];
        if (whole.Length == 0 || whole.Any(static item => item is < '0' or > '9') || fraction.Any(static item => item is < '0' or > '9') || dot >= 0 && fraction.Length == 0 || whole.Length > 1 && whole[0] == '0' || fraction.Length > 28) throw Invalid();
        fraction = fraction.TrimEnd('0'); string digits = (whole + fraction).TrimStart('0');
        if (digits.Length == 0) return "0"u8.ToArray();
        if (!UInt128.TryParse(digits, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out UInt128 magnitude)
            || magnitude > (negative ? (UInt128)Int128.MaxValue + 1 : (UInt128)Int128.MaxValue)) throw Invalid();
        string canonical = (negative ? "-" : string.Empty) + (whole.TrimStart('0') is { Length: > 0 } admittedWhole ? admittedWhole : "0") + (fraction.Length == 0 ? string.Empty : "." + fraction);
        return canonical.Select(static item => checked((byte)item)).ToArray();
    }

    private static void String(IBufferWriter<byte> writer, string value)
    {
        Byte(writer, (byte)'"');
        for (int index = 0; index < value.Length; index++)
        {
            int scalar = value[index];
            if (char.IsHighSurrogate(value[index]))
            {
                if (++index >= value.Length || !char.IsLowSurrogate(value[index])) throw Invalid();
                scalar = char.ConvertToUtf32(value[index - 1], value[index]);
            }
            else if (char.IsLowSurrogate(value[index])) throw Invalid();
            switch (scalar)
            {
                case 0x22: Bytes(writer, "\\\""u8); break; case 0x5c: Bytes(writer, "\\\\"u8); break;
                case 0x08: Bytes(writer, "\\b"u8); break; case 0x09: Bytes(writer, "\\t"u8); break; case 0x0a: Bytes(writer, "\\n"u8); break; case 0x0c: Bytes(writer, "\\f"u8); break; case 0x0d: Bytes(writer, "\\r"u8); break;
                default:
                    if (scalar < 0x20) Bytes(writer, new byte[] { (byte)'\\', (byte)'u', (byte)'0', (byte)'0', Hex(scalar >> 4), Hex(scalar & 15) });
                    else Bytes(writer, EncodeScalar(scalar));
                    break;
            }
        }
        Byte(writer, (byte)'"');
    }

    private static int CompareUtf8(string left, string right) { byte[] a = BaseStrictUtf8.Encode(left); byte[] b = BaseStrictUtf8.Encode(right); return a.AsSpan().SequenceCompareTo(b); }
    private static byte[] EncodeScalar(int scalar) => BaseStrictUtf8.Encode(char.ConvertFromUtf32(scalar));
    private static byte Hex(int value) => (byte)(value < 10 ? '0' + value : 'a' + value - 10);
    private static void Byte(IBufferWriter<byte> writer, byte value) { Span<byte> span = writer.GetSpan(1); span[0] = value; writer.Advance(1); }
    private static void Bytes(IBufferWriter<byte> writer, ReadOnlySpan<byte> value) { value.CopyTo(writer.GetSpan(value.Length)); writer.Advance(value.Length); }
    private static FormatException Invalid() => new(BaseSchemaErrorCodes.ScalarConstraintViolated);
    private static void ValidateLimits(BaseCanonicalJsonLimits value) { if (value.MaximumCanonicalBytes < 1 || value.MaximumDepth < 1 || value.MaximumTotalNodes < 1 || value.MaximumTotalStringUtf8Bytes < 1 || value.MaximumTotalNameUtf8Bytes < 1 || value.MaximumArrayItemsPerContainer < 1 || value.MaximumObjectPropertiesPerContainer < 1) throw new ArgumentOutOfRangeException(nameof(value)); }

    private sealed class Accounting(BaseCanonicalJsonLimits limits)
    {
        private int _nodes; private int _strings; private int _names;
        internal BaseCanonicalJsonLimits Limits { get; } = limits;
        internal void Node() { if (checked(++_nodes) > Limits.MaximumTotalNodes) throw Invalid(); }
        internal void String(string value) { _strings = checked(_strings + BaseStrictUtf8.GetByteCount(value)); if (_strings > Limits.MaximumTotalStringUtf8Bytes) throw Invalid(); }
        internal void Name(string value) { Node(); _names = checked(_names + BaseStrictUtf8.GetByteCount(value)); if (_names > Limits.MaximumTotalNameUtf8Bytes) throw Invalid(); }
    }
}

/// <summary>Provides the reflection-free serializer contract for canonical BASE JSON.</summary>
public sealed class BaseCanonicalJsonConverter : JsonConverter<BaseCanonicalJson>
{
    private static readonly BaseCanonicalJsonLimits PlatformLimits = new()
    {
        MaximumCanonicalBytes = 1_048_576, MaximumDepth = 64, MaximumTotalNodes = 65_536,
        MaximumTotalStringUtf8Bytes = 1_048_576, MaximumTotalNameUtf8Bytes = 1_048_576,
        MaximumArrayItemsPerContainer = 16_384, MaximumObjectPropertiesPerContainer = 16_384,
    };

    /// <inheritdoc />
    public override BaseCanonicalJson Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using JsonDocument document = JsonDocument.ParseValue(ref reader);
        return BaseCanonicalJson.ParseAndValidate(BaseStrictUtf8.Encode(document.RootElement.GetRawText()), PlatformLimits);
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, BaseCanonicalJson value, JsonSerializerOptions options) => writer.WriteRawValue(value.Utf8.Span, skipInputValidation: false);
}
