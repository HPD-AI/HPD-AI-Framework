namespace HPD.Environment.Runtime;

using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using HPD.Environment.Contracts;

internal static class AuthorityEvidenceReader
{
    public const int DefaultMaximumPayloadBytes = 32 * 1024;
    private static readonly ContentType JsonContentType =
        new("application/json");

    public static bool TryRead<T>(
        IReadOnlyList<ProviderExtensionData> extensions,
        ProviderId providerId,
        SchemaId schemaId,
        JsonTypeInfo<T> typeInfo,
        out T? evidence,
        int maximumPayloadBytes = DefaultMaximumPayloadBytes)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(extensions);
        ArgumentNullException.ThrowIfNull(typeInfo);
        if (maximumPayloadBytes is < 1 or > 1024 * 1024)
            throw new ArgumentOutOfRangeException(
                nameof(maximumPayloadBytes));

        ProviderExtensionData? match = null;
        foreach (ProviderExtensionData extension in extensions)
        {
            if (extension.ProviderId != providerId ||
                extension.SchemaId != schemaId)
                continue;
            if (match is not null ||
                extension.ContentType != JsonContentType ||
                extension.Payload.Length == 0 ||
                extension.Payload.Length > maximumPayloadBytes)
            {
                evidence = null;
                return false;
            }
            match = extension;
        }

        if (match is null)
        {
            evidence = null;
            return false;
        }

        try
        {
            evidence = JsonSerializer.Deserialize(
                match.Value.Payload.Span,
                typeInfo);
            return evidence is not null;
        }
        catch (JsonException)
        {
            evidence = null;
            return false;
        }
    }
}

internal static class SensitiveValueRedactor
{
    public const string Redacted = "[REDACTED]";

    public static string Redact(
        string? value,
        int maximumVisibleCharacters = 256)
    {
        if (maximumVisibleCharacters is < 0 or > 4096)
            throw new ArgumentOutOfRangeException(
                nameof(maximumVisibleCharacters));
        if (string.IsNullOrEmpty(value))
            return string.Empty;
        return value.Length <= maximumVisibleCharacters
            ? Redacted
            : $"{Redacted}:{value.Length}";
    }
}

internal readonly record struct AuthorityRevocationEvaluation(
    bool Verified,
    ResourcePhase ResourcePhase,
    AuthorityBindingPhase BindingPhase);

internal static class AuthorityRevocationVerifier
{
    public static AuthorityRevocationEvaluation Evaluate(
        AuthorityBindingPhase reportedPhase,
        RevocationVerificationStatus evidence)
    {
        bool verified =
            reportedPhase == AuthorityBindingPhase.Revoked &&
            evidence == RevocationVerificationStatus.Verified;
        return new AuthorityRevocationEvaluation(
            verified,
            verified
                ? ResourcePhase.Deleted
                : ResourcePhase.Deleting,
            verified
                ? AuthorityBindingPhase.Revoked
                : AuthorityBindingPhase.Revoking);
    }
}
