using HPD.Base;

namespace HPD.Auth.Base;

internal sealed class AuthUserCleanupSemanticDefinitionV1;
internal sealed class AuthRoleCleanupSemanticDefinitionV1;

internal static class AuthCleanupSemanticActivations
{
    internal static BaseSemanticActivationRegistration<AuthUserCleanupInitializeV1,
        AuthUserCleanupSemanticDefinitionV1> User { get; } = CreateUser();

    internal static BaseSemanticActivationRegistration<AuthRoleCleanupInitializeV1,
        AuthRoleCleanupSemanticDefinitionV1> Role { get; } = CreateRole();

    private static BaseSemanticActivationRegistration<AuthUserCleanupInitializeV1,
        AuthUserCleanupSemanticDefinitionV1> CreateUser()
    {
        BaseSemanticActivationKeyExpression expression = BaseSemanticActivationKeyBuilder.Tuple(
            BaseSemanticActivationKeyBuilder.String("hpd.auth.semantic.cleanup.user.key.v1", 64),
            BaseSemanticActivationKeyBuilder.Property(
                AuthUserCleanupInitializeOperationV1.RequestProperties.CleanupWorkId, 64),
            BaseSemanticActivationKeyBuilder.Property(
                AuthUserCleanupInitializeOperationV1.RequestProperties.Incarnation, 64),
            BaseSemanticActivationKeyBuilder.Property(
                AuthUserCleanupInitializeOperationV1.RequestProperties.TombstoneSequence, 8));
        return BaseSemanticActivationDeclarationBuilder.Create<AuthUserCleanupInitializeV1,
            AuthCleanupInitializeResultV1, AuthCleanupRetirementResultV1,
            AuthUserCleanupSemanticDefinitionV1>(
            Draft(
                "hpd.auth.semantic.cleanup.user.v1",
                AuthUserCleanupInitializeOperationV1.Definition,
                AuthUserCleanupRetireOperationV1.Definition,
                AuthCleanupActivationDeclarations.User.Definition,
                "auth.semantic.cleanup.user.ensure",
                "auth.semantic.cleanup.user.retire",
                "auth.semantic.cleanup.user.maintain",
                "hpd.auth.type.auth-user-cleanup-initialize-v1.v1",
                BaseGeneratedSemanticActivationCompactions.SubjectRetirement(
                    AuthUserSubject.HPDBaseSubjectRegistration,
                    AuthUserCleanupInitializeOperationV1.RequestProperties.Subject,
                    "base.subjectLifecycle.finalizeRetirement")),
            AuthUserCleanupInitializeOperationV1.Identity,
            AuthUserCleanupRetireOperationV1.Identity,
            new AuthBaseJsonSerializerContext(BaseSerializerGeneratedContract.CreateOptions(
                System.Text.Json.JsonNamingPolicy.CamelCase,
                System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)).AuthUserCleanupInitializeV1,
            expression);
    }

    private static BaseSemanticActivationRegistration<AuthRoleCleanupInitializeV1,
        AuthRoleCleanupSemanticDefinitionV1> CreateRole()
    {
        BaseSemanticActivationKeyExpression expression = BaseSemanticActivationKeyBuilder.Tuple(
            BaseSemanticActivationKeyBuilder.String("hpd.auth.semantic.cleanup.role.key.v1", 64),
            BaseSemanticActivationKeyBuilder.Property(
                AuthRoleCleanupInitializeOperationV1.RequestProperties.CleanupWorkId, 64),
            BaseSemanticActivationKeyBuilder.Property(
                AuthRoleCleanupInitializeOperationV1.RequestProperties.Incarnation, 64),
            BaseSemanticActivationKeyBuilder.Property(
                AuthRoleCleanupInitializeOperationV1.RequestProperties.TombstoneSequence, 8));
        return BaseSemanticActivationDeclarationBuilder.Create<AuthRoleCleanupInitializeV1,
            AuthCleanupInitializeResultV1, AuthCleanupRetirementResultV1,
            AuthRoleCleanupSemanticDefinitionV1>(
            Draft(
                "hpd.auth.semantic.cleanup.role.v1",
                AuthRoleCleanupInitializeOperationV1.Definition,
                AuthRoleCleanupRetireOperationV1.Definition,
                AuthCleanupActivationDeclarations.Role.Definition,
                "auth.semantic.cleanup.role.ensure",
                "auth.semantic.cleanup.role.retire",
                "auth.semantic.cleanup.role.maintain",
                "hpd.auth.type.auth-role-cleanup-initialize-v1.v1",
                BaseGeneratedSemanticActivationCompactions.SubjectRetirement(
                    AuthRoleSubject.HPDBaseSubjectRegistration,
                    AuthRoleCleanupInitializeOperationV1.RequestProperties.Subject,
                    "base.subjectLifecycle.finalizeRetirement")),
            AuthRoleCleanupInitializeOperationV1.Identity,
            AuthRoleCleanupRetireOperationV1.Identity,
            new AuthBaseJsonSerializerContext(BaseSerializerGeneratedContract.CreateOptions(
                System.Text.Json.JsonNamingPolicy.CamelCase,
                System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)).AuthRoleCleanupInitializeV1,
            expression);
    }

    private static BaseSemanticActivationKeyDefinition Draft(
        string id,
        BaseRegisteredModuleMutationDefinition ensure,
        BaseRegisteredModuleMutationDefinition retirement,
        BaseActivationDefinition activation,
        string ensureGrant,
        string retirementGrant,
        string maintenanceGrant,
        string requestTypeId,
        BaseSemanticActivationCompactionContract compaction) => new()
    {
        Id = id,
        Version = 1,
        OwningApplicationId = AuthBaseContract.ApplicationId,
        OwningModuleId = AuthBaseContract.ModuleId,
        EnsureOperation = new BaseSemanticActivationModuleOperationIdentity
        {
            OperationId = ensure.Id,
            OperationVersion = ensure.Version,
            OperationChecksum = Convert.ToHexStringLower(ensure.Checksum.ToArray()),
        },
        RetirementOperation = new BaseSemanticActivationModuleOperationIdentity
        {
            OperationId = retirement.Id,
            OperationVersion = retirement.Version,
            OperationChecksum = Convert.ToHexStringLower(retirement.Checksum.ToArray()),
        },
        Activation = new BaseActivationDefinitionKey
        {
            Id = activation.Id,
            Version = activation.Version,
            Checksum = activation.Checksum,
        },
        ScopeKind = BaseSubjectScopeKind.Tenant,
        EnsureGrantId = ensureGrant,
        RetirementGrantId = retirementGrant,
        MaintenanceGrantId = maintenanceGrant,
        Compaction = compaction,
        RequestTypeId = requestTypeId,
        RequestSerializerChecksum = [],
        KeyExpressionChecksum = [],
        Limits = new BaseSemanticActivationLimits
        {
            MaximumCanonicalKeyBytes = 256,
            MaximumLiveSlots = 1_000_000,
            MaximumRetiredSlots = 1_000_000,
            MaximumAbsenceMarkers = 1_000_000,
            Execution = new BaseSemanticActivationExecutionLimits
            {
                MaximumOperations = 1,
                MaximumScopeDirectoryReads = 1,
                MaximumSlotReads = 1,
                MaximumActivationReads = 1,
                MaximumReadIntervals = 64,
                MaximumIndexOperations = 256,
                MaximumActivationBytes = 1_048_576,
                MaximumScopeDirectoryBytes = 65_536,
                MaximumEvidenceBytes = 1_048_576,
                MaximumReceiptBytes = 1_048_576,
                MaximumTransientBytes = 8_388_608,
            },
            Deadlines = new BaseSemanticActivationDeadlineCapability
            {
                AcquisitionTimeout = TimeSpan.FromSeconds(5),
                TransactionTimeout = TimeSpan.FromSeconds(30),
                CommitObservationTimeout = TimeSpan.FromSeconds(30),
                ReceiptResolutionTimeout = TimeSpan.FromSeconds(30),
                MaintenanceTimeout = TimeSpan.FromSeconds(300),
                QuarantineRetentionTimeout = TimeSpan.FromSeconds(300),
            },
        },
        Checksum = [],
    };
}
