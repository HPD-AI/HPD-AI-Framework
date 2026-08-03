namespace HPD.Base;

internal sealed class UnavailableHPDBaseAdministration : IHPDBaseAdministration
{
    public BaseAdministrationCapability Capability { get; } = new()
    {
        Backup = false,
        Validate = false,
        Restore = false,
        AdministrativePurge = false,
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

    public ValueTask<BaseResult<BasePurgeResult>> PurgeAsync(BasePurgeRequest request, CancellationToken cancellationToken = default) =>
        Unsupported<BasePurgeResult>(cancellationToken);

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
