using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace HPD.Base;

/// <summary>Projects opaque generated identities into inert deterministic graph evidence.</summary>
public static class BaseGeneratedGraphEvidence
{
    /// <summary>Gets the exact installed L50 operation checksum.</summary>
    /// <typeparam name="TRequest">The closed request type.</typeparam>
    /// <typeparam name="TResult">The closed result type.</typeparam>
    /// <param name="identity">The opaque generated operation identity.</param>
    public static string ModuleMutation<TRequest, TResult>(
        BaseGeneratedModuleMutationIdentity<TRequest, TResult> identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return Convert.ToHexStringLower(identity.Checksum);
    }

    /// <summary>Gets the exact installed L43 profile checksum.</summary>
    /// <param name="identity">The opaque generated profile identity.</param>
    public static string SelectionProfile(BaseGeneratedSelectionProfileIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return new string(identity.Checksum.AsSpan());
    }

    /// <summary>Gets the exact installed L47 consumer checksum.</summary>
    /// <typeparam name="TSubject">The exported-subject marker.</typeparam>
    /// <param name="identity">The opaque lifecycle-consumer identity.</param>
    public static string LifecycleConsumer<TSubject>(BaseGeneratedSubjectLifecycleConsumerIdentity<TSubject> identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return new string(identity.Checksum.AsSpan());
    }

    /// <summary>Gets the exact installed L48 consumer checksum.</summary>
    /// <typeparam name="TSubject">The exported-subject marker.</typeparam>
    /// <param name="identity">The opaque retirement-consumer identity.</param>
    public static string RetirementConsumer<TSubject>(BaseGeneratedSubjectRetirementConsumerIdentity<TSubject> identity)
    {
        ArgumentNullException.ThrowIfNull(identity);
        return new string(identity.Checksum.AsSpan());
    }

    /// <summary>Gets a deterministic checksum over one finalized registered-read handle.</summary>
    /// <typeparam name="TParameters">The closed parameter type.</typeparam>
    /// <typeparam name="TRow">The closed row type.</typeparam>
    /// <param name="handle">The opaque executable registered-read handle.</param>
    public static string RegisteredRead<TParameters, TRow>(BaseReadHandle<TParameters, TRow> handle)
    {
        ArgumentNullException.ThrowIfNull(handle);
        BaseReadDefinition<TParameters, TRow> definition = handle.Definition;
        var writer = new ArrayBufferWriter<byte>();
        Write(writer, definition.Id);
        Write(writer, definition.ParameterSerializerContractChecksum);
        Write(writer, definition.RowSerializerContractChecksum);
        Write(writer, JsonSerializer.SerializeToUtf8Bytes(
            definition.Plan, HPDBaseJsonSerializerContext.Default.BaseRelationalReadPlan));
        return Convert.ToHexStringLower(SHA256.HashData(writer.WrittenSpan));
    }

    private static void Write(ArrayBufferWriter<byte> writer, string value) =>
        Write(writer, Encoding.UTF8.GetBytes(value));

    private static void Write(ArrayBufferWriter<byte> writer, ReadOnlySpan<byte> value)
    {
        Span<byte> length = writer.GetSpan(sizeof(uint));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(length, checked((uint)value.Length));
        writer.Advance(sizeof(uint));
        writer.Write(value);
    }
}
