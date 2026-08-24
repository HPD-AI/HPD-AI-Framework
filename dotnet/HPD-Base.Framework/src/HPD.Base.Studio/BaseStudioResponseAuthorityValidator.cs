using HPD.AI.Platform.Studio;

namespace HPD.Base.Studio;

internal sealed class BaseStudioResponseAuthorityValidator(HPDBaseStudioAuthoritySnapshot authority,
    IBaseStudioDynamicStoreAuthoritySource storeAuthority, BaseStudioLateWorkRegistry lateWork)
    : IBaseStudioResponseAuthorityValidator
{
    public async ValueTask<bool> IsCurrentAsync(BaseStudioResponseAuthority response, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (response.Stores.Length != 1 || response.PolicyOwnerGeneration != authority.PolicyOwnerGeneration ||
            !BaseStudioSha256.FixedTimeEquals(response.PolicyOwnerChecksum, BaseStudioSha256.FromDigest(authority.GetPolicyOwnerChecksum())) ||
            !BaseStudioSha256.FixedTimeEquals(response.StudioOwnerChecksum, BaseStudioSha256.FromDigest(authority.GetChecksum()))) return false;
        BaseStudioDynamicStoreAuthorityRequest request = BaseStudioBootstrapRuntime.StoreAuthorityRequest(authority.ApplicationId);
        OperationResult<BaseStudioDynamicStoreAuthority>? captured = await BaseStudioBootstrapRuntime.CaptureStoreAsync(
            storeAuthority, lateWork, request, cancellationToken).ConfigureAwait(false);
        if (captured is null || !captured.IsSuccess() || captured.Value is null || !BaseStudioDynamicStoreAuthorityContract.IsValidResult(request, captured.Value)) return false;
        BaseStudioStoreAuthority expected = BaseStudioStoreAuthority.Create(captured.Value.StoreInstanceId, authority.ProviderGeneration,
            captured.Value.RestoreEpoch, captured.Value.SchemaGeneration, BaseStudioSha256.FromDigest(authority.GetProviderCapabilityChecksum()));
        BaseStudioStoreAuthority actual = response.Stores[0];
        bool current = response.PolicyOwnerGeneration == authority.PolicyOwnerGeneration &&
            BaseStudioSha256.FixedTimeEquals(response.PolicyOwnerChecksum, BaseStudioSha256.FromDigest(authority.GetPolicyOwnerChecksum())) &&
            BaseStudioSha256.FixedTimeEquals(response.StudioOwnerChecksum, BaseStudioSha256.FromDigest(authority.GetChecksum())) &&
            StringComparer.Ordinal.Equals(actual.StoreIdentity, expected.StoreIdentity) && actual.ProviderGeneration == expected.ProviderGeneration &&
            actual.RestoreEpoch == expected.RestoreEpoch && actual.SchemaGeneration == expected.SchemaGeneration &&
            BaseStudioSha256.FixedTimeEquals(actual.CapabilityChecksum, expected.CapabilityChecksum) && BaseStudioSha256.FixedTimeEquals(actual.Checksum, expected.Checksum);
        return current;
    }
}
