namespace HPD.Environment.AppleVirtualization.Authority;

using HPD.Environment.Contracts;
using HPD.Environment.Runtime;

public static class AppleVirtualizationAuthorityEvidenceReader
{
    public static bool TryRead(
        AuthorityBindingStatus status,
        out AppleVirtualizationAuthorityEvidenceExtension evidence)
    {
        ArgumentNullException.ThrowIfNull(status);
        return TryRead(status.Extensions, out evidence);
    }

    public static bool TryRead(
        IReadOnlyList<ProviderExtensionData> extensions,
        out AppleVirtualizationAuthorityEvidenceExtension evidence)
    {
        ArgumentNullException.ThrowIfNull(extensions);

        bool read = AuthorityEvidenceReader.TryRead(
            extensions,
            AppleVirtualizationProviderDescriptor.ProviderId,
            AppleVirtualizationAuthorityBindingProvider
                .AuthorityEvidenceExtensionSchema,
            AppleVirtualizationJsonContext.Default
                .AppleVirtualizationAuthorityEvidenceExtension,
            out AppleVirtualizationAuthorityEvidenceExtension? parsed);
        evidence = parsed ?? new AppleVirtualizationAuthorityEvidenceExtension
        {
            BindingId = string.Empty,
        };
        return read && !string.IsNullOrWhiteSpace(evidence.BindingId);
    }
}
