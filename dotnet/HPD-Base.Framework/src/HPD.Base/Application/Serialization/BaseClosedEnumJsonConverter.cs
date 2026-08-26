using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPD.Base;

/// <summary>Serializes one closed enum using source-generated exact wire authority.</summary>
/// <typeparam name="TEnum">The closed enum type bound into the generated schema.</typeparam>
[BaseSerializerConverter("hpd.base.closed-enum-json", 3)]
public sealed class BaseClosedEnumJsonConverter<TEnum> : JsonConverter<TEnum>
    where TEnum : struct, Enum
{
    private readonly BaseClosedEnumGeneratedAuthority<TEnum> _authority = BaseClosedEnumGeneratedContract.Resolve<TEnum>();

    /// <inheritdoc />
    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String || reader.GetString() is not { } value || !_authority.FromWire.TryGetValue(value, out TEnum result))
            throw new JsonException(BaseSchemaErrorCodes.ContractInvalid);
        return result;
    }

    /// <inheritdoc />
    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        if (!_authority.ToWire.TryGetValue(value, out string? wire)) throw new JsonException(BaseSchemaErrorCodes.ContractInvalid);
        writer.WriteStringValue(wire);
    }
}

/// <summary>Receives closed enum authority emitted by the Base source generator.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public static class BaseClosedEnumGeneratedContract
{
    private static readonly ConcurrentDictionary<Type, object> Authorities = new();

    /// <summary>Registers one exact generated enum-to-wire table.</summary>
    /// <typeparam name="TEnum">The closed enum whose generated wire authority is being registered.</typeparam>
    /// <param name="values">The complete ordered set of declared enum values.</param>
    /// <param name="wireLiterals">The exact ordinal wire literal corresponding to each declared value.</param>
    public static void Register<TEnum>(TEnum[] values, string[] wireLiterals) where TEnum : struct, Enum
    {
        ArgumentNullException.ThrowIfNull(values);
        ArgumentNullException.ThrowIfNull(wireLiterals);
        if (values.Length is < 1 or > 256 || values.Length != wireLiterals.Length || values.Distinct().Count() != values.Length)
            throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);
        var fromWire = new Dictionary<string, TEnum>(StringComparer.Ordinal);
        var toWire = new Dictionary<TEnum, string>();
        for (int index = 0; index < values.Length; index++)
        {
            string wire = wireLiterals[index] ?? throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);
            if (string.IsNullOrEmpty(wire) || wire.Length > 128 || !wire.IsNormalized(NormalizationForm.FormC) ||
                wire.Any(static character => char.IsControl(character) || char.IsSurrogate(character)) ||
                !fromWire.TryAdd(new string(wire.AsSpan()), values[index]) || !toWire.TryAdd(values[index], new string(wire.AsSpan())))
                throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);
        }
        var authority = new BaseClosedEnumGeneratedAuthority<TEnum>(fromWire, toWire);
        object installed = Authorities.GetOrAdd(typeof(TEnum), authority);
        if (installed is not BaseClosedEnumGeneratedAuthority<TEnum> existing || !Equivalent(existing.ToWire, authority.ToWire))
            throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);
    }

    internal static BaseClosedEnumGeneratedAuthority<TEnum> Resolve<TEnum>() where TEnum : struct, Enum =>
        Authorities.TryGetValue(typeof(TEnum), out object? authority) && authority is BaseClosedEnumGeneratedAuthority<TEnum> typed
            ? typed
            : throw new InvalidOperationException(BaseSchemaErrorCodes.ContractInvalid);

    internal static bool TryGetWire(Type enumType, object value, out string wire)
    {
        wire = string.Empty;
        return Authorities.TryGetValue(enumType, out object? authority)
            && authority is IBaseClosedEnumGeneratedAuthority untyped
            && untyped.TryGetWire(value, out wire);
    }

    private static bool Equivalent<TEnum>(IReadOnlyDictionary<TEnum, string> left, IReadOnlyDictionary<TEnum, string> right) where TEnum : struct, Enum =>
        left.Count == right.Count && left.All(item => right.TryGetValue(item.Key, out string? wire) && string.Equals(item.Value, wire, StringComparison.Ordinal));
}

internal interface IBaseClosedEnumGeneratedAuthority
{
    bool TryGetWire(object value, out string wire);
}

internal sealed class BaseClosedEnumGeneratedAuthority<TEnum> : IBaseClosedEnumGeneratedAuthority where TEnum : struct, Enum
{
    internal BaseClosedEnumGeneratedAuthority(Dictionary<string, TEnum> fromWire, Dictionary<TEnum, string> toWire)
    {
        FromWire = fromWire;
        ToWire = toWire;
    }

    internal IReadOnlyDictionary<string, TEnum> FromWire { get; }
    internal IReadOnlyDictionary<TEnum, string> ToWire { get; }
    bool IBaseClosedEnumGeneratedAuthority.TryGetWire(object value, out string wire)
    {
        if (value is TEnum typed && ToWire.TryGetValue(typed, out string? found))
        {
            wire = found;
            return true;
        }
        wire = string.Empty;
        return false;
    }
}
