using HPD.Base;

namespace HPD.Auth.Base;

internal sealed class AuthUserCleanupRetirementHandler
    : IBaseActivationHandler<AuthUserCleanupInitializeV1, AuthCleanupRetirementResultV1>
{
    public ValueTask<BaseActivationHandlerResult<AuthCleanupRetirementResultV1>> ExecuteAsync(
        BaseActivationContext context,
        AuthUserCleanupInitializeV1 input,
        CancellationToken cancellationToken) =>
        AuthCleanupRetirementActivationHandler.ExecuteUserAsync(context, input, cancellationToken);
}

internal sealed class AuthRoleCleanupRetirementHandler
    : IBaseActivationHandler<AuthRoleCleanupInitializeV1, AuthCleanupRetirementResultV1>
{
    public ValueTask<BaseActivationHandlerResult<AuthCleanupRetirementResultV1>> ExecuteAsync(
        BaseActivationContext context,
        AuthRoleCleanupInitializeV1 input,
        CancellationToken cancellationToken) =>
        AuthCleanupRetirementActivationHandler.ExecuteRoleAsync(context, input, cancellationToken);
}

internal static class AuthCleanupRetirementActivationHandler
{
    internal static ValueTask<BaseActivationHandlerResult<AuthCleanupRetirementResultV1>> ExecuteUserAsync(
        BaseActivationContext context,
        AuthUserCleanupInitializeV1 input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);
        BaseMutationRequestIdentity identity = context.CreateModuleMutationRequestIdentity(
            AuthUserCleanupRetireOperationV1.Identity, input,
            $"cleanup-retire:user:{input.CleanupWorkId}");
        BaseSemanticActivationKey<AuthUserCleanupSemanticDefinitionV1> key =
            context.CreateSemanticActivationKey(AuthCleanupSemanticActivations.User.KeyIdentity, input);
        return ExecuteUserCoreAsync(context, input, identity, key, cancellationToken);
    }

    internal static ValueTask<BaseActivationHandlerResult<AuthCleanupRetirementResultV1>> ExecuteRoleAsync(
        BaseActivationContext context,
        AuthRoleCleanupInitializeV1 input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);
        BaseMutationRequestIdentity identity = context.CreateModuleMutationRequestIdentity(
            AuthRoleCleanupRetireOperationV1.Identity, input,
            $"cleanup-retire:role:{input.CleanupWorkId}");
        BaseSemanticActivationKey<AuthRoleCleanupSemanticDefinitionV1> key =
            context.CreateSemanticActivationKey(AuthCleanupSemanticActivations.Role.KeyIdentity, input);
        return ExecuteRoleCoreAsync(context, input, identity, key, cancellationToken);
    }

    private static async ValueTask<BaseActivationHandlerResult<AuthCleanupRetirementResultV1>> ExecuteUserCoreAsync(
        BaseActivationContext context,
        AuthUserCleanupInitializeV1 request,
        BaseMutationRequestIdentity identity,
        BaseSemanticActivationKey<AuthUserCleanupSemanticDefinitionV1> key,
        CancellationToken cancellationToken)
    {
        BaseActivationHandlerResult<AuthCleanupRetirementResultV1> retirement = await ExecuteAsync(
            context, request, identity, key, AuthUserCleanupRetireOperationV1.Identity, cancellationToken)
            .ConfigureAwait(false);
        if (retirement is not BaseActivationSucceeded<AuthCleanupRetirementResultV1>)
            return retirement;
        return await FinalizeUserSubjectAsync(context, request, retirement, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async ValueTask<BaseActivationHandlerResult<AuthCleanupRetirementResultV1>> ExecuteRoleCoreAsync(
        BaseActivationContext context,
        AuthRoleCleanupInitializeV1 request,
        BaseMutationRequestIdentity identity,
        BaseSemanticActivationKey<AuthRoleCleanupSemanticDefinitionV1> key,
        CancellationToken cancellationToken)
    {
        BaseActivationHandlerResult<AuthCleanupRetirementResultV1> retirement = await ExecuteAsync(
            context, request, identity, key, AuthRoleCleanupRetireOperationV1.Identity, cancellationToken)
            .ConfigureAwait(false);
        if (retirement is not BaseActivationSucceeded<AuthCleanupRetirementResultV1>)
            return retirement;
        return await FinalizeRoleSubjectAsync(context, request, retirement, cancellationToken)
            .ConfigureAwait(false);
    }

    private static async ValueTask<BaseActivationHandlerResult<AuthCleanupRetirementResultV1>> FinalizeUserSubjectAsync(
        BaseActivationContext context,
        AuthUserCleanupInitializeV1 request,
        BaseActivationHandlerResult<AuthCleanupRetirementResultV1> retirement,
        CancellationToken cancellationToken)
    {
        BaseResult<BaseSubjectRetirementInspection> inspection = await InspectAsync(
            context, "hpd.auth.user-subject", request.Subject, cancellationToken).ConfigureAwait(false);
        if (inspection is BaseFailure<BaseSubjectRetirementInspection> inspectionFailure)
            return AuthActivationFailureMapper.Map<AuthCleanupRetirementResultV1>(inspectionFailure.Error);
        BaseSubjectRetirementInspection inspected = inspection.RequireValue();
        if (inspected.TerminalSummary is not null)
            return retirement;
        if (inspected.CurrentBarrier is { } barrier)
            return await PurgeAsync(context, request.Subject, request.TombstoneSequence,
                request.TombstoneRevision, request.CleanupWorkId, barrier, retirement, cancellationToken)
                .ConfigureAwait(false);

        BaseMutationRequestIdentity identity = FinalRetirementIdentity(
            context, "user", request.CleanupWorkId, request.Subject.SubjectId.Value,
            request.Incarnation, request.TombstoneSequence, null);
        BaseResult<BaseSubjectFinalRetirementResult<AuthUserSubject>> finalized = await context
            .GetExportedSubjectContract<AuthUserSubject>(AuthUserSubject.HPDBaseSubjectRegistration)
            .FinalizeRetirementAsync(new BaseSubjectFinalRetirementRequest<AuthUserSubject>
            {
                Subject = request.Subject,
                ExpectedTombstoneSequence = request.TombstoneSequence,
                ExpectedPrivateRevision = new RevisionToken(request.TombstoneRevision),
                Identity = identity,
            }, context.GuardSubjectFinalRetirement("cleanup-final-retirement", 2, identity), cancellationToken)
            .ConfigureAwait(false);
        return finalized is BaseFailure<BaseSubjectFinalRetirementResult<AuthUserSubject>> failure
            ? AuthActivationFailureMapper.Map<AuthCleanupRetirementResultV1>(failure.Error)
            : retirement;
    }

    private static async ValueTask<BaseActivationHandlerResult<AuthCleanupRetirementResultV1>> FinalizeRoleSubjectAsync(
        BaseActivationContext context,
        AuthRoleCleanupInitializeV1 request,
        BaseActivationHandlerResult<AuthCleanupRetirementResultV1> retirement,
        CancellationToken cancellationToken)
    {
        BaseResult<BaseSubjectRetirementInspection> inspection = await InspectAsync(
            context, "hpd.auth.role-subject", request.Subject, cancellationToken).ConfigureAwait(false);
        if (inspection is BaseFailure<BaseSubjectRetirementInspection> inspectionFailure)
            return AuthActivationFailureMapper.Map<AuthCleanupRetirementResultV1>(inspectionFailure.Error);
        BaseSubjectRetirementInspection inspected = inspection.RequireValue();
        if (inspected.TerminalSummary is not null)
            return retirement;
        if (inspected.CurrentBarrier is { } barrier)
            return await PurgeAsync(context, request.Subject, request.TombstoneSequence,
                request.TombstoneRevision, request.CleanupWorkId, barrier, retirement, cancellationToken)
                .ConfigureAwait(false);

        BaseMutationRequestIdentity identity = FinalRetirementIdentity(
            context, "role", request.CleanupWorkId, request.Subject.SubjectId.Value,
            request.Incarnation, request.TombstoneSequence, null);
        BaseResult<BaseSubjectFinalRetirementResult<AuthRoleSubject>> finalized = await context
            .GetExportedSubjectContract<AuthRoleSubject>(AuthRoleSubject.HPDBaseSubjectRegistration)
            .FinalizeRetirementAsync(new BaseSubjectFinalRetirementRequest<AuthRoleSubject>
            {
                Subject = request.Subject,
                ExpectedTombstoneSequence = request.TombstoneSequence,
                ExpectedPrivateRevision = new RevisionToken(request.TombstoneRevision),
                Identity = identity,
            }, context.GuardSubjectFinalRetirement("cleanup-final-retirement", 2, identity), cancellationToken)
            .ConfigureAwait(false);
        return finalized is BaseFailure<BaseSubjectFinalRetirementResult<AuthRoleSubject>> failure
            ? AuthActivationFailureMapper.Map<AuthCleanupRetirementResultV1>(failure.Error)
            : retirement;
    }

    private static ValueTask<BaseResult<BaseSubjectRetirementInspection>> InspectAsync<TSubject>(
        BaseActivationContext context,
        string contractId,
        BaseSubjectReference<TSubject> subject,
        CancellationToken cancellationToken) =>
        context.SubjectRetirements.InspectAsync(new BaseSubjectRetirementInspectionRequest
        {
            ContractId = contractId,
            ContractVersion = 1,
            SubjectId = subject.SubjectId,
            AuthorityEpoch = subject.AuthorityEpoch,
            Incarnation = subject.Incarnation,
            ScopeAuthority = new BaseSubjectScopeQueryAuthority
            {
                Mode = BaseSubjectScopeQueryMode.ExactScope,
                InstalledAuthorityDigest = new string('0', 64),
            },
            IncludeTerminalSummary = true,
            MaximumResultBytes = 65_536,
            DeadlineUtc = DateTimeOffset.FromUnixTimeMilliseconds(context.Claim.SliceStartedAt).AddSeconds(30),
        }, cancellationToken);

    private static async ValueTask<BaseActivationHandlerResult<AuthCleanupRetirementResultV1>> PurgeAsync<TSubject>(
        BaseActivationContext context,
        BaseSubjectReference<TSubject> subject,
        long tombstoneSequence,
        string tombstoneRevision,
        string cleanupWorkId,
        BaseSubjectRetirementBarrier barrier,
        BaseActivationHandlerResult<AuthCleanupRetirementResultV1> retirement,
        CancellationToken cancellationToken)
    {
        if (barrier.State is not (BaseSubjectRetirementBarrierState.Satisfied
                or BaseSubjectRetirementBarrierState.Overridden))
            return YieldForBarrier(context, cleanupWorkId, barrier);
        BaseMutationRequestIdentity identity = FinalRetirementIdentity(
            context, barrier.ContractId, cleanupWorkId, subject.SubjectId.Value,
            subject.Incarnation, tombstoneSequence, barrier);
        BaseResult<BaseSubjectFinalPurgeResult> purged = await context.SubjectRetirements.PurgeAsync(
            new BaseSubjectFinalPurgeRequest
            {
                ContractId = barrier.ContractId,
                ContractVersion = barrier.ContractVersion,
                SubjectId = subject.SubjectId,
                AuthorityEpoch = subject.AuthorityEpoch,
                Incarnation = subject.Incarnation,
                ExpectedTombstoneSequence = tombstoneSequence,
                ExpectedPrivateRevision = new RevisionToken(tombstoneRevision),
                ExpectedBarrierGeneration = barrier.Generation,
                ExpectedBarrierChecksum = barrier.BarrierChecksum,
                Identity = identity,
            }, context.GuardSubjectFinalPurge("cleanup-final-retirement", 2, identity), cancellationToken)
            .ConfigureAwait(false);
        return purged is BaseFailure<BaseSubjectFinalPurgeResult> failure
            ? AuthActivationFailureMapper.Map<AuthCleanupRetirementResultV1>(failure.Error)
            : retirement;
    }

    private static BaseActivationHandlerResult<AuthCleanupRetirementResultV1> YieldForBarrier(
        BaseActivationContext context,
        string cleanupWorkId,
        BaseSubjectRetirementBarrier barrier)
    {
        byte[] progress = System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
            string.Join('\n', "hpd.auth.cleanup.semantic-barrier-wait.v1", cleanupWorkId,
                barrier.State.ToString(), barrier.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture),
                barrier.BarrierChecksum)));
        return new BaseActivationYielded<AuthCleanupRetirementResultV1>
        {
            Yield = new BaseActivationYield
            {
                ResumeAt = DateTimeOffset.FromUnixTimeMilliseconds(context.Claim.SliceStartedAt).AddMinutes(1),
                ProgressFingerprint = BaseActivationProgressFingerprint.Create(progress),
            },
        };
    }

    private static BaseMutationRequestIdentity FinalRetirementIdentity(
        BaseActivationContext context,
        string subjectKind,
        string cleanupWorkId,
        string subjectId,
        BaseSubjectIncarnation incarnation,
        long tombstoneSequence,
        BaseSubjectRetirementBarrier? barrier)
    {
        BaseMutationRequestFingerprint fingerprint = AuthCleanupWorkIdentity.Fingerprint(
            $"hpd.auth.cleanup.final-retirement.{subjectKind}.v1", cleanupWorkId, subjectId,
            incarnation.ToBase64Url(),
            tombstoneSequence.ToString(System.Globalization.CultureInfo.InvariantCulture),
            barrier?.Generation.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "uncoordinated",
            barrier?.BarrierChecksum ?? "uncoordinated");
        return context.DeriveChildIdentity("cleanup-final-retirement", 2, fingerprint);
    }

    private static async ValueTask<BaseActivationHandlerResult<AuthCleanupRetirementResultV1>> ExecuteAsync<TRequest, TDefinition>(
        BaseActivationContext context,
        TRequest request,
        BaseMutationRequestIdentity identity,
        BaseSemanticActivationKey<TDefinition> key,
        BaseGeneratedModuleMutationIdentity<TRequest, AuthCleanupRetirementResultV1> operation,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        BaseModuleMutationExecutionOptions options = context.GuardModuleMutationAndRetireSemanticActivation(
            "cleanup-semantic-retire", 1, identity.Fingerprint, key);
        BaseResult<BaseModuleMutationExecutionResult<AuthCleanupRetirementResultV1>> result =
            await context.ExecuteModuleMutationAsync(
                operation, request, identity, options, cancellationToken).ConfigureAwait(false);
        if (result is BaseFailure<BaseModuleMutationExecutionResult<AuthCleanupRetirementResultV1>> indeterminate
            && string.Equals(indeterminate.Error.Code, "base.moduleMutation.commitIndeterminate",
                StringComparison.Ordinal))
        {
            result = await context.ResolveModuleMutationAsync(
                operation, identity, cancellationToken).ConfigureAwait(false);
        }
        if (result is BaseSuccess<BaseModuleMutationExecutionResult<AuthCleanupRetirementResultV1>> success)
            return new BaseActivationSucceeded<AuthCleanupRetirementResultV1>
            {
                Result = success.Value.Result,
            };

        BaseError error = ((BaseFailure<BaseModuleMutationExecutionResult<AuthCleanupRetirementResultV1>>)result).Error;
        return MapFailure(error);
    }

    internal static BaseActivationHandlerResult<AuthCleanupRetirementResultV1> MapFailure(BaseError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        if (string.Equals(error.Code, BaseSemanticActivationErrorCodes.ActivationNotTerminal,
                StringComparison.Ordinal))
        {
            return new BaseActivationFailed<AuthCleanupRetirementResultV1>
            {
                FailureCode = "auth.cleanup.semanticRetirementPending",
                Retryable = true,
            };
        }
        if (string.Equals(error.Code, "base.moduleMutation.requirementFailed", StringComparison.Ordinal))
            return AuthActivationFailureMapper.Domain<AuthCleanupRetirementResultV1>(
                "auth.cleanup.reconcileConflict");
        return AuthActivationFailureMapper.Map<AuthCleanupRetirementResultV1>(error);
    }
}
