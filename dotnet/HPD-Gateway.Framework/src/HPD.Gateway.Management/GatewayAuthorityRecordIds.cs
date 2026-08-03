using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using HPD.Base;

namespace HPD.Gateway.Management;

internal static class GatewayAuthorityRecordIds
{
    internal const string Version = "gateway.management.record-id.v1";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);

    internal static RecordId TargetOwnership(string managementAuthorityId, string targetNodeId) =>
        Create("target-ownership", managementAuthorityId, targetNodeId);

    internal static RecordId NodeDeliveryAuthority(string managementAuthorityId, string targetNodeId) =>
        Create("delivery-authority", managementAuthorityId, targetNodeId);

    internal static RecordId DesiredState(string managementAuthorityId, string targetNodeId) =>
        Create("desired-state", managementAuthorityId, targetNodeId);

    internal static RecordId CommandFact(string role, string namespaceId, string operation, string idempotencyKey, params string[] additional) =>
        Create(role, [namespaceId, operation, idempotencyKey, .. additional]);

    internal static bool IsCanonicalComponent(string? value)
    {
        try { Validate(value!, nameof(value)); return true; }
        catch (ArgumentException) { return false; }
    }

    private static RecordId Create(string purpose, params string[] values)
    {
        Validate(purpose, nameof(purpose));
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, Version);
        Append(hash, purpose);
        foreach (string value in values)
        {
            Validate(value, nameof(values));
            Append(hash, value);
        }

        string id = "gwm." + purpose + "." + Convert.ToHexStringLower(hash.GetHashAndReset());
        return RecordId.Create(id);
    }

    private static void Append(IncrementalHash hash, string value)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, utf8.Length);
        hash.AppendData(length);
        hash.AppendData(utf8);
    }

    private static void Validate(string value, string parameter)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameter);
        int bytes;
        try { bytes = StrictUtf8.GetByteCount(value); }
        catch (EncoderFallbackException exception) { throw new ArgumentException("Authority identity inputs must be valid UTF-8 text.", parameter, exception); }
        if (bytes > 256 || !value.IsNormalized(NormalizationForm.FormC) || value.Any(char.IsControl))
            throw new ArgumentException("Authority identity inputs must be bounded canonical text.", parameter);
    }
}
