using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace HPD.Base;

internal sealed class DefaultBaseSchemaPlanProtector : IBaseSchemaPlanProtector
{
    private static readonly byte[] Magic = "HPDBSP01"u8.ToArray();
    private readonly byte[] _key;

    /// <summary>Initializes a new instance.</summary>
    public DefaultBaseSchemaPlanProtector(IOptions<HPDBaseSchemaOptions> options) =>
        _key = options.Value.PlanProtectionKey.ToArray();

    /// <summary>Executes the protect operation.</summary>
    public byte[] Protect(BaseSchemaPlan plan, byte[] providerApplyArtifact)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(providerApplyArtifact);
        EnsureKey();
        byte[] header = Header(plan.ApplicationId, plan.StoreId, plan.PersistedStoreInstanceId, plan.ProviderId);
        byte[] plaintext = Encode(plan, providerApplyArtifact);
        try
        {
            byte[] nonce = RandomNumberGenerator.GetBytes(12);
            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[16];
            using (var aes = new AesGcm(_key, tag.Length)) aes.Encrypt(nonce, plaintext, ciphertext, tag, header);
            using var stream = new MemoryStream();
            stream.Write(Magic); WriteBytes(stream, header); stream.Write(nonce); stream.Write(tag); WriteBytes(stream, ciphertext);
            return stream.ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    /// <summary>Executes the unprotect operation.</summary>
    public OperationResult<BaseSchemaVerifiedPlan> Unprotect(byte[] protectedArtifact)
    {
        ArgumentNullException.ThrowIfNull(protectedArtifact);
        byte[]? plaintext = null;
        BaseSchemaVerifiedPlan? decoded = null;
        try
        {
            EnsureKey();
            using var stream = new MemoryStream(protectedArtifact, writable: false);
            Span<byte> magic = stackalloc byte[Magic.Length];
            stream.ReadExactly(magic);
            if (!magic.SequenceEqual(Magic)) return Invalid();
            byte[] header = ReadBytes(stream, 4_096);
            byte[] nonce = new byte[12]; stream.ReadExactly(nonce);
            byte[] tag = new byte[16]; stream.ReadExactly(tag);
            byte[] ciphertext = ReadBytes(stream, 64 * 1024 * 1024);
            if (stream.Position != stream.Length) return Invalid();
            plaintext = new byte[ciphertext.Length];
            using (var aes = new AesGcm(_key, tag.Length)) aes.Decrypt(nonce, ciphertext, tag, plaintext, header);
            decoded = Decode(plaintext, protectedArtifact);
            byte[] expectedHeader = Header(decoded.Plan.ApplicationId, decoded.Plan.StoreId, decoded.Plan.PersistedStoreInstanceId, decoded.Plan.ProviderId);
            if (!CryptographicOperations.FixedTimeEquals(header, expectedHeader) ||
                !Digest(decoded.ProviderApplyArtifact).Equals(decoded.Plan.ProviderApplyArtifactDigest, StringComparison.Ordinal))
            {
                CryptographicOperations.ZeroMemory(decoded.ProviderApplyArtifact);
                return Invalid();
            }
            return OperationResults.Ok(decoded);
        }
        catch (Exception exception) when (exception is CryptographicException or EndOfStreamException or IOException or InvalidDataException or ArgumentException or InvalidOperationException)
        {
            if (decoded is not null) CryptographicOperations.ZeroMemory(decoded.ProviderApplyArtifact);
            return Invalid();
        }
        finally
        {
            if (plaintext is not null) CryptographicOperations.ZeroMemory(plaintext);
        }
    }

    private void EnsureKey()
    {
        if (_key.Length != 32) throw new InvalidOperationException("Schema plan protection is not configured.");
    }

    private static byte[] Encode(BaseSchemaPlan plan, byte[] providerArtifact)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        Write(writer, plan.PlanId); Write(writer, plan.ApplicationId); Write(writer, plan.StoreId); Write(writer, plan.PersistedStoreInstanceId);
        Write(writer, plan.ProviderId); Write(writer, plan.ProviderVersion); Write(writer, plan.PlannerVersion); writer.Write(plan.ExpectedGeneration);
        WriteNullable(writer, plan.BaselineId); WriteNullable(writer, plan.BaselineChecksum); Write(writer, plan.TargetBaselineId); Write(writer, plan.TargetChecksum);
        writer.Write((int)plan.Classification); writer.Write(plan.Operations.Length);
        foreach (BaseSchemaLogicalOperation operation in plan.Operations)
        { writer.Write((int)operation.Kind); Write(writer, operation.LogicalId); WriteNullable(writer, operation.PreviousName); WriteNullable(writer, operation.TargetName); writer.Write(operation.Destructive); }
        OperationWarning[] warnings = plan.Warnings ?? [];
        writer.Write(warnings.Length);
        foreach (OperationWarning warning in warnings)
        { Write(writer, warning.Code); Write(writer, warning.Message); WriteNullable(writer, warning.Target); WriteNullable(writer, warning.CapabilityPath); }
        writer.Write(plan.RequiresExternalDataMigration); WriteAttestation(writer, plan.ExternalMigrationAttestation); writer.Write(plan.CreatedAt.ToUnixTimeMilliseconds()); writer.Write(plan.ExpiresAt.ToUnixTimeMilliseconds());
        Write(writer, plan.LogicalPlanDigest); Write(writer, plan.ProviderApplyArtifactDigest); writer.Write(providerArtifact.Length); writer.Write(providerArtifact);
        writer.Flush(); return stream.ToArray();
    }

    private static BaseSchemaVerifiedPlan Decode(byte[] bytes, byte[] protectedArtifact)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);
        string planId = Read(reader); string applicationId = Read(reader); string storeId = Read(reader); string instanceId = Read(reader);
        string providerId = Read(reader); string providerVersion = Read(reader); string plannerVersion = Read(reader); long generation = reader.ReadInt64();
        string? baselineId = ReadNullable(reader); string? baselineChecksum = ReadNullable(reader); string targetBaselineId = Read(reader); string targetChecksum = Read(reader);
        var classification = EnumValue<BaseSchemaPlanClassification>(reader.ReadInt32());
        int operationCount = Count(reader, 10_000); var operations = new BaseSchemaLogicalOperation[operationCount];
        for (int index = 0; index < operationCount; index++) operations[index] = new BaseSchemaLogicalOperation
        {
            Kind = EnumValue<BaseSchemaOperationKind>(reader.ReadInt32()), LogicalId = Read(reader), PreviousName = ReadNullable(reader), TargetName = ReadNullable(reader), Destructive = reader.ReadBoolean()
        };
        int warningCount = Count(reader, 10_000); var warnings = new OperationWarning[warningCount];
        for (int index = 0; index < warningCount; index++) warnings[index] = new OperationWarning
        { Code = Read(reader), Message = Read(reader), Target = ReadNullable(reader), CapabilityPath = ReadNullable(reader) };
        bool external = reader.ReadBoolean(); BaseExternalMigrationAttestation? attestation = ReadAttestation(reader); var created = DateTimeOffset.FromUnixTimeMilliseconds(reader.ReadInt64()); var expires = DateTimeOffset.FromUnixTimeMilliseconds(reader.ReadInt64());
        string logicalDigest = Read(reader); string providerDigest = Read(reader); int artifactLength = Count(reader, 64 * 1024 * 1024); byte[] providerArtifact = reader.ReadBytes(artifactLength);
        if (providerArtifact.Length != artifactLength || stream.Position != stream.Length) throw new InvalidDataException();
        var plan = new BaseSchemaPlan
        {
            PlanId = planId, ApplicationId = applicationId, StoreId = storeId, PersistedStoreInstanceId = instanceId,
            ProviderId = providerId, ProviderVersion = providerVersion, PlannerVersion = plannerVersion, ExpectedGeneration = generation,
            BaselineId = baselineId, BaselineChecksum = baselineChecksum, TargetBaselineId = targetBaselineId, TargetChecksum = targetChecksum,
            Classification = classification, Operations = operations, Warnings = warnings.Length == 0 ? null : warnings,
            RequiresExternalDataMigration = external, ExternalMigrationAttestation = attestation, CreatedAt = created, ExpiresAt = expires,
            LogicalPlanDigest = logicalDigest, ProviderApplyArtifactDigest = providerDigest, ProtectedArtifact = protectedArtifact.ToArray()
        };
        return new BaseSchemaVerifiedPlan { Plan = plan, ProviderApplyArtifact = providerArtifact };
    }

    internal static string Digest(byte[] value) => Convert.ToHexStringLower(SHA256.HashData(value));
    private static byte[] Header(params string[] values) { using var stream = new MemoryStream(); using var writer = new BinaryWriter(stream, Encoding.UTF8, true); foreach (string value in values) Write(writer, value); writer.Flush(); return stream.ToArray(); }
    private static void Write(BinaryWriter writer, string value) { byte[] bytes = Encoding.UTF8.GetBytes(value); if (bytes.Length > 16_384) throw new InvalidDataException(); writer.Write(bytes.Length); writer.Write(bytes); }
    private static string Read(BinaryReader reader) { int length = Count(reader, 16_384); byte[] bytes = reader.ReadBytes(length); if (bytes.Length != length) throw new EndOfStreamException(); return Encoding.UTF8.GetString(bytes); }
    private static void WriteNullable(BinaryWriter writer, string? value) { writer.Write(value is not null); if (value is not null) Write(writer, value); }
    private static string? ReadNullable(BinaryReader reader) => reader.ReadBoolean() ? Read(reader) : null;
    private static void WriteAttestation(BinaryWriter writer, BaseExternalMigrationAttestation? value)
    {
        writer.Write(value is not null); if (value is null) return;
        Write(writer, value.AttestationId); Write(writer, value.ApplicationId); Write(writer, value.StoreId); Write(writer, value.SourceChecksum); Write(writer, value.TargetChecksum);
        writer.Write(value.CompletedAt.ToUnixTimeMilliseconds()); Write(writer, value.Tool); Write(writer, value.ToolVersion); Write(writer, value.SignerId); writer.Write(value.AuthenticationTag.Length); writer.Write(value.AuthenticationTag);
    }
    private static BaseExternalMigrationAttestation? ReadAttestation(BinaryReader reader)
    {
        if (!reader.ReadBoolean()) return null;
        string id = Read(reader); string app = Read(reader); string store = Read(reader); string source = Read(reader); string target = Read(reader); DateTimeOffset completed = DateTimeOffset.FromUnixTimeMilliseconds(reader.ReadInt64());
        string tool = Read(reader); string version = Read(reader); string signer = Read(reader); int length = Count(reader, 64); byte[] tag = reader.ReadBytes(length); if (tag.Length != length) throw new EndOfStreamException();
        return new BaseExternalMigrationAttestation { AttestationId = id, ApplicationId = app, StoreId = store, SourceChecksum = source, TargetChecksum = target, CompletedAt = completed, Tool = tool, ToolVersion = version, SignerId = signer, AuthenticationTag = tag };
    }
    private static int Count(BinaryReader reader, int maximum) { int value = reader.ReadInt32(); if (value < 0 || value > maximum) throw new InvalidDataException(); return value; }
    private static T EnumValue<T>(int value) where T : struct, Enum => Enum.IsDefined(typeof(T), value) ? (T)Enum.ToObject(typeof(T), value) : throw new InvalidDataException();
    private static void WriteBytes(Stream stream, byte[] value) { Span<byte> length = stackalloc byte[4]; System.Buffers.Binary.BinaryPrimitives.WriteInt32LittleEndian(length, value.Length); stream.Write(length); stream.Write(value); }
    private static byte[] ReadBytes(Stream stream, int maximum) { Span<byte> length = stackalloc byte[4]; stream.ReadExactly(length); int count = System.Buffers.Binary.BinaryPrimitives.ReadInt32LittleEndian(length); if (count < 0 || count > maximum) throw new InvalidDataException(); byte[] value = new byte[count]; stream.ReadExactly(value); return value; }
    private static OperationResult<BaseSchemaVerifiedPlan> Invalid() => OperationResults.ValidationFailed<BaseSchemaVerifiedPlan>(new BaseError { Code = BaseSchemaErrorCodes.PlanInvalid, Message = "The schema plan artifact is invalid.", Category = ErrorCategory.Validation });
}
