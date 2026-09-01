using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using HPD.Base;

namespace HPD.Auth.Base;

internal sealed class AuthCleanupReconcileActivationHandler
    : IBaseActivationHandler<AuthCleanupReconcileInputV1, AuthCleanupReconcileResultV1>
{
    private const string CursorId = "hpd.auth.cleanup-reconcile.cursor.v1";
    private const int PageSize = 200;
    private const int MaximumPages = 4;
    private readonly TimeProvider _timeProvider;

    internal AuthCleanupReconcileActivationHandler(TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(timeProvider);
        _timeProvider = timeProvider;
    }

    public async ValueTask<BaseActivationHandlerResult<AuthCleanupReconcileResultV1>> ExecuteAsync(
        BaseActivationContext context,
        AuthCleanupReconcileInputV1 input,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(input);
        cancellationToken.ThrowIfCancellationRequested();
        if (input.ContractVersion != 1)
            return AuthActivationFailureMapper.Domain<AuthCleanupReconcileResultV1>(
                "auth.cleanup.reconcileConflict");

        BaseCollectionSession<AuthMaintenanceCursorRecordV1> cursors =
            context.Collection(AuthMaintenanceCursorRecordV1.Collection);
        BaseResult<BaseRecord<AuthMaintenanceCursorRecordV1>> cursorRead =
            await cursors.GetAsync(RecordId.Create(CursorId), cancellationToken).ConfigureAwait(false);
        CursorAuthority cursor;
        if (cursorRead is BaseSuccess<BaseRecord<AuthMaintenanceCursorRecordV1>> cursorSuccess)
        {
            BaseRecord<AuthMaintenanceCursorRecordV1> record = cursorSuccess.Value;
            if (record.Revision is null || !ValidCursor(record.Value))
                return AuthActivationFailureMapper.Domain<AuthCleanupReconcileResultV1>(
                    "auth.cleanup.reconcileConflict");
            cursor = new CursorAuthority(record.Revision.Value, record.Value.PassGeneration,
                record.Value.AfterTenantId, record.Value.AfterSubjectKind, record.Value.AfterSubjectId);
        }
        else if (cursorRead.Status == OperationStatus.NotFound)
        {
            cursor = new CursorAuthority(null, null, null, null, null);
        }
        else
        {
            return AuthActivationFailureMapper.Map<AuthCleanupReconcileResultV1>(
                ((BaseFailure<BaseRecord<AuthMaintenanceCursorRecordV1>>)cursorRead).Error);
        }

        int pages = 0, examined = 0, committed = 0, duplicates = 0, childOrdinal = 0;
        DateTimeOffset operationTime = DateTimeOffset.FromUnixTimeMilliseconds(context.Claim.SliceStartedAt);
        for (int pageIndex = 0; pageIndex < MaximumPages; pageIndex++)
        {
            ReadPageResult page = await ReadPageAsync(context, cursor, cancellationToken)
                .ConfigureAwait(false);
            if (page.Error is not null)
                return AuthActivationFailureMapper.Map<AuthCleanupReconcileResultV1>(page.Error);
            Candidate[] candidates = page.Candidates!;
            pages++;
            examined = checked(examined + candidates.Length);

            foreach (Candidate candidate in candidates)
            {
                BaseActivationHandlerResult<AuthCleanupReconcileResultV1>? renewalFailure =
                    await RenewWhenRequiredAsync(context, cancellationToken).ConfigureAwait(false);
                if (renewalFailure is not null)
                    return renewalFailure;
                childOrdinal++;
                BaseActivationHandlerResult<EnsureObservation> ensured = await EnsureAsync(
                    context, candidate, childOrdinal, cancellationToken).ConfigureAwait(false);
                if (ensured is BaseActivationFailed<EnsureObservation> failed)
                {
                    return new BaseActivationFailed<AuthCleanupReconcileResultV1>
                    {
                        FailureCode = failed.FailureCode,
                        Retryable = failed.Retryable,
                    };
                }
                EnsureObservation observation = ((BaseActivationSucceeded<EnsureObservation>)ensured).Result;
                if (observation.Materialized)
                    committed++;
                else
                    duplicates++;
            }

            Candidate? last = candidates.LastOrDefault();
            bool wrap = last is null;
            BaseBinary pageDigest = BaseBinary.From(PageDigest(candidates));
            BaseActivationHandlerResult<AuthCleanupReconcileResultV1>? cursorRenewalFailure =
                await RenewWhenRequiredAsync(context, cancellationToken).ConfigureAwait(false);
            if (cursorRenewalFailure is not null)
                return cursorRenewalFailure;
            var cursorRequest = new AuthCleanupReconcileCursorV1
            {
                CursorId = CursorId,
                ExpectedRevision = cursor.Revision,
                ExpectedPassGeneration = cursor.PassGeneration,
                ExpectedAfterTenantId = cursor.AfterTenantId,
                ExpectedAfterSubjectKind = cursor.AfterSubjectKind,
                ExpectedAfterSubjectId = cursor.AfterSubjectId,
                PageDigest = pageDigest,
                NextTenantId = last?.TenantId,
                NextSubjectKind = last?.Kind,
                NextSubjectId = last?.SubjectId,
                Wrap = wrap,
                OperationTime = operationTime,
            };
            BaseMutationRequestIdentity cursorIdentity = context.CreateModuleMutationRequestIdentity(
                AuthCleanupReconcileCursorOperationV1.Identity, cursorRequest,
                $"cleanup-reconcile-cursor:{cursor.PassGeneration?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? "missing"}:{Convert.ToHexStringLower(pageDigest.ToArray())}");
            BaseResult<BaseModuleMutationExecutionResult<AuthCleanupReconcileCursorResultV1>> moved =
                await context.ExecuteModuleMutationAsync(
                    AuthCleanupReconcileCursorOperationV1.Identity,
                    cursorRequest,
                    cursorIdentity,
                    context.GuardModuleMutation("cleanup-reconcile-cursor", 800 + pages,
                        cursorIdentity.Fingerprint),
                    cancellationToken).ConfigureAwait(false);
            if (moved is BaseFailure<BaseModuleMutationExecutionResult<AuthCleanupReconcileCursorResultV1>> moveFailure)
                return AuthActivationFailureMapper.Map<AuthCleanupReconcileResultV1>(moveFailure.Error);
            AuthCleanupReconcileCursorResultV1 next = moved.RequireValue().Result;
            cursor = new CursorAuthority(next.Revision, next.PassGeneration,
                next.AfterTenantId, next.AfterSubjectKind, next.AfterSubjectId);
            if (wrap)
                break;
        }

        return new BaseActivationSucceeded<AuthCleanupReconcileResultV1>
        {
            Result = new AuthCleanupReconcileResultV1
            {
                Pages = pages,
                ExaminedSubjects = examined,
                CommittedEnqueues = committed,
                DuplicateEnqueues = duplicates,
                CursorPass = cursor.PassGeneration ?? 1,
                CursorTenantId = cursor.AfterTenantId,
                CursorSubjectKind = cursor.AfterSubjectKind,
                CursorSubjectId = cursor.AfterSubjectId,
                CompletedAt = operationTime,
            },
        };
    }

    private async ValueTask<BaseActivationHandlerResult<AuthCleanupReconcileResultV1>?>
        RenewWhenRequiredAsync(BaseActivationContext context, CancellationToken cancellationToken)
    {
        long renewalBoundary = checked(_timeProvider.GetUtcNow().ToUnixTimeMilliseconds() + 10_000);
        if (context.Lease.LeaseExpiresAt > renewalBoundary)
            return null;
        OperationResult<BaseActivationLeaseObservation> renewed = await context
            .RenewAsync(cancellationToken).ConfigureAwait(false);
        return renewed.IsSuccess()
            ? null
            : AuthActivationFailureMapper.Map<AuthCleanupReconcileResultV1>(renewed.Error!);
    }

    private static async ValueTask<ReadPageResult> ReadPageAsync(
        BaseActivationContext context,
        CursorAuthority cursor,
        CancellationToken cancellationToken)
    {
        var usersRequest = new AuthTombstonedUsersForReconciliationReadV1
        {
            AfterTenantId = cursor.AfterTenantId,
            AfterSubjectKind = cursor.AfterSubjectKind,
            AfterSubjectId = cursor.AfterSubjectId,
        };
        var rolesRequest = new AuthTombstonedRolesForReconciliationReadV1
        {
            AfterTenantId = cursor.AfterTenantId,
            AfterSubjectKind = cursor.AfterSubjectKind,
            AfterSubjectId = cursor.AfterSubjectId,
        };
        BaseResult<BasePage<AuthTombstonedUsersForReconciliationReadV1.Row>> users =
            await context.Reads.ExecuteAsync(AuthTombstonedUsersForReconciliationReadV1.Handle,
                usersRequest, BaseReadPageRequest.Create(1, PageSize), cancellationToken).ConfigureAwait(false);
        if (users is BaseFailure<BasePage<AuthTombstonedUsersForReconciliationReadV1.Row>> userFailure)
            return new ReadPageResult(null, userFailure.Error);
        BaseResult<BasePage<AuthTombstonedRolesForReconciliationReadV1.Row>> roles =
            await context.Reads.ExecuteAsync(AuthTombstonedRolesForReconciliationReadV1.Handle,
                rolesRequest, BaseReadPageRequest.Create(1, PageSize), cancellationToken).ConfigureAwait(false);
        if (roles is BaseFailure<BasePage<AuthTombstonedRolesForReconciliationReadV1.Row>> roleFailure)
            return new ReadPageResult(null, roleFailure.Error);

        BaseResult<BasePage<AuthTombstonedUserReferencesForReconciliationReadV1.Row>> userReferences =
            await context.Reads.ExecuteAsync(
                AuthTombstonedUserReferencesForReconciliationReadV1.Handle,
                new AuthTombstonedUserReferencesForReconciliationReadV1
                {
                    AfterTenantId = cursor.AfterTenantId,
                    AfterSubjectKind = cursor.AfterSubjectKind,
                    AfterSubjectId = cursor.AfterSubjectId,
                }, BaseReadPageRequest.Create(1, PageSize), cancellationToken).ConfigureAwait(false);
        if (userReferences is BaseFailure<BasePage<AuthTombstonedUserReferencesForReconciliationReadV1.Row>> userReferenceFailure)
            return new ReadPageResult(null, userReferenceFailure.Error);
        BaseResult<BasePage<AuthTombstonedRoleReferencesForReconciliationReadV1.Row>> roleReferences =
            await context.Reads.ExecuteAsync(
                AuthTombstonedRoleReferencesForReconciliationReadV1.Handle,
                new AuthTombstonedRoleReferencesForReconciliationReadV1
                {
                    AfterTenantId = cursor.AfterTenantId,
                    AfterSubjectKind = cursor.AfterSubjectKind,
                    AfterSubjectId = cursor.AfterSubjectId,
                }, BaseReadPageRequest.Create(1, PageSize), cancellationToken).ConfigureAwait(false);
        if (roleReferences is BaseFailure<BasePage<AuthTombstonedRoleReferencesForReconciliationReadV1.Row>> roleReferenceFailure)
            return new ReadPageResult(null, roleReferenceFailure.Error);

        Dictionary<Guid, BaseSubjectReference<AuthUserSubject>> userReferencesById = [];
        foreach (AuthTombstonedUserReferencesForReconciliationReadV1.Row row in userReferences.RequireValue().Items)
        {
            if (!Guid.TryParseExact(row.Reference.SubjectId.Value, "D", out Guid id)
                || !userReferencesById.TryAdd(id, row.Reference))
                return ConflictPage();
        }
        Dictionary<Guid, BaseSubjectReference<AuthRoleSubject>> roleReferencesById = [];
        foreach (AuthTombstonedRoleReferencesForReconciliationReadV1.Row row in roleReferences.RequireValue().Items)
        {
            if (!Guid.TryParseExact(row.Reference.SubjectId.Value, "D", out Guid id)
                || !roleReferencesById.TryAdd(id, row.Reference))
                return ConflictPage();
        }
        if (userReferencesById.Count != users.RequireValue().Items.Length
            || roleReferencesById.Count != roles.RequireValue().Items.Length)
            return ConflictPage();

        var merged = new List<Candidate>(PageSize * 2);
        foreach (AuthTombstonedUsersForReconciliationReadV1.Row row in users.RequireValue().Items)
        {
            if (!userReferencesById.TryGetValue(row.SubjectId, out BaseSubjectReference<AuthUserSubject> reference))
                return ConflictPage();
            merged.Add(Candidate.User(row, reference));
        }
        foreach (AuthTombstonedRolesForReconciliationReadV1.Row row in roles.RequireValue().Items)
        {
            if (!roleReferencesById.TryGetValue(row.SubjectId, out BaseSubjectReference<AuthRoleSubject> reference))
                return ConflictPage();
            merged.Add(Candidate.Role(row, reference));
        }
        merged.Sort(CandidateComparer.Instance);
        Candidate[] page = merged.Take(PageSize).ToArray();
        Candidate? prior = null;
        foreach (Candidate candidate in page)
        {
            if (!candidate.Valid || (prior is not null && CandidateComparer.Instance.Compare(prior, candidate) >= 0)
                || !After(cursor, candidate))
            {
                return new ReadPageResult(null, new BaseError
                {
                    Code = "auth.cleanup.reconcileConflict",
                    Message = "Cleanup reconciliation requires operator review.",
                    Category = ErrorCategory.Conflict,
                });
            }
            prior = candidate;
        }
        return new ReadPageResult(page, null);
    }

    private static ReadPageResult ConflictPage() => new(null, new BaseError
    {
        Code = "auth.cleanup.reconcileConflict",
        Message = "Cleanup reconciliation requires operator review.",
        Category = ErrorCategory.Conflict,
    });

    private static bool After(CursorAuthority cursor, Candidate candidate) =>
        cursor.AfterTenantId is null || CandidateComparer.CompareKey(
            cursor.AfterTenantId.Value, cursor.AfterSubjectKind!.Value, cursor.AfterSubjectId!.Value,
            candidate.TenantId, candidate.Kind, candidate.SubjectId) < 0;

    private static async ValueTask<BaseActivationHandlerResult<EnsureObservation>> EnsureAsync(
        BaseActivationContext context,
        Candidate candidate,
        int ordinal,
        CancellationToken cancellationToken)
    {
        if (candidate.UserSubject is { } userSubject)
        {
            var request = new AuthUserCleanupInitializeV1
            {
                CleanupWorkId = candidate.CleanupWorkId,
                TenantId = candidate.TenantId,
                SubjectId = candidate.SubjectId,
                Subject = userSubject,
                Incarnation = userSubject.Incarnation,
                TombstoneSequence = candidate.TombstoneSequence,
                TombstoneRevision = candidate.PrivateRevision.Value,
                WorkflowVersion = 1,
                TombstonedAt = candidate.TombstonedAt,
                RetirementReceiptScope = "auth.cleanup.initialize",
                OperationTime = candidate.TombstonedAt,
            };
            BaseMutationRequestIdentity identity = context.CreateModuleMutationRequestIdentity(
                AuthUserCleanupInitializeOperationV1.Identity, request,
                $"cleanup-reconcile:user:{candidate.CleanupWorkId}");
            BaseSemanticActivationKey<AuthUserCleanupSemanticDefinitionV1> key =
                context.CreateSemanticActivationKey(AuthCleanupSemanticActivations.User.KeyIdentity, request);
            BaseResult<BaseModuleMutationExecutionResult<AuthCleanupInitializeResultV1>> result =
                await context.ExecuteModuleMutationAsync(
                    AuthUserCleanupInitializeOperationV1.Identity, request, identity,
                    context.GuardModuleMutationAndEnsureActivation(
                        "cleanup-reconcile-subject", ordinal, identity.Fingerprint,
                        AuthCleanupActivationDeclarations.User.Identity,
                        new AuthUserCleanupInputV1
                        {
                            TenantId = request.TenantId,
                            SubjectContractId = AuthUserSubject.HPDBaseSubjectRegistration.Id,
                            SubjectContractVersion = AuthUserSubject.HPDBaseSubjectRegistration.Version,
                            SubjectContractChecksum = AuthUserSubject.HPDBaseSubjectRegistration.ContractChecksum,
                            SubjectId = request.SubjectId,
                            Subject = request.Subject,
                            Incarnation = request.Incarnation,
                            TombstoneSequence = request.TombstoneSequence,
                            TombstoneRevision = request.TombstoneRevision,
                            WorkflowVersion = request.WorkflowVersion,
                        },
                        request.TombstonedAt, key),
                    cancellationToken).ConfigureAwait(false);
            return EnsureResult(result);
        }

        BaseSubjectReference<AuthRoleSubject> roleSubject = candidate.RoleSubject!.Value;
        var roleRequest = new AuthRoleCleanupInitializeV1
        {
            CleanupWorkId = candidate.CleanupWorkId,
            TenantId = candidate.TenantId,
            SubjectId = candidate.SubjectId,
            Subject = roleSubject,
            Incarnation = roleSubject.Incarnation,
            TombstoneSequence = candidate.TombstoneSequence,
            TombstoneRevision = candidate.PrivateRevision.Value,
            WorkflowVersion = 1,
            TombstonedAt = candidate.TombstonedAt,
            RetirementReceiptScope = "auth.cleanup.initialize",
            OperationTime = candidate.TombstonedAt,
        };
        BaseMutationRequestIdentity roleIdentity = context.CreateModuleMutationRequestIdentity(
            AuthRoleCleanupInitializeOperationV1.Identity, roleRequest,
            $"cleanup-reconcile:role:{candidate.CleanupWorkId}");
        BaseSemanticActivationKey<AuthRoleCleanupSemanticDefinitionV1> roleKey =
            context.CreateSemanticActivationKey(AuthCleanupSemanticActivations.Role.KeyIdentity, roleRequest);
        BaseResult<BaseModuleMutationExecutionResult<AuthCleanupInitializeResultV1>> roleResult =
            await context.ExecuteModuleMutationAsync(
                AuthRoleCleanupInitializeOperationV1.Identity, roleRequest, roleIdentity,
                context.GuardModuleMutationAndEnsureActivation(
                    "cleanup-reconcile-subject", ordinal, roleIdentity.Fingerprint,
                    AuthCleanupActivationDeclarations.Role.Identity,
                    new AuthRoleCleanupInputV1
                    {
                        TenantId = roleRequest.TenantId,
                        SubjectContractId = AuthRoleSubject.HPDBaseSubjectRegistration.Id,
                        SubjectContractVersion = AuthRoleSubject.HPDBaseSubjectRegistration.Version,
                        SubjectContractChecksum = AuthRoleSubject.HPDBaseSubjectRegistration.ContractChecksum,
                        SubjectId = roleRequest.SubjectId,
                        Subject = roleRequest.Subject,
                        Incarnation = roleRequest.Incarnation,
                        TombstoneSequence = roleRequest.TombstoneSequence,
                        TombstoneRevision = roleRequest.TombstoneRevision,
                        WorkflowVersion = roleRequest.WorkflowVersion,
                    },
                    roleRequest.TombstonedAt, roleKey),
                cancellationToken).ConfigureAwait(false);
        return EnsureResult(roleResult);
    }

    private static BaseActivationHandlerResult<EnsureObservation> EnsureResult(
        BaseResult<BaseModuleMutationExecutionResult<AuthCleanupInitializeResultV1>> result)
    {
        if (result is BaseSuccess<BaseModuleMutationExecutionResult<AuthCleanupInitializeResultV1>> success)
            return new BaseActivationSucceeded<EnsureObservation>
            {
                Result = new EnsureObservation(success.Value.Result.SemanticActivationWasMaterialized),
            };
        BaseActivationHandlerResult<EnsureObservation> mapped =
            AuthActivationFailureMapper.Map<EnsureObservation>(
                ((BaseFailure<BaseModuleMutationExecutionResult<AuthCleanupInitializeResultV1>>)result).Error);
        return mapped;
    }

    private static bool ValidCursor(AuthMaintenanceCursorRecordV1 value)
    {
        bool any = value.AfterTenantId is not null || value.AfterSubjectKind is not null
            || value.AfterSubjectId is not null;
        bool all = value.AfterTenantId is not null && value.AfterSubjectKind is not null
            && value.AfterSubjectId is not null;
        return value.Id == CursorId && value.PassGeneration > 0 && (!any || all);
    }

    private static byte[] PageDigest(IEnumerable<Candidate> candidates)
    {
        using IncrementalHash hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Append(hash, "hpd.auth.cleanup-reconcile.page.v1");
        Span<byte> sequence = stackalloc byte[8];
        foreach (Candidate candidate in candidates)
        {
            Append(hash, candidate.TenantId.ToString("D"));
            hash.AppendData([(byte)(candidate.Kind == AuthCleanupSubjectKindV1.user ? 0 : 1)]);
            Append(hash, candidate.SubjectId.ToString("D"));
            Append(hash, candidate.PrivateRevision.Value);
            BinaryPrimitives.WriteInt64BigEndian(sequence, candidate.TombstoneSequence);
            hash.AppendData(sequence);
            Append(hash, candidate.TombstonedAt.UtcTicks.ToString(
                System.Globalization.CultureInfo.InvariantCulture));
            hash.AppendData(candidate.Incarnation!.Value.ToArray());
        }
        return hash.GetHashAndReset();
    }

    private static void Append(IncrementalHash hash, string value)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(value);
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(length, bytes.Length);
        hash.AppendData(length);
        hash.AppendData(bytes);
    }

    private sealed record CursorAuthority(
        RevisionToken? Revision,
        long? PassGeneration,
        Guid? AfterTenantId,
        AuthCleanupSubjectKindV1? AfterSubjectKind,
        Guid? AfterSubjectId);

    private sealed record EnsureObservation(bool Materialized);

    private sealed record ReadPageResult(Candidate[]? Candidates, BaseError? Error);

    private sealed record Candidate
    {
        internal required Guid TenantId { get; init; }
        internal required AuthCleanupSubjectKindV1 Kind { get; init; }
        internal required Guid SubjectId { get; init; }
        internal required RevisionToken PrivateRevision { get; init; }
        internal BaseSubjectReference<AuthUserSubject>? UserSubject { get; init; }
        internal BaseSubjectReference<AuthRoleSubject>? RoleSubject { get; init; }
        internal BaseSubjectIncarnation? Incarnation { get; init; }
        internal required long TombstoneSequence { get; init; }
        internal required DateTimeOffset TombstonedAt { get; init; }
        internal string CleanupWorkId => AuthCleanupWorkIdentity.Create(
            TenantId, Kind == AuthCleanupSubjectKindV1.user ? "user" : "role", SubjectId,
            Kind == AuthCleanupSubjectKindV1.user
                ? AuthUserSubject.HPDBaseSubjectRegistration
                : AuthRoleSubject.HPDBaseSubjectRegistration,
            Incarnation!.Value, TombstoneSequence);
        internal bool Valid => TombstoneSequence > 0 && TombstonedAt != default
            && TombstonedAt.Offset == TimeSpan.Zero
            && Incarnation is { } incarnation && incarnation.ToArray().Length == 24
            && ((Kind == AuthCleanupSubjectKindV1.user && UserSubject is not null && RoleSubject is null)
                || (Kind == AuthCleanupSubjectKindV1.role && RoleSubject is not null && UserSubject is null));

        internal static Candidate User(
            AuthTombstonedUsersForReconciliationReadV1.Row row,
            BaseSubjectReference<AuthUserSubject> reference) => new()
        {
            TenantId = row.TenantId,
            Kind = AuthCleanupSubjectKindV1.user,
            SubjectId = row.SubjectId,
            PrivateRevision = row.PrivateRevision,
            UserSubject = reference,
            Incarnation = reference.Incarnation,
            TombstoneSequence = row.TombstoneSequence,
            TombstonedAt = row.TombstonedAt ?? default,
        };

        internal static Candidate Role(
            AuthTombstonedRolesForReconciliationReadV1.Row row,
            BaseSubjectReference<AuthRoleSubject> reference) => new()
        {
            TenantId = row.TenantId,
            Kind = AuthCleanupSubjectKindV1.role,
            SubjectId = row.SubjectId,
            PrivateRevision = row.PrivateRevision,
            RoleSubject = reference,
            Incarnation = reference.Incarnation,
            TombstoneSequence = row.TombstoneSequence,
            TombstonedAt = row.TombstonedAt ?? default,
        };
    }

    private sealed class CandidateComparer : IComparer<Candidate>
    {
        internal static CandidateComparer Instance { get; } = new();
        public int Compare(Candidate? left, Candidate? right)
        {
            if (ReferenceEquals(left, right)) return 0;
            if (left is null) return -1;
            if (right is null) return 1;
            return CompareKey(left.TenantId, left.Kind, left.SubjectId,
                right.TenantId, right.Kind, right.SubjectId);
        }

        internal static int CompareKey(
            Guid leftTenant, AuthCleanupSubjectKindV1 leftKind, Guid leftSubject,
            Guid rightTenant, AuthCleanupSubjectKindV1 rightKind, Guid rightSubject)
        {
            int tenant = string.CompareOrdinal(leftTenant.ToString("D"), rightTenant.ToString("D"));
            if (tenant != 0) return tenant;
            int kind = leftKind.CompareTo(rightKind);
            return kind != 0 ? kind : string.CompareOrdinal(
                leftSubject.ToString("D"), rightSubject.ToString("D"));
        }
    }
}
