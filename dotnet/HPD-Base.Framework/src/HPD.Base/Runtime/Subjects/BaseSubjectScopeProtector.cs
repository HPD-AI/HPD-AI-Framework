using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

internal sealed class BaseSubjectScopeProtector(BaseOpaqueTokenProtector tokens)
{
    private const string DigestPurpose = "hpd.base.subject-scope-index.v1";
    private const string ValuePurpose = "hpd.base.subject-scope-value.v1";
    private static readonly byte[] Binding = SHA256.HashData("hpd.base.subject-scope-binding.v1"u8);

    internal BaseProtectedSubjectScope Protect(BaseOwnedSubjectScopeEvidence scope)
        => Protect(scope, tokens.ActiveKeyId);

    internal BaseProtectedSubjectScope Protect(BaseOwnedSubjectScopeEvidence scope, byte keyId)
    {
        Validate(scope);
        byte[] canonical = scope.Value is null ? [] : Encoding.UTF8.GetBytes(scope.Value);
        byte[] framed = new byte[1 + 4 + canonical.Length]; framed[0] = checked((byte)scope.Kind);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(framed.AsSpan(1, 4), canonical.Length); canonical.CopyTo(framed, 5);
        byte[] digest = tokens.Authenticate(DigestPurpose, keyId, framed);
        string protectedValue = tokens.Protect(ValuePurpose, 1, canonical, Binding, keyId);
        return new BaseProtectedSubjectScope { Kind = scope.Kind, IndexDigest = digest, ProtectedCanonicalValue = Encoding.ASCII.GetBytes(protectedValue) };
    }

    internal bool Matches(BaseProtectedSubjectScope protectedScope, BaseOwnedSubjectScopeEvidence expected)
    {
        try
        {
            BaseOwnedSubjectScopeEvidence? decoded = Unprotect(protectedScope);
            return decoded is not null && decoded.Kind == expected.Kind
                && string.Equals(decoded.Value, expected.Value, StringComparison.Ordinal);
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException) { return false; }
    }

    internal BaseOwnedSubjectScopeEvidence? Unprotect(BaseProtectedSubjectScope protectedScope)
    {
        try
        {
            string token = Encoding.ASCII.GetString(protectedScope.ProtectedCanonicalValue);
            BaseOpaqueTokenResult decoded = tokens.Unprotect(ValuePurpose, 1, token, 0, 256, Binding);
            if (decoded.Status != BaseOpaqueTokenStatus.Valid || decoded.Plaintext is null) return null;
            string? value = decoded.Plaintext.Length == 0 ? null : Encoding.UTF8.GetString(decoded.Plaintext);
            var scope = new BaseOwnedSubjectScopeEvidence { Kind = protectedScope.Kind, Value = value };
            Validate(scope);
            byte keyId = DecodeKeyId(token);
            byte[] canonical = scope.Value is null ? [] : Encoding.UTF8.GetBytes(scope.Value);
            byte[] framed = new byte[1 + 4 + canonical.Length]; framed[0] = checked((byte)scope.Kind);
            System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(framed.AsSpan(1, 4), canonical.Length); canonical.CopyTo(framed, 5);
            byte[] expectedDigest = tokens.Authenticate(DigestPurpose, keyId, framed);
            return CryptographicOperations.FixedTimeEquals(protectedScope.IndexDigest, expectedDigest) ? scope : null;
        }
        catch (Exception exception) when (exception is ArgumentException or InvalidOperationException or FormatException or KeyNotFoundException) { return null; }
    }

    private static byte DecodeKeyId(string token)
    {
        string text = token.Replace('-', '+').Replace('_', '/');
        int remainder = text.Length % 4; if (remainder != 0) text = text.PadRight(text.Length + 4 - remainder, '=');
        byte[] bytes = Convert.FromBase64String(text);
        if (bytes.Length < 2) throw new FormatException("The protected scope token is malformed.");
        return bytes[0];
    }

    private static void Validate(BaseOwnedSubjectScopeEvidence scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        if (!Enum.IsDefined(scope.Kind) || scope.Kind == BaseSubjectScopeKind.Global && scope.Value is not null || scope.Kind != BaseSubjectScopeKind.Global && string.IsNullOrEmpty(scope.Value))
            throw new ArgumentException(BaseSubjectErrorCodes.ContractInvalid, nameof(scope));
        if (scope.Value is { } value && (!value.IsNormalized(NormalizationForm.FormC) || Encoding.UTF8.GetByteCount(value) is < 1 or > 256 || value.Any(char.IsControl)))
            throw new ArgumentException(BaseSubjectErrorCodes.ContractInvalid, nameof(scope));
    }
}
