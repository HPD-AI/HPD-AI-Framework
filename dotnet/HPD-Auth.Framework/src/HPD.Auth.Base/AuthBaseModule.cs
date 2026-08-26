using HPD.Base;

namespace HPD.Auth.Base;

/// <summary>Installs the currently declared private HPD Auth authority graph into HPD Base.</summary>
public static class AuthBaseModule
{
    /// <summary>Installs HPD Auth collections and exported subject contracts.</summary>
    /// <param name="builder">The application Base builder.</param>
    /// <returns>The same builder.</returns>
    public static HPDBaseBuilder Install(HPDBaseBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        AuthPolicyAuthorityInstaller.Install(builder);
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
            .AddCollection(AuthImportStateRecordV1.Collection)
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
            .AddRead(AuthUserByNormalizedNameReadV1.Definition)
            .AddRead(AuthUserByNormalizedEmailReadV1.Definition)
            .AddRead(AuthUsersInRoleReadV1.Definition)
            .AddRead(AuthUserPasswordReadV1.Definition)
            .AddRead(AuthUserTwoFactorSecretsReadV1.Definition)
            .AddRead(AuthDataProtectionKeysReadV1.Definition)
            .AddRead(AuthPasskeyByDigestReadV1.Definition)
            .AddRead(AuthUserTokenSecretReadV1.Definition)
            .AddRead(AuthUserClaimsReadV1.Definition)
            .AddRead(AuthRoleClaimsReadV1.Definition)
            .AddRead(AuthTenantSettingsReadV1.Definition)
            .AddRead(AuthRoleByNormalizedNameReadV1.Definition)
            .AddRead(AuthUserRolesReadV1.Definition)
            .AddRead(AuthUserLoginsReadV1.Definition)
            .AddRead(AuthActiveSessionsReadV1.Definition)
            .AddRead(AuthActiveSessionsAdminReadV1.Definition)
            .AddRead(AuthAuditReadV1.Definition)
            .AddRead(AuthExternalIdentityReadV1.Definition)
            .AddRead(AuthSsoProviderSecretReadV1.Definition)
            .AddRead(AuthRecoveryCodeByDigestReadV1.Definition)
            .AddRead(AuthRefreshByDigestReadV1.Definition)
            .AddRead(AuthRefreshDigestKeyVersionsReadV1.Definition)
            .AddRead(AuthRefreshDeliveryReadV1.Definition)
            .AddRead(AuthCleanupDependentsReadV1.Definition)
            .AddRead(AuthCleanupWorkReadV1.Definition)
            .AddRead(AuthMaintenanceRunReadV1.Definition)
            .AddRead(AuthTombstonedUsersForReconciliationReadV1.Definition)
            .AddRead(AuthTombstonedRolesForReconciliationReadV1.Definition);

        foreach (BaseSelectionOperationProfile profile in AuthSelectionProfiles.All)
            builder.AddSelectionOperationProfile(profile);

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
}
