namespace HPD.Execution.AppleVirtualization.Authority;

using System.Text.Json;
using HPD.Execution.Contracts;

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

        for (int i = 0; i < extensions.Count; i++)
        {
            ProviderExtensionData extension = extensions[i];
            if (extension.ProviderId != AppleVirtualizationProviderDescriptor.ProviderId ||
                extension.SchemaId != AppleVirtualizationAuthorityBindingProvider.AuthorityEvidenceExtensionSchema)
            {
                continue;
            }

            evidence = JsonSerializer.Deserialize(
                extension.Payload.Span,
                AppleVirtualizationJsonContext.Default.AppleVirtualizationAuthorityEvidenceExtension) ?? new AppleVirtualizationAuthorityEvidenceExtension
                {
                    BindingId = string.Empty,
                };
            return !string.IsNullOrWhiteSpace(evidence.BindingId);
        }

        evidence = new AppleVirtualizationAuthorityEvidenceExtension
        {
            BindingId = string.Empty,
        };
        return false;
    }
}
