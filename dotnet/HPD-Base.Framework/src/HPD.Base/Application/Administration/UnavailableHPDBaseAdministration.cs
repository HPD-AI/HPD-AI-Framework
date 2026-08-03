using Microsoft.Extensions.DependencyInjection;

namespace HPD.Base;

internal sealed class UnavailableHPDBaseAdministration(IServiceProvider services) : IHPDBaseAdministration
{
    public BaseAdministrationCapability Capability { get; } = new()
    {
        Backup = false,
        Validate = false,
        Restore = false,
        AdministrativePurge = true,
        OnlineBackup = false,
        WritersBlockedDuringBackup = false,
        ReadersBlockedDuringBackup = false,
        RestoreRequiresExclusiveMaintenance = false,
        Durable = false,
        MaxArtifactBytes = 0,
    };

    public ValueTask<BaseResult<BaseBackupManifest>> CreateBackupAsync(Stream destination, BaseBackupRequest request, CancellationToken cancellationToken = default) =>
        Unsupported<BaseBackupManifest>(cancellationToken);

    public ValueTask<BaseResult<BaseBackupManifest>> ValidateBackupAsync(Stream source, BaseBackupValidationRequest request, CancellationToken cancellationToken = default) =>
        Unsupported<BaseBackupManifest>(cancellationToken);

    public ValueTask<BaseResult<BaseRestoreResult>> RestoreAsync(Stream source, BaseRestoreRequest request, CancellationToken cancellationToken = default) =>
        Unsupported<BaseRestoreResult>(cancellationToken);

    public async ValueTask<BaseResult<BasePurgeResult>> PurgeAsync(BasePurgeRequest request, CancellationToken cancellationToken = default) =>
        BaseResultMapper.Map<BasePurgeResult, BasePurgeResult>(
            await services.GetRequiredService<IBaseMutationCoordinator>().ExecutePurgeAsync(request, cancellationToken).ConfigureAwait(false),
            static value => value);

    private static ValueTask<BaseResult<T>> Unsupported<T>(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult<BaseResult<T>>(new BaseFailure<T>(
            OperationStatus.CapabilityUnavailable,
            new BaseError
            {
                Code = BaseAdministrationErrorCodes.CapabilityUnavailable,
                Message = "The selected BASE provider does not support administration.",
                Category = ErrorCategory.Capability,
            },
            warnings: null,
            diagnostics: null));
    }
}
