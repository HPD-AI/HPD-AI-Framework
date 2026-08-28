using System.Collections.Immutable;
using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Auth.Base;

internal sealed record AuthCleanupReconcileInputV1
{
    [BaseField("auth.activation.cleanup.reconcile.contractVersion", MinimumInt32 = 1, HasMinimumInt32 = true,
        MaximumInt32 = 1, HasMaximumInt32 = true)]
    public required int ContractVersion { get; init; }
}

internal sealed record AuthCleanupReconcileResultV1
{
    [BaseField("auth.activation.cleanup.reconcile.result.pages", MinimumInt32 = 0, HasMinimumInt32 = true,
        MaximumInt32 = 4, HasMaximumInt32 = true)]
    public required int Pages { get; init; }

    [BaseField("auth.activation.cleanup.reconcile.result.examinedSubjects", MinimumInt32 = 0, HasMinimumInt32 = true,
        MaximumInt32 = 800, HasMaximumInt32 = true)]
    public required int ExaminedSubjects { get; init; }

    [BaseField("auth.activation.cleanup.reconcile.result.committedEnqueues", MinimumInt32 = 0, HasMinimumInt32 = true,
        MaximumInt32 = 800, HasMaximumInt32 = true)]
    public required int CommittedEnqueues { get; init; }

    [BaseField("auth.activation.cleanup.reconcile.result.duplicateEnqueues", MinimumInt32 = 0, HasMinimumInt32 = true,
        MaximumInt32 = 800, HasMaximumInt32 = true)]
    public required int DuplicateEnqueues { get; init; }

    [BaseField("auth.activation.cleanup.reconcile.result.cursorPass", MinimumInt64 = 1, HasMinimumInt64 = true)]
    public required long CursorPass { get; init; }

    [BaseField("auth.activation.cleanup.reconcile.result.cursorTenantId", Presence = BaseFieldPresence.Optional,
        Nullability = BaseFieldNullability.NonNullable)]
    [JsonConverter(typeof(BaseCanonicalNullableGuidJsonConverter))]
    public Guid? CursorTenantId { get; init; }

    [BaseField("auth.activation.cleanup.reconcile.result.cursorSubjectKind", Presence = BaseFieldPresence.Optional,
        Nullability = BaseFieldNullability.NonNullable, AllowedEnumLiterals = ["role", "user"])]
    [JsonConverter(typeof(BaseClosedEnumJsonConverter<AuthCleanupSubjectKindV1>))]
    public AuthCleanupSubjectKindV1? CursorSubjectKind { get; init; }

    [BaseField("auth.activation.cleanup.reconcile.result.cursorSubjectId", Presence = BaseFieldPresence.Optional,
        Nullability = BaseFieldNullability.NonNullable)]
    [JsonConverter(typeof(BaseCanonicalNullableGuidJsonConverter))]
    public Guid? CursorSubjectId { get; init; }

    [BaseField("auth.activation.cleanup.reconcile.result.completedAt")]
    [JsonConverter(typeof(BaseUtcDateTimeJsonConverter))]
    public required DateTimeOffset CompletedAt { get; init; }
}

internal sealed record AuthExpirationTriggerInputV1
{
    [BaseField("auth.activation.expiration.kind",
        AllowedEnumLiterals = ["deliveryExpiration", "refreshExpiration", "sessionExpiration"])]
    [JsonConverter(typeof(BaseClosedEnumJsonConverter<AuthMaintenanceKindV1>))]
    public required AuthMaintenanceKindV1 Kind { get; init; }

    [BaseField("auth.activation.expiration.contractVersion", MinimumInt32 = 1, HasMinimumInt32 = true,
        MaximumInt32 = 1, HasMaximumInt32 = true)]
    public required int ContractVersion { get; init; }
}

internal sealed record AuthExpirationResultV1
{
    [BaseField("auth.activation.expiration.result.selectedCount", MinimumInt32 = 0, HasMinimumInt32 = true,
        MaximumInt32 = 200, HasMaximumInt32 = true)]
    public required int SelectedCount { get; init; }

    [BaseField("auth.activation.expiration.result.mutatedCount", MinimumInt32 = 0, HasMinimumInt32 = true,
        MaximumInt32 = 200, HasMaximumInt32 = true)]
    public required int MutatedCount { get; init; }

    [BaseField("auth.activation.expiration.result.maintenanceRunId", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64)]
    public required string MaintenanceRunId { get; init; }

    [BaseField("auth.activation.expiration.result.cutoff")]
    [JsonConverter(typeof(BaseUtcDateTimeJsonConverter))]
    public required DateTimeOffset Cutoff { get; init; }
}

internal sealed record AuthDataProtectionRefreshInputV1
{
    [BaseField("auth.activation.data-protection.refresh.applicationDiscriminatorDigest", MinimumBytes = 32,
        MaximumBytes = 32)]
    public required BaseBinary ApplicationDiscriminatorDigest { get; init; }

    [BaseField("auth.activation.data-protection.refresh.contractVersion", MinimumInt32 = 1, HasMinimumInt32 = true,
        MaximumInt32 = 1, HasMaximumInt32 = true)]
    public required int ContractVersion { get; init; }
}

internal sealed record AuthDataProtectionRefreshResultV1
{
    [BaseField("auth.activation.data-protection.refresh.result.cacheGeneration", MinimumInt64 = 0,
        HasMinimumInt64 = true)]
    public required long CacheGeneration { get; init; }
}

internal sealed class AuthDeclarationHandler<TInput, TResult> : IBaseActivationHandler<TInput, TResult>
{
    public ValueTask<BaseActivationHandlerResult<TResult>> ExecuteAsync(
        BaseActivationContext context,
        TInput input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(new BaseActivationHandlerResult<TResult>
        {
            FailureCode = "auth.persistence.unavailable",
            Retryable = true,
        });
    }
}

[BaseActivationDtoAuthority("hpd.auth.cleanup.bootstrap.user.dto.v1", 1, AuthBaseContract.ModuleId,
    "hpd.auth.type.auth-user-cleanup-initialize-v1.v1", "hpd.auth.type.auth-cleanup-initialize-result-v1.v1",
    typeof(AuthBaseJsonSerializerContext), typeof(AuthUserCleanupInitializeV1), typeof(AuthCleanupInitializeResultV1))]
internal static partial class AuthUserCleanupBootstrapDtos;

[BaseActivationDtoAuthority("hpd.auth.cleanup.bootstrap.role.dto.v1", 1, AuthBaseContract.ModuleId,
    "hpd.auth.type.auth-role-cleanup-initialize-v1.v1", "hpd.auth.type.auth-cleanup-initialize-result-v1.v1",
    typeof(AuthBaseJsonSerializerContext), typeof(AuthRoleCleanupInitializeV1), typeof(AuthCleanupInitializeResultV1))]
internal static partial class AuthRoleCleanupBootstrapDtos;

[BaseActivationDtoAuthority("hpd.auth.cleanup.semantic-retire.user.dto.v1", 1, AuthBaseContract.ModuleId,
    "hpd.auth.type.auth-user-cleanup-initialize-v1.v1", "hpd.auth.type.auth-cleanup-retirement-result-v1.v1",
    typeof(AuthBaseJsonSerializerContext), typeof(AuthUserCleanupInitializeV1), typeof(AuthCleanupRetirementResultV1))]
internal static partial class AuthUserCleanupRetirementDtos;

[BaseActivationDtoAuthority("hpd.auth.cleanup.semantic-retire.role.dto.v1", 1, AuthBaseContract.ModuleId,
    "hpd.auth.type.auth-role-cleanup-initialize-v1.v1", "hpd.auth.type.auth-cleanup-retirement-result-v1.v1",
    typeof(AuthBaseJsonSerializerContext), typeof(AuthRoleCleanupInitializeV1), typeof(AuthCleanupRetirementResultV1))]
internal static partial class AuthRoleCleanupRetirementDtos;

[BaseActivationDtoAuthority("hpd.auth.cleanup.reconcile.dto.v1", 1, AuthBaseContract.ModuleId,
    "hpd.auth.type.auth-cleanup-reconcile-input-v1.v1", "hpd.auth.type.auth-cleanup-reconcile-result-v1.v1",
    typeof(AuthBaseJsonSerializerContext), typeof(AuthCleanupReconcileInputV1), typeof(AuthCleanupReconcileResultV1))]
internal static partial class AuthCleanupReconcileDtos;

[BaseActivationDtoAuthority("hpd.auth.expiration.dto.v1", 1, AuthBaseContract.ModuleId,
    "hpd.auth.type.auth-expiration-trigger-input-v1.v1", "hpd.auth.type.auth-expiration-result-v1.v1",
    typeof(AuthBaseJsonSerializerContext), typeof(AuthExpirationTriggerInputV1), typeof(AuthExpirationResultV1))]
internal static partial class AuthExpirationDtos;

[BaseActivationDtoAuthority("hpd.auth.data-protection.refresh.dto.v1", 1, AuthBaseContract.ModuleId,
    "hpd.auth.type.auth-data-protection-refresh-input-v1.v1", "hpd.auth.type.auth-data-protection-refresh-result-v1.v1",
    typeof(AuthBaseJsonSerializerContext), typeof(AuthDataProtectionRefreshInputV1), typeof(AuthDataProtectionRefreshResultV1))]
internal static partial class AuthDataProtectionRefreshDtos;

internal static class AuthLifecycleActivationDeclarations
{
    internal static BaseActivationHandlerRegistration<AuthUserCleanupInitializeV1, AuthCleanupInitializeResultV1> BootstrapUser { get; } =
        Create("hpd.auth.cleanup.bootstrap.user.v1", "hpd.auth.handler.cleanup.bootstrap.user",
            "hpd.auth.factory.cleanup.bootstrap.user.v1",
            ["auth.cleanup.execute", "auth.operation.cleanup.initialize.user", "auth.semantic.cleanup.user.ensure"],
            AuthUserCleanupBootstrapDtos.HPDBaseActivationDtoAuthority);

    internal static BaseActivationHandlerRegistration<AuthRoleCleanupInitializeV1, AuthCleanupInitializeResultV1> BootstrapRole { get; } =
        Create("hpd.auth.cleanup.bootstrap.role.v1", "hpd.auth.handler.cleanup.bootstrap.role",
            "hpd.auth.factory.cleanup.bootstrap.role.v1",
            ["auth.cleanup.execute", "auth.operation.cleanup.initialize.role", "auth.semantic.cleanup.role.ensure"],
            AuthRoleCleanupBootstrapDtos.HPDBaseActivationDtoAuthority);

    internal static BaseActivationHandlerRegistration<AuthUserCleanupInitializeV1, AuthCleanupRetirementResultV1> RetireUser { get; } =
        Create("hpd.auth.cleanup.semantic-retire.user.v1", "hpd.auth.handler.cleanup.semantic-retire.user",
            "hpd.auth.factory.cleanup.semantic-retire.user.v1",
            ["auth.cleanup.execute", "auth.operation.cleanup.retire.user", "auth.semantic.cleanup.user.retire"],
            AuthUserCleanupRetirementDtos.HPDBaseActivationDtoAuthority, semanticRetirement: true);

    internal static BaseActivationHandlerRegistration<AuthRoleCleanupInitializeV1, AuthCleanupRetirementResultV1> RetireRole { get; } =
        Create("hpd.auth.cleanup.semantic-retire.role.v1", "hpd.auth.handler.cleanup.semantic-retire.role",
            "hpd.auth.factory.cleanup.semantic-retire.role.v1",
            ["auth.cleanup.execute", "auth.operation.cleanup.retire.role", "auth.semantic.cleanup.role.retire"],
            AuthRoleCleanupRetirementDtos.HPDBaseActivationDtoAuthority, semanticRetirement: true);

    internal static BaseActivationHandlerRegistration<AuthCleanupReconcileInputV1, AuthCleanupReconcileResultV1> Reconcile { get; } =
        Create("hpd.auth.cleanup.reconcile.v1", "hpd.auth.handler.cleanup.reconcile",
            "hpd.auth.factory.cleanup.reconcile.v1",
            ["auth.cleanup.execute", "auth.operation.cleanup.advance", "auth.operation.cleanup.initialize.role",
                "auth.operation.cleanup.initialize.user", "auth.semantic.cleanup.role.ensure", "auth.semantic.cleanup.user.ensure",
                "hpd.auth.cleanup.role.v1.enqueue", "hpd.auth.cleanup.user.v1.enqueue"],
            AuthCleanupReconcileDtos.HPDBaseActivationDtoAuthority, reconciliation: true);

    internal static BaseActivationHandlerRegistration<AuthExpirationTriggerInputV1, AuthExpirationResultV1> Sessions { get; } =
        Create("hpd.auth.expiration.sessions.v1", "hpd.auth.handler.expiration.sessions",
            "hpd.auth.factory.expiration.sessions.v1", ["auth.cleanup.execute", "auth.session.mutate"],
            AuthExpirationDtos.HPDBaseActivationDtoAuthority);

    internal static BaseActivationHandlerRegistration<AuthExpirationTriggerInputV1, AuthExpirationResultV1> RefreshTokens { get; } =
        Create("hpd.auth.expiration.refresh-tokens.v1", "hpd.auth.handler.expiration.refresh-tokens",
            "hpd.auth.factory.expiration.refresh-tokens.v1", ["auth.cleanup.execute", "auth.token.mutate"],
            AuthExpirationDtos.HPDBaseActivationDtoAuthority);

    internal static BaseActivationHandlerRegistration<AuthExpirationTriggerInputV1, AuthExpirationResultV1> Deliveries { get; } =
        Create("hpd.auth.expiration.deliveries.v1", "hpd.auth.handler.expiration.deliveries",
            "hpd.auth.factory.expiration.deliveries.v1", ["auth.cleanup.execute", "auth.token.delivery"],
            AuthExpirationDtos.HPDBaseActivationDtoAuthority);

    internal static BaseActivationHandlerRegistration<AuthDataProtectionRefreshInputV1, AuthDataProtectionRefreshResultV1> DataProtection { get; } =
        Create("hpd.auth.data-protection.refresh.v1", "hpd.auth.handler.data-protection.refresh",
            "hpd.auth.factory.data-protection.refresh.v1", ["auth.dataProtection.read"],
            AuthDataProtectionRefreshDtos.HPDBaseActivationDtoAuthority);

    private static BaseActivationHandlerRegistration<TInput, TResult> Create<TInput, TResult>(
        string id,
        string handlerId,
        string factoryId,
        IEnumerable<string> sourceGrants,
        BaseGeneratedActivationDtoAuthority<TInput, TResult> authority,
        bool semanticRetirement = false,
        bool reconciliation = false) =>
        AuthActivationDefinitionFactory.Create(id, handlerId, factoryId, sourceGrants, authority,
            static _ => new AuthDeclarationHandler<TInput, TResult>(), semanticRetirement, reconciliation);
}

internal static class AuthScheduleDeclarations
{
    internal static IReadOnlyList<BaseGeneratedScheduleRegistration> Create(BaseBinary applicationDiscriminatorDigest)
    {
        ArgumentNullException.ThrowIfNull(applicationDiscriminatorDigest);
        if (applicationDiscriminatorDigest.Length != 32)
            throw new ArgumentException("The Auth Data Protection discriminator digest must contain exactly 32 bytes.",
                nameof(applicationDiscriminatorDigest));

        return
        [
            Schedule("hpd.auth.schedule.cleanup-reconcile.v1", new BaseCronSchedule("0 7 * * * *", "UTC"),
                BaseScheduleMisfirePolicy.RunLatest, 8, AuthLifecycleActivationDeclarations.Reconcile,
                AuthCleanupReconcileDtos.HPDBaseActivationDtoAuthority, new AuthCleanupReconcileInputV1 { ContractVersion = 1 }),
            Schedule("hpd.auth.schedule.session-expiration.v1", new BaseIntervalSchedule(0, 300_000),
                BaseScheduleMisfirePolicy.RunLatest, 12, AuthLifecycleActivationDeclarations.Sessions,
                AuthExpirationDtos.HPDBaseActivationDtoAuthority,
                new AuthExpirationTriggerInputV1 { Kind = AuthMaintenanceKindV1.sessionExpiration, ContractVersion = 1 }),
            Schedule("hpd.auth.schedule.refresh-expiration.v1", new BaseIntervalSchedule(0, 900_000),
                BaseScheduleMisfirePolicy.RunLatest, 10, AuthLifecycleActivationDeclarations.RefreshTokens,
                AuthExpirationDtos.HPDBaseActivationDtoAuthority,
                new AuthExpirationTriggerInputV1 { Kind = AuthMaintenanceKindV1.refreshExpiration, ContractVersion = 1 }),
            Schedule("hpd.auth.schedule.delivery-expiration.v1", new BaseIntervalSchedule(0, 300_000),
                BaseScheduleMisfirePolicy.RunLatest, 16, AuthLifecycleActivationDeclarations.Deliveries,
                AuthExpirationDtos.HPDBaseActivationDtoAuthority,
                new AuthExpirationTriggerInputV1 { Kind = AuthMaintenanceKindV1.deliveryExpiration, ContractVersion = 1 }),
            Schedule("hpd.auth.schedule.data-protection-refresh.v1", new BaseIntervalSchedule(0, 30_000),
                BaseScheduleMisfirePolicy.Skip, 20, AuthLifecycleActivationDeclarations.DataProtection,
                AuthDataProtectionRefreshDtos.HPDBaseActivationDtoAuthority,
                new AuthDataProtectionRefreshInputV1
                {
                    ApplicationDiscriminatorDigest = BaseBinary.From(applicationDiscriminatorDigest.ToArray()),
                    ContractVersion = 1,
                }),
        ];
    }

    private static BaseGeneratedScheduleRegistration Schedule<TInput, TResult>(
        string id,
        BaseScheduleExpression expression,
        BaseScheduleMisfirePolicy misfire,
        int priority,
        BaseActivationHandlerRegistration<TInput, TResult> activation,
        BaseGeneratedActivationDtoAuthority<TInput, TResult> authority,
        TInput input) =>
        BaseScheduleDefinitionBuilder.CreateGenerated(new BaseScheduleDefinitionDraft
        {
            Id = id,
            Version = 1,
            OwningModuleId = AuthBaseContract.ModuleId,
            ManageGrantId = id + ".manage",
            MaterializeGrantId = id + ".materialize",
            Expression = expression,
            GapPolicy = BaseTimeGapPolicy.Skip,
            TimeOverlapPolicy = BaseTimeOverlapPolicy.EarlierOffset,
            MisfirePolicy = misfire,
            ActivationOverlapPolicy = BaseScheduleOverlapPolicy.SkipWhileActive,
            OverlapKeyKind = BaseScheduleOverlapKeyKind.Schedule,
            ConcurrencyKey = ImmutableArray<byte>.Empty,
            Priority = priority,
            MaximumSplayMilliseconds = 0,
        }, activation, authority, input);
}
