using HPD.Base;

namespace HPD.Auth.Base;

/// <summary>Contains immutable host authority required to finalize the HPD Auth Base graph.</summary>
public sealed record AuthBaseModuleOptions
{
    /// <summary>Gets the exact 32-byte digest of the installed Data Protection application discriminator.</summary>
    public required BaseBinary DataProtectionApplicationDiscriminatorDigest { get; init; }

    /// <summary>Gets the host-selected exact storage-protection requirement owned by HPD Auth.</summary>
    public required BaseStorageProtectionRequirement StorageProtectionRequirement { get; init; }
}

/// <summary>Installs the currently declared private HPD Auth authority graph into HPD Base.</summary>
public static class AuthBaseModule
{
    /// <summary>Installs the complete HPD Auth identity module into an HPD Base graph.</summary>
    /// <param name="builder">The application Base builder.</param>
    /// <param name="options">The immutable Auth graph-finalization authority.</param>
    /// <returns>The same builder.</returns>
    public static HPDBaseBuilder AddHPDAuthIdentityModule(
        this HPDBaseBuilder builder,
        AuthBaseModuleOptions options) => Install(builder, options);

    /// <summary>Installs HPD Auth collections and exported subject contracts.</summary>
    /// <param name="builder">The application Base builder.</param>
    /// <param name="options">The immutable Auth graph-finalization authority.</param>
    /// <returns>The same builder.</returns>
    public static HPDBaseBuilder Install(HPDBaseBuilder builder, AuthBaseModuleOptions options)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(options.DataProtectionApplicationDiscriminatorDigest);
        ArgumentNullException.ThrowIfNull(options.StorageProtectionRequirement);
        if (options.DataProtectionApplicationDiscriminatorDigest.Length != 32)
            throw new ArgumentException(
                "The Data Protection application discriminator digest must contain exactly 32 bytes.",
                nameof(options));
        if (!string.Equals(options.StorageProtectionRequirement.OwningModuleId,
                AuthBaseContract.ModuleId, StringComparison.Ordinal))
            throw new ArgumentException(
                "The storage-protection requirement must be owned by HPD Auth.",
                nameof(options));

        BaseStorageProtectionRequirement storageProtection = Own(options.StorageProtectionRequirement);

        AuthPolicyAuthorityInstaller.Install(builder);
        builder.RequireStorageProtection(storageProtection);
        builder.ConfigureRelational(options => options.MaxSources = Math.Max(options.MaxSources, 12));

        builder
            .AddCollection(AuthUserRecordV1.Collection)
            .AddCollection(AuthRoleRecordV1.Collection)
            .AddCollection(AuthUserClaimRecordV1.Collection)
            .AddCollection(AuthRoleClaimRecordV1.Collection)
            .AddCollection(AuthUserRoleRecordV1.Collection)
            .AddCollection(AuthUserLoginRecordV1.Collection)
            .AddCollection(AuthUserTokenRecordV1.Collection)
            .AddCollection(AuthRecoveryCodeRecordV1.Collection)
            .AddCollection(AuthPasskeyRecordV1.Collection)
            .AddCollection(AuthRefreshTokenRecordV1.Collection)
            .AddCollection(AuthRefreshTokenDeliveryRecordV1.Collection)
            .AddCollection(AuthSessionRecordV1.Collection)
            .AddCollection(AuthSsoProviderRecordV1.Collection)
            .AddCollection(AuthUserIdentityRecordV1.Collection)
            .AddCollection(AuthTenantSettingsRecordV1.Collection)
            .AddCollection(AuthSecurityAuditRecordV1.Collection)
            .AddCollection(AuthDataProtectionKeyRecordV1.Collection)
            .AddCollection(AuthCleanupWorkRecordV1.Collection)
            .AddCollection(AuthMaintenanceCursorRecordV1.Collection)
            .AddCollection(AuthMaintenanceRunRecordV1.Collection)
            .AddModuleGenerationCell(GenerationCell("hpd.auth.membership-generation.v1"))
            .AddModuleGenerationCell(GenerationCell("hpd.auth.role-state-generation.v1"))
            .AddModuleGenerationCell(GenerationCell("hpd.auth.tenant-policy-generation.v1"))
            .AddModuleGenerationCell(GenerationCell("hpd.auth.user-security-generation.v1"))
            .AddModuleGenerationCell(GenerationCell("hpd.auth.user-state-generation.v1"))
            .AddExportedSubject(AuthUserSubject.HPDBaseSubjectRegistration)
            .AddExportedSubject(AuthRoleSubject.HPDBaseSubjectRegistration)
            .AddSubjectAcquisition(new BaseSubjectAcquisitionDefinition
            {
                Id = "hpd.auth.user-subject.acquire.v1",
                Version = 1,
                ContractId = "hpd.auth.user-subject",
                ContractVersion = 1,
                RegisteredReadId = "auth.read.userSubject.acquire.v1",
                RequiredGrantId = "auth.subject.user.acquire",
                Audience = HPDBaseEndpointAudience.Application,
                MaximumResults = 1,
            })
            .AddSubjectAcquisition(new BaseSubjectAcquisitionDefinition
            {
                Id = "hpd.auth.role-subject.acquire.v1",
                Version = 1,
                ContractId = "hpd.auth.role-subject",
                ContractVersion = 1,
                RegisteredReadId = "auth.read.roleSubject.acquire.v1",
                RequiredGrantId = "auth.subject.role.acquire",
                Audience = HPDBaseEndpointAudience.Application,
                MaximumResults = 1,
            })
            .AddSubjectAcquisition(new BaseSubjectAcquisitionDefinition
            {
                Id = "hpd.auth.user-subject.reconciliation.v1",
                Version = 1,
                ContractId = "hpd.auth.user-subject",
                ContractVersion = 1,
                RegisteredReadId = "auth.read.tombstonedUserSubjectForReconciliation.v1",
                RequiredGrantId = "auth.subject.user.acquire",
                Audience = HPDBaseEndpointAudience.Application,
                MaximumResults = 1,
            })
            .AddSubjectAcquisition(new BaseSubjectAcquisitionDefinition
            {
                Id = "hpd.auth.role-subject.reconciliation.v1",
                Version = 1,
                ContractId = "hpd.auth.role-subject",
                ContractVersion = 1,
                RegisteredReadId = "auth.read.tombstonedRoleSubjectForReconciliation.v1",
                RequiredGrantId = "auth.subject.role.acquire",
                Audience = HPDBaseEndpointAudience.Application,
                MaximumResults = 1,
            })
            .AddSubjectAcquisition(new BaseSubjectAcquisitionDefinition
            {
                Id = "hpd.auth.user-subject.reconciliation-page.v1",
                Version = 1,
                ContractId = "hpd.auth.user-subject",
                ContractVersion = 1,
                RegisteredReadId = "auth.read.tombstonedUserReferencesForReconciliation.v1",
                RequiredGrantId = "auth.subject.user.acquire",
                Audience = HPDBaseEndpointAudience.Application,
                MaximumResults = 200,
            })
            .AddSubjectAcquisition(new BaseSubjectAcquisitionDefinition
            {
                Id = "hpd.auth.role-subject.reconciliation-page.v1",
                Version = 1,
                ContractId = "hpd.auth.role-subject",
                ContractVersion = 1,
                RegisteredReadId = "auth.read.tombstonedRoleReferencesForReconciliation.v1",
                RequiredGrantId = "auth.subject.role.acquire",
                Audience = HPDBaseEndpointAudience.Application,
                MaximumResults = 200,
            })
            .AddRead(AuthUserSubjectAcquisitionReadV1.Definition)
            .AddRead(AuthRoleSubjectAcquisitionReadV1.Definition)
            .AddRead(AuthTombstonedUserSubjectForReconciliationReadV1.Definition)
            .AddRead(AuthTombstonedRoleSubjectForReconciliationReadV1.Definition)
            .AddRead(AuthTombstonedUserReferencesForReconciliationReadV1.Definition)
            .AddRead(AuthTombstonedRoleReferencesForReconciliationReadV1.Definition)
            .AddRead(AuthUserByIdReadV1.Definition)
            .AddRead(AuthUserByNormalizedNameReadV1.Definition)
            .AddRead(AuthUserByNormalizedEmailReadV1.Definition)
            .AddRead(AuthUsersInRoleReadV1.Definition)
            .AddRead(AuthUserPasswordReadV1.Definition)
            .AddRead(AuthUserTwoFactorSecretsReadV1.Definition)
            .AddRead(AuthDataProtectionKeysReadV1.Definition)
            .AddRead(AuthPasskeyByDigestReadV1.Definition)
            .AddRead(AuthUserPasskeysReadV1.Definition)
            .AddRead(AuthUserTokenSecretReadV1.Definition)
            .AddRead(AuthUserClaimsReadV1.Definition)
            .AddRead(AuthUsersForClaimReadV1.Definition)
            .AddRead(AuthRoleClaimsReadV1.Definition)
            .AddRead(AuthTenantSettingsReadV1.Definition)
            .AddRead(AuthRoleByIdReadV1.Definition)
            .AddRead(AuthRoleByNormalizedNameReadV1.Definition)
            .AddRead(AuthUserRolesReadV1.Definition)
            .AddRead(AuthUserLoginsReadV1.Definition)
            .AddRead(AuthActiveSessionsReadV1.Definition)
            .AddRead(AuthActiveSessionsAdminReadV1.Definition)
            .AddRead(AuthAuditReadV1.Definition)
            .AddRead(AuthExternalIdentityReadV1.Definition)
            .AddRead(AuthSsoProviderSecretReadV1.Definition)
            .AddRead(AuthRecoveryCodeByDigestReadV1.Definition)
            .AddRead(AuthRecoveryCodesForUserReadV1.Definition)
            .AddRead(AuthRefreshByDigestReadV1.Definition)
            .AddRead(AuthRefreshDigestKeyVersionsReadV1.Definition)
            .AddRead(AuthRefreshDeliveryReadV1.Definition)
            .AddRead(AuthCleanupDependentsReadV1.Definition)
            .AddRead(AuthCleanupWorkReadV1.Definition)
            .AddRead(AuthMaintenanceRunReadV1.Definition)
            .AddRead(AuthTombstonedUsersForReconciliationReadV1.Definition)
            .AddRead(AuthTombstonedRolesForReconciliationReadV1.Definition)
            .AddRead(AuthAdminUsersCreatedAtAscReadV1.Definition)
            .AddRead(AuthAdminUsersCreatedAtDescReadV1.Definition)
            .AddRead(AuthAdminUsersEmailAscReadV1.Definition)
            .AddRead(AuthAdminUsersEmailDescReadV1.Definition)
            .AddRead(AuthAdminUsersLastLoginAtAscReadV1.Definition)
            .AddRead(AuthAdminUsersLastLoginAtDescReadV1.Definition);

        foreach (BaseSelectionOperationProfile profile in AuthSelectionProfiles.All)
            builder.AddSelectionOperationProfile(profile);

        builder.AddModuleMutation(
            AuthCreateUserOperationV1.Definition,
            AuthCreateUserOperationV1.Identity);
        builder.AddModuleMutation(
            AuthUpdateUserProfileOperationV1.Definition,
            AuthUpdateUserProfileOperationV1.Identity);
        builder.AddModuleMutation(
            AuthCreateRoleOperationV1.Definition,
            AuthCreateRoleOperationV1.Identity);
        builder.AddModuleMutation(
            AuthRenameRoleOperationV1.Definition,
            AuthRenameRoleOperationV1.Identity);
        builder.AddModuleMutation(
            AuthMembershipAddOperationV1.Definition,
            AuthMembershipAddOperationV1.Identity);
        builder.AddModuleMutation(
            AuthMembershipRemoveOperationV1.Definition,
            AuthMembershipRemoveOperationV1.Identity);
        builder.AddModuleMutation(
            AuthLoginLinkOperationV1.Definition,
            AuthLoginLinkOperationV1.Identity);
        builder.AddModuleMutation(
            AuthLoginUnlinkOperationV1.Definition,
            AuthLoginUnlinkOperationV1.Identity);
        builder.AddModuleMutation(
            AuthChangePasswordOperationV1.Definition,
            AuthChangePasswordOperationV1.Identity);
        builder.AddModuleMutation(
            AuthRemovePasswordOperationV1.Definition,
            AuthRemovePasswordOperationV1.Identity);
        builder.AddModuleMutation(
            AuthResetPasswordOperationV1.Definition,
            AuthResetPasswordOperationV1.Identity);
        builder.AddModuleMutation(
            AuthSetSecurityStateOperationV1.Definition,
            AuthSetSecurityStateOperationV1.Identity);
        builder.AddModuleMutation(
            AuthAuditAppendOperationV1.Definition,
            AuthAuditAppendOperationV1.Identity);
        builder.AddModuleMutation(
            AuthPasskeyRecordAssertionOperationV1.Definition,
            AuthPasskeyRecordAssertionOperationV1.Identity);
        builder.AddModuleMutation(
            AuthSessionCreateOperationV1.Definition,
            AuthSessionCreateOperationV1.Identity);
        builder.AddModuleMutation(
            AuthSessionTouchOperationV1.Definition,
            AuthSessionTouchOperationV1.Identity);
        builder.AddModuleMutation(
            AuthRefreshIssueOperationV1.Definition,
            AuthRefreshIssueOperationV1.Identity);
        builder.AddModuleMutation(
            AuthRefreshRotateOperationV1.Definition,
            AuthRefreshRotateOperationV1.Identity);
        builder.AddModuleMutation(
            AuthRecoveryCodeConsumeOperationV1.Definition,
            AuthRecoveryCodeConsumeOperationV1.Identity);
        builder.AddModuleMutation(
            AuthRecoveryCodesReplaceOperationV1.Definition,
            AuthRecoveryCodesReplaceOperationV1.Identity);
        builder.AddModuleMutation(
            AuthPasskeyRegisterOperationV1.Definition,
            AuthPasskeyRegisterOperationV1.Identity);
        builder.AddModuleMutation(
            AuthPasskeyRemoveOperationV1.Definition,
            AuthPasskeyRemoveOperationV1.Identity);
        builder.AddModuleMutation(
            AuthMaintenanceRunInitializeOperationV1.Definition,
            AuthMaintenanceRunInitializeOperationV1.Identity);
        builder.AddModuleMutation(
            AuthCleanupReconcileCursorOperationV1.Definition,
            AuthCleanupReconcileCursorOperationV1.Identity);
        builder.AddModuleMutation(
            AuthCleanupAdvanceOperationV1.Definition,
            AuthCleanupAdvanceOperationV1.Identity);
        builder.AddModuleMutation(
            AuthCleanupPrepareRetirementOperationV1.Definition,
            AuthCleanupPrepareRetirementOperationV1.Identity);
        builder.AddModuleMutation(
            AuthUserCleanupInitializeOperationV1.Definition,
            AuthUserCleanupInitializeOperationV1.Identity);
        builder.AddModuleMutation(
            AuthRoleCleanupInitializeOperationV1.Definition,
            AuthRoleCleanupInitializeOperationV1.Identity);
        builder.AddModuleMutation(
            AuthUserCleanupRetireOperationV1.Definition,
            AuthUserCleanupRetireOperationV1.Identity);
        builder.AddModuleMutation(
            AuthRoleCleanupRetireOperationV1.Definition,
            AuthRoleCleanupRetireOperationV1.Identity);
        builder
            .AddActivation(AuthCleanupActivationDeclarations.User)
            .AddActivation(AuthCleanupActivationDeclarations.Role)
            .AddActivation(AuthLifecycleActivationDeclarations.BootstrapUser)
            .AddActivation(AuthLifecycleActivationDeclarations.BootstrapRole)
            .AddActivation(AuthLifecycleActivationDeclarations.RetireUser)
            .AddActivation(AuthLifecycleActivationDeclarations.RetireRole)
            .AddActivation(AuthLifecycleActivationDeclarations.Reconcile)
            .AddActivation(AuthLifecycleActivationDeclarations.Sessions)
            .AddActivation(AuthLifecycleActivationDeclarations.RefreshTokens)
            .AddActivation(AuthLifecycleActivationDeclarations.Deliveries)
            .AddActivation(AuthLifecycleActivationDeclarations.DataProtection);

        builder
            .AddSemanticActivation(AuthCleanupSemanticActivations.User)
            .AddSemanticActivation(AuthCleanupSemanticActivations.Role);

        foreach (BaseGeneratedScheduleRegistration schedule in AuthScheduleDeclarations.Create(
            options.DataProtectionApplicationDiscriminatorDigest))
            builder.AddSchedule(schedule);
        return builder;
    }

    private static BaseModuleGenerationCellDefinition GenerationCell(string id) => new()
    {
        Id = id,
        Version = 1,
        OwningModuleId = AuthBaseContract.ModuleId,
        Scope = BaseModuleGenerationScope.TenantAndKey,
        MaximumKeyUtf8Bytes = 36,
        MaximumCellsPerOperation = 1,
    };

    private static BaseStorageProtectionRequirement Own(BaseStorageProtectionRequirement value) => value with
    {
        OwningModuleId = new string(value.OwningModuleId.AsSpan()),
        PermittedGuarantees = [.. value.PermittedGuarantees],
        PermittedKeyOwners = [.. value.PermittedKeyOwners],
        Coverage = value.Coverage with
        {
            AuthoritativeRecords = [.. value.Coverage.AuthoritativeRecords],
            Journal = [.. value.Coverage.Journal],
            Receipts = [.. value.Coverage.Receipts],
            ProviderState = [.. value.Coverage.ProviderState],
            Indexes = [.. value.Coverage.Indexes],
            TemporaryFiles = [.. value.Coverage.TemporaryFiles],
            AuthoritativeBackups = [.. value.Coverage.AuthoritativeBackups],
            AdministrativeExports = [.. value.Coverage.AdministrativeExports],
            OrdinaryExports = [.. value.Coverage.OrdinaryExports],
            ExternalFilesAndBlobs = [.. value.Coverage.ExternalFilesAndBlobs],
        },
    };
}
