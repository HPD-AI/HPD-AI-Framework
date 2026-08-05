namespace HPD.Base;

/// <summary>Provider-owned host administration for one exact record-store instance.</summary>
internal interface IRecordStoreAdministration
{
    /// <summary>Gets the provider's exact administration guarantees.</summary>
    BaseAdministrationCapability AdministrationCapability { get; }

    /// <summary>Creates one complete authenticated provider backup.</summary>
    ValueTask<OperationResult<BaseBackupManifest>> CreateBackupAsync(
        Stream destination,
        BaseBackupRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Validates one complete authenticated provider backup.</summary>
    ValueTask<OperationResult<BaseBackupManifest>> ValidateBackupAsync(
        Stream source,
        BaseBackupValidationRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Installs one validated provider backup under exclusive maintenance.</summary>
    ValueTask<OperationResult<BaseRestoreResult>> RestoreAsync(
        Stream source,
        BaseRestoreRequest request,
        CancellationToken cancellationToken = default);
}
