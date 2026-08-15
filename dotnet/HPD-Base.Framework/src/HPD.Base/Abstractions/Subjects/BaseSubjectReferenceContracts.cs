using System.Globalization;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Base;

/// <summary>Identifies the canonical grammar of an exported logical subject identifier.</summary>
public enum BaseSubjectIdKind
{
    /// <summary>Uses NFC-normalized ordinal Unicode scalar text.</summary>
    OrdinalString = 0,
    /// <summary>Uses lowercase RFC 4122 <c>D</c> text.</summary>
    Guid = 1,
    /// <summary>Uses the shortest unsigned 64-bit invariant decimal text.</summary>
    UInt64 = 2,
}

/// <summary>Identifies the logical scope owned by an exported subject contract.</summary>
public enum BaseSubjectScopeKind
{
    /// <summary>The subject is application-global.</summary>
    Global = 0,
    /// <summary>The subject is bound to one tenant.</summary>
    Tenant = 1,
    /// <summary>The subject is bound to one project.</summary>
    Project = 2,
}

/// <summary>Specifies the validity required from a logical subject reference.</summary>
public enum BaseSubjectReferenceRequirement
{
    /// <summary>The subject lifetime and private record must exist.</summary>
    Exists = 0,
    /// <summary>The subject lifetime must exist and satisfy its declared active-state binding.</summary>
    Active = 1,
}

/// <summary>Specifies the validation guarantee required by a subject-reference field.</summary>
public enum BaseSubjectValidationGuarantee
{
    /// <summary>Validation and the source mutation share one authoritative serializable transaction.</summary>
    TransactionSnapshot = 0,
}

/// <summary>Represents a deeply immutable canonical exported-subject identifier.</summary>
public readonly struct BaseSubjectId : IEquatable<BaseSubjectId>
{
    private readonly string? _value;

    private BaseSubjectId(string value)
    {
        _value = value;
    }

    /// <summary>Gets the canonical text.</summary>
    public string Value => _value ?? throw new InvalidOperationException(BaseSubjectErrorCodes.ReferenceInvalid);

    /// <summary>Creates and validates a canonical identifier for the selected grammar.</summary>
    public static BaseSubjectId Create(string value, BaseSubjectIdKind kind, int maximumUtf8Bytes = 256)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (maximumUtf8Bytes is < 1 or > 256) throw new ArgumentOutOfRangeException(nameof(maximumUtf8Bytes));
        string canonical = kind switch
        {
            BaseSubjectIdKind.OrdinalString => CanonicalOrdinal(value),
            BaseSubjectIdKind.Guid => CanonicalGuid(value),
            BaseSubjectIdKind.UInt64 => CanonicalUInt64(value),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
        int bytes = Encoding.UTF8.GetByteCount(canonical);
        if (bytes is < 1 || bytes > maximumUtf8Bytes) throw new ArgumentOutOfRangeException(nameof(value));
        return new BaseSubjectId(canonical);
    }

    /// <summary>Returns a defensive copy of the canonical UTF-8 bytes.</summary>
    public byte[] ToUtf8Bytes() => Encoding.UTF8.GetBytes(Value);

    /// <inheritdoc />
    public bool Equals(BaseSubjectId other) => string.Equals(_value, other._value, StringComparison.Ordinal);
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is BaseSubjectId other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode()
    {
        return _value is null ? 0 : StringComparer.Ordinal.GetHashCode(_value);
    }
    /// <inheritdoc />
    public override string ToString() => _value ?? nameof(BaseSubjectId);

    private static string CanonicalOrdinal(string value)
    {
        ReadOnlySpan<char> remaining = value.AsSpan();
        while (!remaining.IsEmpty)
        {
            System.Buffers.OperationStatus status = Rune.DecodeFromUtf16(remaining, out _, out int consumed);
            if (status != System.Buffers.OperationStatus.Done)
                throw new FormatException(BaseSubjectErrorCodes.ReferenceInvalid);
            remaining = remaining[consumed..];
        }
        if (!value.IsNormalized(NormalizationForm.FormC)) throw new FormatException(BaseSubjectErrorCodes.ReferenceInvalid);
        foreach (Rune rune in value.EnumerateRunes())
            if (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Control) throw new FormatException(BaseSubjectErrorCodes.ReferenceInvalid);
        return value;
    }

    private static string CanonicalGuid(string value)
    {
        if (!Guid.TryParseExact(value, "D", out Guid parsed)) throw new FormatException(BaseSubjectErrorCodes.ReferenceInvalid);
        string canonical = parsed.ToString("D", CultureInfo.InvariantCulture);
        if (!string.Equals(canonical, value, StringComparison.Ordinal)) throw new FormatException(BaseSubjectErrorCodes.ReferenceInvalid);
        return canonical;
    }

    private static string CanonicalUInt64(string value)
    {
        if (!ulong.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out ulong parsed)) throw new FormatException(BaseSubjectErrorCodes.ReferenceInvalid);
        string canonical = parsed.ToString(CultureInfo.InvariantCulture);
        if (!string.Equals(canonical, value, StringComparison.Ordinal)) throw new FormatException(BaseSubjectErrorCodes.ReferenceInvalid);
        return canonical;
    }
}

/// <summary>Represents one opaque 128-bit exported-subject authority epoch.</summary>
public readonly struct BaseSubjectAuthorityEpoch : IEquatable<BaseSubjectAuthorityEpoch>
{
    private readonly ulong _high;
    private readonly ulong _low;
    internal BaseSubjectAuthorityEpoch(ReadOnlySpan<byte> value)
    {
        if (value.Length != 16) throw new ArgumentOutOfRangeException(nameof(value));
        _high = BinaryPrimitives.ReadUInt64BigEndian(value);
        _low = BinaryPrimitives.ReadUInt64BigEndian(value[8..]);
    }
    /// <summary>Returns a defensive copy of the epoch bytes.</summary>
    public byte[] ToArray()
    {
        byte[] value = new byte[16];
        BinaryPrimitives.WriteUInt64BigEndian(value, _high);
        BinaryPrimitives.WriteUInt64BigEndian(value.AsSpan(8), _low);
        return value;
    }
    /// <summary>Returns the canonical unpadded base64url representation.</summary>
    public string ToBase64Url() => BaseSubjectReferenceEncoding.Encode(ToArray());
    internal static BaseSubjectAuthorityEpoch Create() => new(RandomNumberGenerator.GetBytes(16));
    internal static BaseSubjectAuthorityEpoch Parse(string value) => new(BaseSubjectReferenceEncoding.Decode(value));
    /// <inheritdoc />
    public bool Equals(BaseSubjectAuthorityEpoch other) => _high == other._high && _low == other._low;
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is BaseSubjectAuthorityEpoch other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(_high, _low);
    /// <inheritdoc />
    public override string ToString() => nameof(BaseSubjectAuthorityEpoch);
}

/// <summary>Represents one opaque 128-bit logical subject lifetime incarnation.</summary>
public readonly struct BaseSubjectIncarnation : IEquatable<BaseSubjectIncarnation>
{
    private readonly ulong _high;
    private readonly ulong _low;
    internal BaseSubjectIncarnation(ReadOnlySpan<byte> value)
    {
        if (value.Length != 16) throw new ArgumentOutOfRangeException(nameof(value));
        _high = BinaryPrimitives.ReadUInt64BigEndian(value);
        _low = BinaryPrimitives.ReadUInt64BigEndian(value[8..]);
    }
    /// <summary>Returns a defensive copy of the incarnation bytes.</summary>
    public byte[] ToArray()
    {
        byte[] value = new byte[16];
        BinaryPrimitives.WriteUInt64BigEndian(value, _high);
        BinaryPrimitives.WriteUInt64BigEndian(value.AsSpan(8), _low);
        return value;
    }
    /// <summary>Returns the canonical unpadded base64url representation.</summary>
    public string ToBase64Url() => BaseSubjectReferenceEncoding.Encode(ToArray());
    internal static BaseSubjectIncarnation Create() => new(RandomNumberGenerator.GetBytes(16));
    internal static BaseSubjectIncarnation Parse(string value) => new(BaseSubjectReferenceEncoding.Decode(value));
    /// <inheritdoc />
    public bool Equals(BaseSubjectIncarnation other) => _high == other._high && _low == other._low;
    /// <inheritdoc />
    public override bool Equals(object? obj) => obj is BaseSubjectIncarnation other && Equals(other);
    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(_high, _low);
    /// <inheritdoc />
    public override string ToString() => nameof(BaseSubjectIncarnation);
}

/// <summary>References one lifetime of a public logical subject without exposing its private storage.</summary>
/// <typeparam name="TSubject">The generated exported-subject marker type.</typeparam>
[JsonConverter(typeof(BaseSubjectReferenceJsonConverterFactory))]
public readonly record struct BaseSubjectReference<TSubject>
{
    internal BaseSubjectReference(BaseSubjectId subjectId, BaseSubjectAuthorityEpoch authorityEpoch, BaseSubjectIncarnation incarnation)
    {
        SubjectId = subjectId;
        AuthorityEpoch = authorityEpoch;
        Incarnation = incarnation;
    }

    /// <summary>Gets the canonical public subject identifier.</summary>
    public BaseSubjectId SubjectId { get; }
    /// <summary>Gets the opaque authority epoch.</summary>
    public BaseSubjectAuthorityEpoch AuthorityEpoch { get; }
    /// <summary>Gets the opaque subject-lifetime incarnation.</summary>
    public BaseSubjectIncarnation Incarnation { get; }
}

/// <summary>Creates closed JSON converters for generator-discovered subject-reference marker types.</summary>
public sealed class BaseSubjectReferenceJsonConverterFactory : JsonConverterFactory
{
    private static readonly Dictionary<Type, Registration> Converters = [];
    private static readonly Lock Sync = new();

    /// <summary>Registers one generated closed subject-reference type and its subject-ID contract.</summary>
    public static void Register<TSubject>(BaseSubjectIdKind kind, int maximumSubjectIdUtf8Bytes)
    {
        if (!Enum.IsDefined(kind) || maximumSubjectIdUtf8Bytes is < 1 or > 256)
            throw new InvalidOperationException(BaseSubjectErrorCodes.ContractInvalid);
        Type type = typeof(BaseSubjectReference<TSubject>);
        lock (Sync)
        {
            if (Converters.TryGetValue(type, out Registration? existing))
            {
                if (existing.Kind != kind || existing.MaximumSubjectIdUtf8Bytes != maximumSubjectIdUtf8Bytes)
                    throw new InvalidOperationException(BaseSubjectErrorCodes.RegistrationConflict);
                return;
            }
            Converters.Add(type, new Registration(kind, maximumSubjectIdUtf8Bytes, new Converter<TSubject>(kind, maximumSubjectIdUtf8Bytes)));
        }
    }

    /// <inheritdoc />
    public override bool CanConvert(Type typeToConvert) => typeToConvert.IsGenericType && typeToConvert.GetGenericTypeDefinition() == typeof(BaseSubjectReference<>);

    /// <inheritdoc />
    public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        lock (Sync) return Converters.TryGetValue(typeToConvert, out Registration? registration)
            ? registration.Converter
            : throw new NotSupportedException("Subject-reference JSON metadata was not generated for this closed type.");
    }

    private sealed record Registration(
        BaseSubjectIdKind Kind,
        int MaximumSubjectIdUtf8Bytes,
        JsonConverter Converter);

    private sealed class Converter<TSubject>(BaseSubjectIdKind kind, int maximumBytes) : JsonConverter<BaseSubjectReference<TSubject>>
    {
        public override BaseSubjectReference<TSubject> Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartObject ||
                !reader.Read() || reader.TokenType != JsonTokenType.PropertyName || !reader.ValueTextEquals("subjectId") ||
                !reader.Read() || reader.TokenType != JsonTokenType.String)
                throw new JsonException(BaseSubjectErrorCodes.ReferenceInvalid);
            string subject = reader.GetString()!;
            if (!reader.Read() || reader.TokenType != JsonTokenType.PropertyName || !reader.ValueTextEquals("authorityEpoch") ||
                !reader.Read() || reader.TokenType != JsonTokenType.String)
                throw new JsonException(BaseSubjectErrorCodes.ReferenceInvalid);
            string epoch = reader.GetString()!;
            if (!reader.Read() || reader.TokenType != JsonTokenType.PropertyName || !reader.ValueTextEquals("incarnation") ||
                !reader.Read() || reader.TokenType != JsonTokenType.String)
                throw new JsonException(BaseSubjectErrorCodes.ReferenceInvalid);
            string incarnation = reader.GetString()!;
            if (!reader.Read() || reader.TokenType != JsonTokenType.EndObject)
                throw new JsonException(BaseSubjectErrorCodes.ReferenceInvalid);
            try
            {
                return new BaseSubjectReference<TSubject>(
                    BaseSubjectId.Create(subject, kind, maximumBytes),
                    BaseSubjectAuthorityEpoch.Parse(epoch),
                    BaseSubjectIncarnation.Parse(incarnation));
            }
            catch (Exception exception) when (exception is FormatException or ArgumentException)
            {
                throw new JsonException(BaseSubjectErrorCodes.ReferenceInvalid, exception);
            }
        }

        public override void Write(Utf8JsonWriter writer, BaseSubjectReference<TSubject> value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();
            writer.WriteString("subjectId", value.SubjectId.Value);
            writer.WriteString("authorityEpoch", value.AuthorityEpoch.ToBase64Url());
            writer.WriteString("incarnation", value.Incarnation.ToBase64Url());
            writer.WriteEndObject();
        }
    }
}

internal static class BaseSubjectReferenceEncoding
{
    internal static string Encode(ReadOnlySpan<byte> value) => Convert.ToBase64String(value).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    internal static byte[] Decode(string value)
    {
        if (value.Length != 22 || value.Contains('=')) throw new FormatException(BaseSubjectErrorCodes.ReferenceInvalid);
        string padded = value.Replace('-', '+').Replace('_', '/') + "==";
        byte[] bytes = Convert.FromBase64String(padded);
        if (bytes.Length != 16 || !string.Equals(Encode(bytes), value, StringComparison.Ordinal)) throw new FormatException(BaseSubjectErrorCodes.ReferenceInvalid);
        return bytes;
    }

    internal static bool TryRewriteAuthorityEpoch(
        JsonElement value,
        BaseSubjectAuthorityEpoch expected,
        BaseSubjectAuthorityEpoch replacement,
        out JsonElement rewritten)
    {
        rewritten = default;
        if (value.ValueKind != JsonValueKind.Object)
            return false;
        JsonProperty[] properties = value.EnumerateObject().ToArray();
        if (properties.Length != 3
            || !string.Equals(properties[0].Name, "subjectId", StringComparison.Ordinal)
            || !string.Equals(properties[1].Name, "authorityEpoch", StringComparison.Ordinal)
            || !string.Equals(properties[2].Name, "incarnation", StringComparison.Ordinal)
            || properties.Any(static property => property.Value.ValueKind != JsonValueKind.String))
        {
            return false;
        }

        try
        {
            if (!BaseSubjectAuthorityEpoch.Parse(properties[1].Value.GetString()!).Equals(expected))
                return false;
            _ = BaseSubjectReferenceEncoding.Decode(properties[2].Value.GetString()!);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            return false;
        }

        var buffer = new System.Buffers.ArrayBufferWriter<byte>();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("subjectId", properties[0].Value.GetString());
            writer.WriteString("authorityEpoch", replacement.ToBase64Url());
            writer.WriteString("incarnation", properties[2].Value.GetString());
            writer.WriteEndObject();
        }
        using JsonDocument document = JsonDocument.Parse(buffer.WrittenMemory);
        rewritten = document.RootElement.Clone();
        return true;
    }
}

/// <summary>Contains stable exported-subject failure codes.</summary>
public static class BaseSubjectErrorCodes
{
    /// <summary>The exported-subject contract is invalid.</summary>
    public const string ContractInvalid = "base.subject.contractInvalid";
    /// <summary>The exported-subject registration conflicts with another registration.</summary>
    public const string RegistrationConflict = "base.subject.registrationConflict";
    /// <summary>The logical subject reference is invalid.</summary>
    public const string ReferenceInvalid = "base.subject.referenceInvalid";
    /// <summary>Subject validation is operationally unavailable.</summary>
    public const string ValidationUnavailable = "base.subject.validationUnavailable";
    /// <summary>The required validation guarantee is unavailable.</summary>
    public const string GuaranteeUnavailable = "base.subject.guaranteeUnavailable";
    /// <summary>The subject-validation safety envelope was exceeded.</summary>
    public const string BudgetExceeded = "base.subject.budgetExceeded";
    /// <summary>The subject-reference provider returned invalid evidence.</summary>
    public const string ProviderContractInvalid = "base.subject.providerContractInvalid";
    /// <summary>The installed subject authority changed during execution.</summary>
    public const string SchemaGenerationChanged = "base.subject.schemaGenerationChanged";
    /// <summary>The subject-validation transaction conflicted.</summary>
    public const string TransactionConflict = "base.subject.transactionConflict";
    /// <summary>The subject-reference commit outcome is indeterminate.</summary>
    public const string CommitIndeterminate = "base.subject.commitIndeterminate";
    /// <summary>An identified request conflicts with its stored receipt.</summary>
    public const string ReceiptMismatch = "base.subject.receiptMismatch";
}
