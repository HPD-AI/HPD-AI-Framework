using HPD.AI.Platform.Studio;

namespace HPD.Base.Studio;

internal sealed class HPDBaseStudioResponseAuthorityValidator(HPDBaseStudioAuthoritySnapshot authority,
    IStudioDynamicStoreAuthoritySource storeAuthority, StudioLateWorkRegistry lateWork)
    : IStudioResponseAuthorityValidator
{
    public async ValueTask<bool> IsCurrentAsync(StudioResponseAuthority response, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (response.Stores.Length != 1 || response.PolicyOwnerGeneration != authority.PolicyOwnerGeneration ||
            !StudioSha256.FixedTimeEquals(response.PolicyOwnerChecksum, StudioSha256.FromDigest(authority.GetPolicyOwnerChecksum())) ||
            !StudioSha256.FixedTimeEquals(response.StudioOwnerChecksum, StudioSha256.FromDigest(authority.GetChecksum()))) return false;
        StudioDynamicStoreAuthorityRequest request = HPDBaseStudioBootstrapRuntime.StoreAuthorityRequest(authority.ApplicationId);
        OperationResult<StudioDynamicStoreAuthority>? captured = await HPDBaseStudioBootstrapRuntime.CaptureStoreAsync(
            storeAuthority, lateWork, request, cancellationToken).ConfigureAwait(false);
        if (captured is null || !captured.IsSuccess() || captured.Value is null || !StudioDynamicStoreAuthorityContract.IsValidResult(request, captured.Value)) return false;
        StudioStoreAuthority expected = StudioStoreAuthority.Create(captured.Value.StoreInstanceId, authority.ProviderGeneration,
            captured.Value.RestoreEpoch, captured.Value.SchemaGeneration, StudioSha256.FromDigest(authority.GetProviderCapabilityChecksum()));
        StudioStoreAuthority actual = response.Stores[0];
        bool current = response.PolicyOwnerGeneration == authority.PolicyOwnerGeneration &&
            StudioSha256.FixedTimeEquals(response.PolicyOwnerChecksum, StudioSha256.FromDigest(authority.GetPolicyOwnerChecksum())) &&
            StudioSha256.FixedTimeEquals(response.StudioOwnerChecksum, StudioSha256.FromDigest(authority.GetChecksum())) &&
            StringComparer.Ordinal.Equals(actual.StoreIdentity, expected.StoreIdentity) && actual.ProviderGeneration == expected.ProviderGeneration &&
            actual.RestoreEpoch == expected.RestoreEpoch && actual.SchemaGeneration == expected.SchemaGeneration &&
            StudioSha256.FixedTimeEquals(actual.CapabilityChecksum, expected.CapabilityChecksum) && StudioSha256.FixedTimeEquals(actual.Checksum, expected.Checksum);
        return current;
    }
}
