using System.Collections.Immutable;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HPD.Base;

namespace HPD.Auth.Base;

internal sealed class AuthUserCleanupActivationHandler
    : IBaseActivationHandler<AuthUserCleanupInputV1, AuthCleanupResultV1>
{
    public ValueTask<BaseActivationHandlerResult<AuthCleanupResultV1>> ExecuteAsync(
        BaseActivationContext context,
        AuthUserCleanupInputV1 input,
        CancellationToken cancellationToken) =>
        AuthCleanupActivationHandler.ExecuteUserAsync(context, input, cancellationToken);
}

internal sealed class AuthRoleCleanupActivationHandler
    : IBaseActivationHandler<AuthRoleCleanupInputV1, AuthCleanupResultV1>
{
    public ValueTask<BaseActivationHandlerResult<AuthCleanupResultV1>> ExecuteAsync(
        BaseActivationContext context,
        AuthRoleCleanupInputV1 input,
        CancellationToken cancellationToken) =>
        AuthCleanupActivationHandler.ExecuteRoleAsync(context, input, cancellationToken);
}

internal static class AuthCleanupActivationHandler
{
    internal static async ValueTask<BaseActivationHandlerResult<AuthCleanupResultV1>> ExecuteUserAsync(
        BaseActivationContext context,
        AuthUserCleanupInputV1 input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();

        BaseResult<AuthCleanupWorkReadV1.Row?> read = await ReadAsync(
            context, input.TenantId, AuthCleanupSubjectKindV1.user, input.SubjectId,
            input.Incarnation, cancellationToken).ConfigureAwait(false);
        if (read is BaseFailure<AuthCleanupWorkReadV1.Row?> failure)
            return AuthActivationFailureMapper.Map<AuthCleanupResultV1>(failure.Error);
        AuthCleanupWorkReadV1.Row? work = read.RequireValue();
        if (work is null || !Matches(work, input))
            return AuthActivationFailureMapper.Domain<AuthCleanupResultV1>("auth.cleanup.reconcileConflict");
        if (work.State == AuthCleanupStateV1.complete)
            return Completed(work);
        DateTimeOffset operationTime = DateTimeOffset.FromUnixTimeMilliseconds(context.Claim.SliceStartedAt);
        if (work.State == AuthCleanupStateV1.readyToPurge)
            return await PrepareUserRetirementAsync(context, input, work, operationTime, cancellationToken).ConfigureAwait(false);
        if (work.State == AuthCleanupStateV1.awaitingSemanticRetirement)
            return AuthActivationFailureMapper.Domain<AuthCleanupResultV1>("auth.cleanup.retirementPending");

        ValueTask<BaseResult<BaseSelectionMutationResult>>? pendingCohort = ExecuteUserCohortAsync(
            context, work, operationTime, cancellationToken);
        BaseResult<BaseSelectionMutationResult>? cohort = pendingCohort is { } userCohort
            ? await userCohort.ConfigureAwait(false)
            : null;
        if (cohort is BaseFailure<BaseSelectionMutationResult> cohortFailure)
            return AuthActivationFailureMapper.Map<AuthCleanupResultV1>(cohortFailure.Error);

        return await AdvanceUserAsync(
            context, input, work, input.Incarnation, cohort?.RequireValue(), operationTime, cancellationToken).ConfigureAwait(false);
    }

    internal static async ValueTask<BaseActivationHandlerResult<AuthCleanupResultV1>> ExecuteRoleAsync(
        BaseActivationContext context,
        AuthRoleCleanupInputV1 input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();

        BaseResult<AuthCleanupWorkReadV1.Row?> read = await ReadAsync(
            context, input.TenantId, AuthCleanupSubjectKindV1.role, input.SubjectId,
            input.Incarnation, cancellationToken).ConfigureAwait(false);
        if (read is BaseFailure<AuthCleanupWorkReadV1.Row?> failure)
            return AuthActivationFailureMapper.Map<AuthCleanupResultV1>(failure.Error);
        AuthCleanupWorkReadV1.Row? work = read.RequireValue();
        if (work is null || !Matches(work, input))
            return AuthActivationFailureMapper.Domain<AuthCleanupResultV1>("auth.cleanup.reconcileConflict");
        if (work.State == AuthCleanupStateV1.complete)
            return Completed(work);
        DateTimeOffset operationTime = DateTimeOffset.FromUnixTimeMilliseconds(context.Claim.SliceStartedAt);
        if (work.State == AuthCleanupStateV1.readyToPurge)
            return await PrepareRoleRetirementAsync(context, input, work, operationTime, cancellationToken).ConfigureAwait(false);
        if (work.State == AuthCleanupStateV1.awaitingSemanticRetirement)
            return AuthActivationFailureMapper.Domain<AuthCleanupResultV1>("auth.cleanup.retirementPending");

        ValueTask<BaseResult<BaseSelectionMutationResult>>? pendingCohort = ExecuteRoleCohortAsync(
            context, work, cancellationToken);
        BaseResult<BaseSelectionMutationResult>? cohort = pendingCohort is { } roleCohort
            ? await roleCohort.ConfigureAwait(false)
            : null;
        if (cohort is BaseFailure<BaseSelectionMutationResult> cohortFailure)
            return AuthActivationFailureMapper.Map<AuthCleanupResultV1>(cohortFailure.Error);

        return await AdvanceRoleAsync(
            context, input, work, input.Incarnation, cohort?.RequireValue(), operationTime, cancellationToken).ConfigureAwait(false);
    }

    private static ValueTask<BaseResult<AuthCleanupWorkReadV1.Row?>> ReadAsync(
        BaseActivationContext context,
        Guid tenantId,
        AuthCleanupSubjectKindV1 kind,
        Guid subjectId,
        BaseSubjectIncarnation incarnation,
        CancellationToken cancellationToken) =>
        context.Reads.FirstAsync(AuthCleanupWorkReadV1.Handle, new AuthCleanupWorkReadV1
        {
            TenantId = tenantId,
            SubjectKind = kind,
            SubjectId = subjectId,
            Incarnation = BaseBinary.From(incarnation.ToArray()),
        }, cancellationToken);

    private static bool Matches(AuthCleanupWorkReadV1.Row work, AuthUserCleanupInputV1 input) =>
        work.TenantId == input.TenantId && work.SubjectKind == AuthCleanupSubjectKindV1.user
        && work.SubjectId == input.SubjectId && work.UserSubject == input.Subject
        && work.RoleSubject is null && work.Incarnation.ToArray().AsSpan().SequenceEqual(input.Incarnation.ToArray())
        && work.TombstoneSequence == input.TombstoneSequence
        && string.Equals(work.TombstoneRevision, input.TombstoneRevision, StringComparison.Ordinal)
        && work.WorkflowVersion == input.WorkflowVersion;

    private static bool Matches(AuthCleanupWorkReadV1.Row work, AuthRoleCleanupInputV1 input) =>
        work.TenantId == input.TenantId && work.SubjectKind == AuthCleanupSubjectKindV1.role
        && work.SubjectId == input.SubjectId && work.RoleSubject == input.Subject
        && work.UserSubject is null && work.Incarnation.ToArray().AsSpan().SequenceEqual(input.Incarnation.ToArray())
        && work.TombstoneSequence == input.TombstoneSequence
        && string.Equals(work.TombstoneRevision, input.TombstoneRevision, StringComparison.Ordinal)
        && work.WorkflowVersion == input.WorkflowVersion;

    private static ValueTask<BaseActivationHandlerResult<AuthCleanupResultV1>> PrepareUserRetirementAsync(
        BaseActivationContext context,
        AuthUserCleanupInputV1 input,
        AuthCleanupWorkReadV1.Row work,
        DateTimeOffset operationTime,
        CancellationToken cancellationToken)
    {
        string receiptScope = $"activation:{context.Claim.ActivationId}";
        return PrepareRetirementAsync(
            context,
            work,
            AuthCleanupSubjectKindV1.user,
            input.Incarnation,
            new AuthUserCleanupInitializeV1
            {
                CleanupWorkId = work.Id,
                TenantId = input.TenantId,
                SubjectId = input.SubjectId,
                Subject = input.Subject,
                Incarnation = input.Incarnation,
                TombstoneSequence = input.TombstoneSequence,
                TombstoneRevision = input.TombstoneRevision,
                WorkflowVersion = input.WorkflowVersion,
                TombstonedAt = work.CreatedAt,
                RetirementReceiptScope = receiptScope,
                OperationTime = operationTime,
            },
            AuthLifecycleActivationDeclarations.RetireUser.Identity,
            receiptScope,
            operationTime,
            cancellationToken);
    }

    private static ValueTask<BaseActivationHandlerResult<AuthCleanupResultV1>> PrepareRoleRetirementAsync(
        BaseActivationContext context,
        AuthRoleCleanupInputV1 input,
        AuthCleanupWorkReadV1.Row work,
        DateTimeOffset operationTime,
        CancellationToken cancellationToken)
    {
        string receiptScope = $"activation:{context.Claim.ActivationId}";
        return PrepareRetirementAsync(
            context,
            work,
            AuthCleanupSubjectKindV1.role,
            input.Incarnation,
            new AuthRoleCleanupInitializeV1
            {
                CleanupWorkId = work.Id,
                TenantId = input.TenantId,
                SubjectId = input.SubjectId,
                Subject = input.Subject,
                Incarnation = input.Incarnation,
                TombstoneSequence = input.TombstoneSequence,
                TombstoneRevision = input.TombstoneRevision,
                WorkflowVersion = input.WorkflowVersion,
                TombstonedAt = work.CreatedAt,
                RetirementReceiptScope = receiptScope,
                OperationTime = operationTime,
            },
            AuthLifecycleActivationDeclarations.RetireRole.Identity,
            receiptScope,
            operationTime,
            cancellationToken);
    }

    private static async ValueTask<BaseActivationHandlerResult<AuthCleanupResultV1>> PrepareRetirementAsync<TInput>(
        BaseActivationContext context,
        AuthCleanupWorkReadV1.Row work,
        AuthCleanupSubjectKindV1 subjectKind,
        BaseSubjectIncarnation incarnation,
        TInput retirementInput,
        BaseActivationRegistrationIdentity<TInput, AuthCleanupRetirementResultV1> retirement,
        string receiptScope,
        DateTimeOffset operationTime,
        CancellationToken cancellationToken)
    {
        var request = new AuthCleanupPrepareRetirementV1
        {
            CleanupWorkId = work.Id,
            SubjectKind = subjectKind,
            ExpectedRevision = work.Revision,
            ExpectedIncarnation = incarnation,
            ExpectedTombstoneSequence = work.TombstoneSequence,
            RetirementReceiptScope = receiptScope,
            OperationTime = operationTime,
        };
        BaseMutationRequestFingerprint fingerprint = Fingerprint(
            "hpd.auth.cleanup.prepare-retirement.v1", work.Id, work.Revision.Value,
            subjectKind.ToString(), work.TombstoneSequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            receiptScope, operationTime.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture));
        BaseMutationRequestIdentity identity = context.DeriveChildIdentity("cleanup-prepare-retirement", 3, fingerprint);
        BaseModuleMutationExecutionOptions options = context.GuardModuleMutationAndCreateActivation(
            "cleanup-prepare-retirement", 3, fingerprint, retirement, retirementInput,
            context.Claim.SliceStartedAt, "semantic-retirement", 1);
        BaseResult<BaseModuleMutationExecutionResult<AuthCleanupMutationResultV1>> prepared =
            await context.ExecuteModuleMutationAsync(
                AuthCleanupPrepareRetirementOperationV1.Identity, request, identity, options, cancellationToken)
                .ConfigureAwait(false);
        if (prepared is BaseFailure<BaseModuleMutationExecutionResult<AuthCleanupMutationResultV1>> failure)
            return AuthActivationFailureMapper.Map<AuthCleanupResultV1>(failure.Error);
        AuthCleanupMutationResultV1 result = prepared.RequireValue().Result;
        return new BaseActivationSucceeded<AuthCleanupResultV1>
        {
            Result = new AuthCleanupResultV1
            {
                Completed = false,
                State = result.State,
                Step = result.Step,
                ChunkOrdinal = result.ChunkOrdinal,
                SelectedCount = 0,
                RetentionEligibleAt = result.RetentionEligibleAt,
            },
        };
    }

    private static async ValueTask<BaseActivationHandlerResult<AuthCleanupResultV1>> AdvanceUserAsync(
        BaseActivationContext context,
        AuthUserCleanupInputV1 input,
        AuthCleanupWorkReadV1.Row work,
        BaseSubjectIncarnation incarnation,
        BaseSelectionMutationResult? cohort,
        DateTimeOffset operationTime,
        CancellationToken cancellationToken) =>
        await AdvanceAsync(context, input, AuthCleanupActivationDeclarations.User.Identity,
            work, incarnation, cohort, operationTime, cancellationToken).ConfigureAwait(false);

    private static async ValueTask<BaseActivationHandlerResult<AuthCleanupResultV1>> AdvanceRoleAsync(
        BaseActivationContext context,
        AuthRoleCleanupInputV1 input,
        AuthCleanupWorkReadV1.Row work,
        BaseSubjectIncarnation incarnation,
        BaseSelectionMutationResult? cohort,
        DateTimeOffset operationTime,
        CancellationToken cancellationToken) =>
        await AdvanceAsync(context, input, AuthCleanupActivationDeclarations.Role.Identity,
            work, incarnation, cohort, operationTime, cancellationToken).ConfigureAwait(false);

    private static async ValueTask<BaseActivationHandlerResult<AuthCleanupResultV1>> AdvanceAsync<TInput>(
        BaseActivationContext context,
        TInput continuationInput,
        BaseActivationRegistrationIdentity<TInput, AuthCleanupResultV1> continuation,
        AuthCleanupWorkReadV1.Row work,
        BaseSubjectIncarnation incarnation,
        BaseSelectionMutationResult? cohort,
        DateTimeOffset operationTime,
        CancellationToken cancellationToken)
    {
        int selectedCount = cohort?.SelectedCount ?? 0;
        AuthCleanupChildDispositionV1 disposition;
        DateTimeOffset? retentionEligibleAt = null;
        long dueAt = context.EffectiveDueAt;
        if (work.Step == AuthCleanupStepV1.waitSecurityRetention)
        {
            retentionEligibleAt = work.CreatedAt.AddDays(30);
            if (operationTime < retentionEligibleAt.Value)
            {
                disposition = AuthCleanupChildDispositionV1.retentionBlocked;
                dueAt = retentionEligibleAt.Value.ToUnixTimeMilliseconds();
            }
            else
            {
                disposition = AuthCleanupChildDispositionV1.zeroDrainProof;
            }
        }
        else if (work.Step == AuthCleanupStepV1.finalizeSubject)
        {
            disposition = AuthCleanupChildDispositionV1.allStepsComplete;
        }
        else
        {
            disposition = selectedCount == 0
                ? AuthCleanupChildDispositionV1.zeroDrainProof
                : AuthCleanupChildDispositionV1.positiveCohort;
        }

        string receiptScope = cohort is null
            ? $"activation:{context.Claim.ActivationId}"
            : SelectionIdentity(context, work).Scope;
        var request = new AuthCleanupAdvanceV1
        {
            CleanupWorkId = work.Id,
            ExpectedRevision = work.Revision,
            ExpectedState = work.State,
            ExpectedStep = work.Step,
            ExpectedChunkOrdinal = work.ChunkOrdinal,
            ExpectedIncarnation = incarnation,
            ChildDisposition = disposition,
            SelectedCount = selectedCount,
            ChildReceiptScope = receiptScope,
            RetentionEligibleAt = retentionEligibleAt,
            OperationTime = operationTime,
        };
        BaseMutationRequestFingerprint fingerprint = Fingerprint(
            "hpd.auth.cleanup.advance.v1", work.Id, work.Step.ToString(),
            work.ChunkOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture),
            disposition.ToString(), selectedCount.ToString(System.Globalization.CultureInfo.InvariantCulture),
            receiptScope, operationTime.UtcTicks.ToString(System.Globalization.CultureInfo.InvariantCulture));
        BaseMutationRequestIdentity identity = context.DeriveChildIdentity("cleanup-advance", 2, fingerprint);
        BaseModuleMutationExecutionOptions options = disposition == AuthCleanupChildDispositionV1.retentionBlocked
            ? context.GuardModuleMutationAndCreateActivation(
                "cleanup-advance", 2, fingerprint, continuation, continuationInput,
                dueAt, "retention-continuation", 1)
            : context.GuardModuleMutation("cleanup-advance", 2, fingerprint);
        BaseResult<BaseModuleMutationExecutionResult<AuthCleanupMutationResultV1>> advanced =
            await context.ExecuteModuleMutationAsync(
                AuthCleanupAdvanceOperationV1.Identity, request, identity, options, cancellationToken)
                .ConfigureAwait(false);
        if (advanced is BaseFailure<BaseModuleMutationExecutionResult<AuthCleanupMutationResultV1>> failure)
            return AuthActivationFailureMapper.Map<AuthCleanupResultV1>(failure.Error);
        AuthCleanupMutationResultV1 result = advanced.RequireValue().Result;
        var resultValue = new AuthCleanupResultV1
        {
            Completed = result.State == AuthCleanupStateV1.complete,
            State = result.State,
            Step = result.Step,
            ChunkOrdinal = result.ChunkOrdinal,
            SelectedCount = selectedCount,
            RetentionEligibleAt = result.RetentionEligibleAt,
        };
        return ResolveAdvanceOutcome(
            work.Id, result.CompletedSteps, result.Revision, disposition, resultValue);
    }

    internal static BaseActivationHandlerResult<AuthCleanupResultV1> ResolveAdvanceOutcome(
        string cleanupWorkId,
        long completedSteps,
        RevisionToken revision,
        AuthCleanupChildDispositionV1 disposition,
        AuthCleanupResultV1 result)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(cleanupWorkId);
        ArgumentNullException.ThrowIfNull(result);
        if (disposition == AuthCleanupChildDispositionV1.retentionBlocked
            || result.State == AuthCleanupStateV1.complete)
            return new BaseActivationSucceeded<AuthCleanupResultV1> { Result = result };

        byte[] progress = SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n',
            "hpd.auth.cleanup.progress.v1", cleanupWorkId, result.State.ToString(), result.Step.ToString(),
            result.ChunkOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture),
            completedSteps.ToString(System.Globalization.CultureInfo.InvariantCulture),
            revision.Value)));
        return new BaseActivationYielded<AuthCleanupResultV1>
        {
            Yield = new BaseActivationYield
            {
                ResumeAt = null,
                ProgressFingerprint = BaseActivationProgressFingerprint.Create(progress),
            },
        };
    }

    private static ValueTask<BaseResult<BaseSelectionMutationResult>>? ExecuteUserCohortAsync(
        BaseActivationContext context,
        AuthCleanupWorkReadV1.Row work,
        DateTimeOffset operationTime,
        CancellationToken cancellationToken)
    {
        BaseRecordId<AuthUserRecordV1> userId = BaseRecordId<AuthUserRecordV1>.Create(work.SubjectId.ToString("D"));
        return work.Step switch
        {
            AuthCleanupStepV1.revokeSessions => RevokeSessionsAsync(context, work, userId, operationTime, cancellationToken),
            AuthCleanupStepV1.revokeRefreshTokens => RevokeRefreshTokensAsync(context, work, userId, operationTime, cancellationToken),
            AuthCleanupStepV1.deleteDeliveries => DeleteByUserAsync(context, work, AuthRefreshTokenDeliveryRecordV1.Collection,
                AuthRefreshTokenDeliveryRecordV1.Fields.TenantId, AuthRefreshTokenDeliveryRecordV1.Fields.UserId, AuthSelectionProfiles.RefreshTokenDeliveriesDeleteExpired, userId, cancellationToken),
            AuthCleanupStepV1.deleteSessions => DeleteByUserAsync(context, work, AuthSessionRecordV1.Collection,
                AuthSessionRecordV1.Fields.TenantId, AuthSessionRecordV1.Fields.UserId, AuthSelectionProfiles.SessionsDeleteUser, userId, cancellationToken),
            AuthCleanupStepV1.deleteRefreshTokens => DeleteByUserAsync(context, work, AuthRefreshTokenRecordV1.Collection,
                AuthRefreshTokenRecordV1.Fields.TenantId, AuthRefreshTokenRecordV1.Fields.UserId, AuthSelectionProfiles.RefreshTokensDeleteUser, userId, cancellationToken),
            AuthCleanupStepV1.deletePasskeys => DeleteByUserAsync(context, work, AuthPasskeyRecordV1.Collection,
                AuthPasskeyRecordV1.Fields.TenantId, AuthPasskeyRecordV1.Fields.UserId, AuthSelectionProfiles.PasskeysDeleteUser, userId, cancellationToken),
            AuthCleanupStepV1.deleteUserClaims => DeleteByUserAsync(context, work, AuthUserClaimRecordV1.Collection,
                AuthUserClaimRecordV1.Fields.TenantId, AuthUserClaimRecordV1.Fields.UserId, AuthSelectionProfiles.UserClaimsDeleteUser, userId, cancellationToken),
            AuthCleanupStepV1.deleteUserLogins => DeleteByUserAsync(context, work, AuthUserLoginRecordV1.Collection,
                AuthUserLoginRecordV1.Fields.TenantId, AuthUserLoginRecordV1.Fields.UserId, AuthSelectionProfiles.UserLoginsDeleteUser, userId, cancellationToken),
            AuthCleanupStepV1.deleteUserTokens => DeleteByUserAsync(context, work, AuthUserTokenRecordV1.Collection,
                AuthUserTokenRecordV1.Fields.TenantId, AuthUserTokenRecordV1.Fields.UserId, AuthSelectionProfiles.UserTokensDeleteUser, userId, cancellationToken),
            AuthCleanupStepV1.deleteUserRoles => DeleteByUserAsync(context, work, AuthUserRoleRecordV1.Collection,
                AuthUserRoleRecordV1.Fields.TenantId, AuthUserRoleRecordV1.Fields.UserId, AuthSelectionProfiles.UserRolesDeleteUser, userId, cancellationToken),
            AuthCleanupStepV1.deleteUserIdentities => DeleteByUserAsync(context, work, AuthUserIdentityRecordV1.Collection,
                AuthUserIdentityRecordV1.Fields.TenantId, AuthUserIdentityRecordV1.Fields.UserId, AuthSelectionProfiles.UserIdentitiesDeleteUser, userId, cancellationToken),
            _ => null,
        };
    }

    private static ValueTask<BaseResult<BaseSelectionMutationResult>>? ExecuteRoleCohortAsync(
        BaseActivationContext context,
        AuthCleanupWorkReadV1.Row work,
        CancellationToken cancellationToken)
    {
        BaseRecordId<AuthRoleRecordV1> roleId = BaseRecordId<AuthRoleRecordV1>.Create(work.SubjectId.ToString("D"));
        return work.Step switch
        {
            AuthCleanupStepV1.deleteRoleClaims => DeleteByRoleAsync(context, work, AuthRoleClaimRecordV1.Collection,
                AuthRoleClaimRecordV1.Fields.TenantId, AuthRoleClaimRecordV1.Fields.RoleId, AuthSelectionProfiles.RoleClaimsDeleteRole, roleId, cancellationToken),
            AuthCleanupStepV1.deleteUserRoles => DeleteByRoleAsync(context, work, AuthUserRoleRecordV1.Collection,
                AuthUserRoleRecordV1.Fields.TenantId, AuthUserRoleRecordV1.Fields.RoleId, AuthSelectionProfiles.UserRolesDeleteRole, roleId, cancellationToken),
            _ => null,
        };
    }

    private static ValueTask<BaseResult<BaseSelectionMutationResult>> DeleteByUserAsync<TRecord>(
        BaseActivationContext context,
        AuthCleanupWorkReadV1.Row work,
        BaseCollection<TRecord> collection,
        BaseField<TRecord, Guid> tenantField,
        BaseField<TRecord, BaseRecordId<AuthUserRecordV1>> userField,
        BaseGeneratedSelectionProfileIdentity profile,
        BaseRecordId<AuthUserRecordV1> userId,
        CancellationToken cancellationToken)
    {
        BaseCollectionSession<TRecord> session = context.Collection(collection);
        BaseMutationRequestIdentity identity = SelectionIdentity(context, work);
        return session.Query().Where(tenantField, work.TenantId).Where(userField, userId).ThenByRecordId().Take(200)
            .DeleteSelectedAsync(session.GetDeleteSelectionProfile(profile), BasePreviousStateRequirement.None,
                identity, context.GuardSelectionMutation("cleanup-cohort", 1, identity), cancellationToken);
    }

    private static ValueTask<BaseResult<BaseSelectionMutationResult>> DeleteByRoleAsync<TRecord>(
        BaseActivationContext context,
        AuthCleanupWorkReadV1.Row work,
        BaseCollection<TRecord> collection,
        BaseField<TRecord, Guid> tenantField,
        BaseField<TRecord, BaseRecordId<AuthRoleRecordV1>> roleField,
        BaseGeneratedSelectionProfileIdentity profile,
        BaseRecordId<AuthRoleRecordV1> roleId,
        CancellationToken cancellationToken)
    {
        BaseCollectionSession<TRecord> session = context.Collection(collection);
        BaseMutationRequestIdentity identity = SelectionIdentity(context, work);
        return session.Query().Where(tenantField, work.TenantId).Where(roleField, roleId).ThenByRecordId().Take(200)
            .DeleteSelectedAsync(session.GetDeleteSelectionProfile(profile), BasePreviousStateRequirement.None,
                identity, context.GuardSelectionMutation("cleanup-cohort", 1, identity), cancellationToken);
    }

    private static ValueTask<BaseResult<BaseSelectionMutationResult>> RevokeSessionsAsync(
        BaseActivationContext context,
        AuthCleanupWorkReadV1.Row work,
        BaseRecordId<AuthUserRecordV1> userId,
        DateTimeOffset operationTime,
        CancellationToken cancellationToken)
    {
        BaseCollectionSession<AuthSessionRecordV1> session = context.Collection(AuthSessionRecordV1.Collection);
        BaseMutationRequestIdentity identity = SelectionIdentity(context, work);
        return session.Query().Where(AuthSessionRecordV1.Fields.TenantId, work.TenantId)
            .Where(AuthSessionRecordV1.Fields.UserId, userId)
            .Where(AuthSessionRecordV1.Fields.Revoked, false).ThenByRecordId().Take(200)
            .PatchSelectedAsync(session.GetMergePatchSelectionProfile(AuthSelectionProfiles.SessionsRevokeUser),
                Patch(AuthSessionRecordV1.Collection,
                    (AuthSessionRecordV1.Fields.Revoked.Id, "true"),
                    (AuthSessionRecordV1.Fields.RevokedAt.Id, Instant(operationTime)),
                    (AuthSessionRecordV1.Fields.RetentionEligibleAt.Id, Instant(work.CreatedAt.AddDays(30))),
                    (AuthSessionRecordV1.Fields.State.Id, "\"loggedOut\"")),
                RequiresFalse(AuthSessionRecordV1.Fields.Revoked.Id), identity,
                context.GuardSelectionMutation("cleanup-cohort", 1, identity), cancellationToken);
    }

    private static ValueTask<BaseResult<BaseSelectionMutationResult>> RevokeRefreshTokensAsync(
        BaseActivationContext context,
        AuthCleanupWorkReadV1.Row work,
        BaseRecordId<AuthUserRecordV1> userId,
        DateTimeOffset operationTime,
        CancellationToken cancellationToken)
    {
        BaseCollectionSession<AuthRefreshTokenRecordV1> session = context.Collection(AuthRefreshTokenRecordV1.Collection);
        BaseMutationRequestIdentity identity = SelectionIdentity(context, work);
        return session.Query().Where(AuthRefreshTokenRecordV1.Fields.TenantId, work.TenantId)
            .Where(AuthRefreshTokenRecordV1.Fields.UserId, userId)
            .Where(AuthRefreshTokenRecordV1.Fields.Revoked, false).ThenByRecordId().Take(200)
            .PatchSelectedAsync(session.GetMergePatchSelectionProfile(AuthSelectionProfiles.RefreshTokensRevokeUser),
                Patch(AuthRefreshTokenRecordV1.Collection,
                    (AuthRefreshTokenRecordV1.Fields.Revoked.Id, "true"),
                    (AuthRefreshTokenRecordV1.Fields.RevokedAt.Id, Instant(operationTime)),
                    (AuthRefreshTokenRecordV1.Fields.RetentionEligibleAt.Id, Instant(work.CreatedAt.AddDays(30)))),
                RequiresFalse(AuthRefreshTokenRecordV1.Fields.Revoked.Id), identity,
                context.GuardSelectionMutation("cleanup-cohort", 1, identity), cancellationToken);
    }

    private static BaseMutationRequestIdentity SelectionIdentity(
        BaseActivationContext context,
        AuthCleanupWorkReadV1.Row work)
    {
        BaseMutationRequestFingerprint fingerprint = Fingerprint(
            "hpd.auth.cleanup.cohort.v1", work.Id, work.Step.ToString(),
            work.ChunkOrdinal.ToString(System.Globalization.CultureInfo.InvariantCulture));
        return context.DeriveChildIdentity("cleanup-cohort", 1, fingerprint);
    }

    private static RecordPatchRequest Patch<TRecord>(
        BaseCollection<TRecord> collection,
        params (string FieldId, string CanonicalJson)[] values)
    {
        var fields = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach ((string fieldId, string canonicalJson) in values)
        {
            string wireName = collection.Definition.Fields!.Single(field => field.Id == fieldId).WireName;
            using JsonDocument document = JsonDocument.Parse(canonicalJson);
            fields.Add(wireName, document.RootElement.Clone());
        }
        return new RecordPatchRequest
        {
            Patch = new RecordPayload { Kind = RecordPayloadKind.FieldMap, Fields = fields },
            RemovedFieldIds = ImmutableArray<string>.Empty,
        };
    }

    private static BasePreviousStateRequirement RequiresFalse(string fieldId) => new()
    {
        Revision = new BaseRevisionRequirement { Kind = BaseRevisionRequirementKind.None },
        Fields =
        [
            new BasePreviousFieldRequirement
            {
                FieldId = fieldId,
                Kind = BasePreviousFieldRequirementKind.Equal,
                Value = new QueryValue { Kind = QueryValueKind.Boolean, Boolean = false },
            },
        ],
    };

    private static string Instant(DateTimeOffset value) =>
        "\"" + value.UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'",
            System.Globalization.CultureInfo.InvariantCulture) + "\"";

    private static BaseActivationHandlerResult<AuthCleanupResultV1> Completed(AuthCleanupWorkReadV1.Row work) => new BaseActivationSucceeded<AuthCleanupResultV1>()
    {
        Result = new AuthCleanupResultV1
        {
            Completed = true,
            State = work.State,
            Step = work.Step,
            ChunkOrdinal = work.ChunkOrdinal,
            SelectedCount = 0,
            RetentionEligibleAt = work.RetentionEligibleAt,
        },
    };

    private static BaseMutationRequestFingerprint Fingerprint(string domain, params string[] values)
    {
        using var stream = new MemoryStream();
        Write(stream, domain);
        foreach (string value in values)
            Write(stream, value);
        return BaseMutationRequestFingerprint.Create(
            SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
    }

    private static void Write(Stream stream, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        stream.Write(length);
        stream.Write(bytes);
    }
}
