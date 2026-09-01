using System.Runtime.CompilerServices;
using System.Security.Claims;
using HPD.Auth.Base;
using HPD.Auth.Core.Entities;
using HPD.Auth.Infrastructure.Base;
using HPD.Base;
using Microsoft.AspNetCore.Identity;

namespace HPD.Auth.Infrastructure.Stores;

/// <summary>
/// Persists detached ASP.NET Core Identity roles through the installed HPD Auth Base graph.
/// </summary>
internal sealed class AuthBaseRoleStore(
    AuthBaseRuntime runtime,
    IdentityErrorDescriber errors) :
    IRoleStore<ApplicationRole>,
    IRoleClaimStore<ApplicationRole>
{
    private readonly ConditionalWeakTable<ApplicationRole, AuthRoleAuthorityLease> _authority = new();
    private bool _disposed;

    /// <inheritdoc />
    public async Task<IdentityResult> CreateAsync(ApplicationRole role, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(role);
        cancellationToken.ThrowIfCancellationRequested();
        if (role.Id == Guid.Empty)
            role.Id = Guid.NewGuid();
        role.InstanceId = runtime.TenantId;
        role.ConcurrencyStamp ??= Guid.NewGuid().ToString("N");
        DateTimeOffset now = runtime.GetUtcNow();
        var request = new AuthRoleCreateV1
        {
            TenantId = runtime.TenantId,
            RoleId = role.Id,
            Name = role.Name,
            NormalizedName = role.NormalizedName,
            ConcurrencyStamp = role.ConcurrencyStamp,
            Description = role.Description,
            OperationTime = now,
        };
        BaseInstalledModuleMutationHandle<AuthRoleCreateV1, AuthRoleCreateResultV1> operation = runtime
            .OpenServiceSession().ModuleMutations.Get(AuthCreateRoleOperationV1.Identity);
        BaseMutationRequestIdentity identity = operation.CreateRequestIdentity(
            request, $"role:{role.Id:D}:create");
        BaseResult<BaseModuleMutationExecutionResult<AuthRoleCreateResultV1>> result = await operation
            .ExecuteAsync(request, identity, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (result is BaseFailure<BaseModuleMutationExecutionResult<AuthRoleCreateResultV1>> failure)
            return AuthBaseIdentityErrorMapper.Role(failure, errors, role.Name);
        role.Created = now.UtcDateTime;
        if (!await RefreshAuthorityAsync(role, result.RequireValue().Result.Revision, cancellationToken).ConfigureAwait(false))
            throw new AuthBasePersistenceException("auth.persistence.authorityUnavailable");
        return IdentityResult.Success;
    }

    /// <inheritdoc />
    public async Task<IdentityResult> UpdateAsync(ApplicationRole role, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(role);
        cancellationToken.ThrowIfCancellationRequested();
        AuthRoleAuthorityLease? authority = await ResolveAuthorityAsync(role, cancellationToken).ConfigureAwait(false);
        if (authority is null)
            return IdentityResult.Failed(errors.ConcurrencyFailure());
        string nextConcurrencyStamp = Guid.NewGuid().ToString("N");
        DateTimeOffset now = runtime.GetUtcNow();
        var request = new AuthRoleRenameV1
        {
            TenantId = runtime.TenantId,
            RoleId = role.Id,
            ExpectedRevision = authority.Revision,
            Name = role.Name,
            NormalizedName = role.NormalizedName,
            ConcurrencyStamp = nextConcurrencyStamp,
            OperationTime = now,
        };
        BaseInstalledModuleMutationHandle<AuthRoleRenameV1, AuthRoleMutationResultV1> operation = runtime
            .OpenServiceSession().ModuleMutations.Get(AuthRenameRoleOperationV1.Identity);
        BaseMutationRequestIdentity identity = operation.CreateRequestIdentity(
            request, $"role:{role.Id:D}:revision:{authority.Revision.Value}:rename");
        BaseResult<BaseModuleMutationExecutionResult<AuthRoleMutationResultV1>> result = await operation
            .ExecuteAsync(request, identity, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (result is BaseFailure<BaseModuleMutationExecutionResult<AuthRoleMutationResultV1>> failure)
            return AuthBaseIdentityErrorMapper.Role(failure, errors, role.Name);
        role.ConcurrencyStamp = nextConcurrencyStamp;
        if (!await RefreshAuthorityAsync(role, result.RequireValue().Result.Revision, cancellationToken).ConfigureAwait(false))
            throw new AuthBasePersistenceException("auth.persistence.authorityUnavailable");
        return IdentityResult.Success;
    }

    /// <inheritdoc />
    public async Task<IdentityResult> DeleteAsync(ApplicationRole role, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(role);
        cancellationToken.ThrowIfCancellationRequested();
        AuthRoleAuthorityLease? authority = await ResolveAuthorityAsync(role, cancellationToken).ConfigureAwait(false);
        if (authority is null)
            return IdentityResult.Failed(errors.ConcurrencyFailure());

        BaseSession session = runtime.OpenServiceSession();
        BaseResult<AuthRoleSubjectAcquisitionReadV1.Row?> acquired = await session.Reads.FirstAsync(
            AuthRoleSubjectAcquisitionReadV1.Handle,
            new AuthRoleSubjectAcquisitionReadV1
            {
                RoleId = BaseRecordId<AuthRoleRecordV1>.Create(role.Id.ToString("D")),
            }, cancellationToken).ConfigureAwait(false);
        if (acquired is BaseFailure<AuthRoleSubjectAcquisitionReadV1.Row?> acquisitionFailure)
            return AuthBaseIdentityErrorMapper.Role(acquisitionFailure, errors, role.Name);
        if (acquired.RequireValue() is not { } acquiredRow)
            return IdentityResult.Failed(errors.ConcurrencyFailure());

        BaseSubjectReference<AuthRoleSubject> subject = acquiredRow.Reference;
        BaseExportedSubjectContract<AuthRoleSubject> contract = AuthSubjects.Roles(session);
        BaseMutationRequestIdentity tombstoneIdentity = AuthBaseRuntime.MutationIdentity(
            "hpd.auth.role-subject.tombstone.v1", runtime.TenantId, role.Id.ToString("D"),
            authority.Revision.Value, subject.Incarnation.ToBase64Url());
        BaseResult<BaseSubjectTombstoneResult<AuthRoleSubject>> tombstoned = await contract.TombstoneAsync(new()
        {
            Subject = subject,
            ExpectedPrivateRevision = authority.Revision,
            Identity = tombstoneIdentity,
        }, cancellationToken).ConfigureAwait(false);
        if (tombstoned is BaseFailure<BaseSubjectTombstoneResult<AuthRoleSubject>> tombstoneFailure)
            return AuthBaseIdentityErrorMapper.Role(tombstoneFailure, errors, role.Name);

        BaseSubjectTombstoneResult<AuthRoleSubject> tombstone = tombstoned.RequireValue();
        string cleanupWorkId = AuthBaseDeterministicId.CreateCleanupWork(
            runtime.TenantId, "role", role.Id, contract, subject.Incarnation,
            tombstone.Fact.Fact.SubjectSequence);
        var initialize = new AuthRoleCleanupInitializeV1
        {
            CleanupWorkId = cleanupWorkId,
            TenantId = runtime.TenantId,
            SubjectId = role.Id,
            Subject = subject,
            Incarnation = subject.Incarnation,
            TombstoneSequence = tombstone.Fact.Fact.SubjectSequence,
            TombstoneRevision = tombstone.PrivateRevision.Value,
            WorkflowVersion = 1,
            TombstonedAt = tombstone.TombstonedAt,
            RetirementReceiptScope = "auth.cleanup.initialize",
            OperationTime = tombstone.TombstonedAt,
        };
        BaseInstalledActivationHandle<AuthRoleCleanupInitializeV1, AuthCleanupInitializeResultV1> bootstrap =
            runtime.OpenLifecycleDispatcherSession().Activations.Get(
                AuthLifecycleActivationDeclarations.BootstrapRole.Identity);
        BaseMutationRequestIdentity bootstrapIdentity = AuthBaseRuntime.MutationIdentity(
            "hpd.auth.cleanup.bootstrap.role.v1", runtime.TenantId, cleanupWorkId,
            tombstone.PrivateRevision.Value,
            tombstone.Fact.Fact.SubjectSequence.ToString(System.Globalization.CultureInfo.InvariantCulture));
        OperationResult<BaseActivationEnqueueResult> enqueued = await bootstrap
            .EnqueueAsync(initialize, bootstrapIdentity, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!enqueued.IsSuccess())
            return IdentityResult.Failed(new IdentityError
            {
                Code = "auth.persistence.unavailable",
                Description = "The durable role cleanup workflow could not be scheduled.",
            });

        authority.Consumed = true;
        return IdentityResult.Success;
    }

    /// <inheritdoc />
    public async Task<ApplicationRole?> FindByIdAsync(string roleId, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (!Guid.TryParseExact(roleId, "D", out Guid id))
            return null;
        BaseResult<BaseRegisteredReadFirstResult<AuthRoleByIdReadV1.Row>> result = await runtime.OpenServiceSession().Reads.FirstWithAuthorityAsync(
            AuthRoleByIdReadV1.Handle,
            new AuthRoleByIdReadV1 { TenantId = runtime.TenantId, RoleId = id },
            cancellationToken).ConfigureAwait(false);
        return MapRead(result);
    }

    /// <inheritdoc />
    public async Task<ApplicationRole?> FindByNameAsync(string normalizedRoleName, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(normalizedRoleName))
            return null;
        BaseResult<BaseRegisteredReadFirstResult<AuthRoleByNormalizedNameReadV1.Row>> result = await runtime.OpenServiceSession().Reads.FirstWithAuthorityAsync(
            AuthRoleByNormalizedNameReadV1.Handle,
            new AuthRoleByNormalizedNameReadV1
            {
                TenantId = runtime.TenantId,
                NormalizedName = normalizedRoleName,
            }, cancellationToken).ConfigureAwait(false);
        return MapRead(result);
    }

    /// <inheritdoc />
    public Task<string> GetRoleIdAsync(ApplicationRole role, CancellationToken cancellationToken)
    { ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(role); cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(role.Id.ToString("D")); }

    /// <inheritdoc />
    public Task<string?> GetRoleNameAsync(ApplicationRole role, CancellationToken cancellationToken)
    { ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(role); cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(role.Name); }

    /// <inheritdoc />
    public Task SetRoleNameAsync(ApplicationRole role, string? roleName, CancellationToken cancellationToken)
    { ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(role); cancellationToken.ThrowIfCancellationRequested(); role.Name = roleName; return Task.CompletedTask; }

    /// <inheritdoc />
    public Task<string?> GetNormalizedRoleNameAsync(ApplicationRole role, CancellationToken cancellationToken)
    { ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(role); cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(role.NormalizedName); }

    /// <inheritdoc />
    public Task SetNormalizedRoleNameAsync(ApplicationRole role, string? normalizedName, CancellationToken cancellationToken)
    { ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(role); cancellationToken.ThrowIfCancellationRequested(); role.NormalizedName = normalizedName; return Task.CompletedTask; }

    /// <inheritdoc />
    public async Task<IList<Claim>> GetClaimsAsync(ApplicationRole role, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(role); cancellationToken.ThrowIfCancellationRequested();
        RequireAttachedAuthority(role);
        BaseResult<AuthRoleClaimsReadV1.Row[]> result = await runtime.OpenServiceSession().Reads.ToArrayAsync(
            AuthRoleClaimsReadV1.Handle,
            new AuthRoleClaimsReadV1
            {
                TenantId = runtime.TenantId,
                RoleId = BaseRecordId<AuthRoleRecordV1>.Create(role.Id.ToString("D")),
            }, cancellationToken).ConfigureAwait(false);
        if (result is BaseFailure<AuthRoleClaimsReadV1.Row[]> failure)
            throw new AuthBasePersistenceException(AuthBaseIdentityErrorMapper.SafeReadCode(failure.Error));
        return result.RequireValue().Select(static row => new Claim(
            row.ClaimType ?? string.Empty,
            row.ClaimValue ?? string.Empty,
            row.ValueType,
            row.Issuer,
            row.OriginalIssuer)).ToList();
    }

    /// <inheritdoc />
    public async Task AddClaimAsync(ApplicationRole role, Claim claim, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(role); ArgumentNullException.ThrowIfNull(claim); cancellationToken.ThrowIfCancellationRequested();
        RequireAttachedAuthority(role);
        Guid claimId = Guid.NewGuid();
        DateTimeOffset now = runtime.GetUtcNow();
        var record = new AuthRoleClaimRecordV1
        {
            Id = claimId,
            TenantId = runtime.TenantId,
            RoleId = BaseRecordId<AuthRoleRecordV1>.Create(role.Id.ToString("D")),
            ClaimType = claim.Type,
            ClaimValue = claim.Value,
            Issuer = claim.Issuer,
            OriginalIssuer = claim.OriginalIssuer,
            ValueType = claim.ValueType,
            CreatedAt = now,
        };
        BaseMutationRequestIdentity identity = AuthBaseRuntime.MutationIdentity(
            "hpd.auth.role-claim.add.v1", runtime.TenantId, claimId.ToString("D"), role.Id.ToString("D"),
            claim.Type, claim.Value, claim.ValueType, claim.Issuer, claim.OriginalIssuer, now.ToString("O"));
        BaseBatchBuilder batch = runtime.OpenServiceSession().Atomic(identity);
        batch.Create(AuthRoleClaimRecordV1.Collection, RecordId.Create(claimId.ToString("D")), record);
        BaseResult<BaseBatchResult> result = await batch.CommitAsync(cancellationToken).ConfigureAwait(false);
        if (result is BaseFailure<BaseBatchResult> failure)
            throw new AuthBasePersistenceException(AuthBaseIdentityErrorMapper.SafeWriteCode(failure.Error));
        result.RequireValue().RequireCommitted();
    }

    /// <inheritdoc />
    public async Task RemoveClaimAsync(ApplicationRole role, Claim claim, CancellationToken cancellationToken = default)
    {
        ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(role); ArgumentNullException.ThrowIfNull(claim); cancellationToken.ThrowIfCancellationRequested();
        RequireAttachedAuthority(role);
        BaseResult<AuthRoleClaimsReadV1.Row[]> read = await runtime.OpenServiceSession().Reads.ToArrayAsync(
            AuthRoleClaimsReadV1.Handle,
            new AuthRoleClaimsReadV1
            {
                TenantId = runtime.TenantId,
                RoleId = BaseRecordId<AuthRoleRecordV1>.Create(role.Id.ToString("D")),
            }, cancellationToken).ConfigureAwait(false);
        if (read is BaseFailure<AuthRoleClaimsReadV1.Row[]> readFailure)
            throw new AuthBasePersistenceException(AuthBaseIdentityErrorMapper.SafeReadCode(readFailure.Error));
        AuthRoleClaimsReadV1.Row? match = read.RequireValue().FirstOrDefault(row =>
            string.Equals(row.ClaimType, claim.Type, StringComparison.Ordinal)
            && string.Equals(row.ClaimValue, claim.Value, StringComparison.Ordinal));
        if (match is null)
            return;
        BaseMutationRequestIdentity identity = AuthBaseRuntime.MutationIdentity(
            "hpd.auth.role-claim.remove.v1", runtime.TenantId, match.Id.ToString("D"),
            role.Id.ToString("D"), match.Revision.Value);
        BaseBatchBuilder batch = runtime.OpenServiceSession().Atomic(identity);
        batch.Delete(AuthRoleClaimRecordV1.Collection, RecordId.Create(match.Id.ToString("D")), match.Revision);
        BaseResult<BaseBatchResult> result = await batch.CommitAsync(cancellationToken).ConfigureAwait(false);
        if (result is BaseFailure<BaseBatchResult> failure)
            throw new AuthBasePersistenceException(AuthBaseIdentityErrorMapper.SafeWriteCode(failure.Error));
        result.RequireValue().RequireCommitted();
    }

    /// <inheritdoc />
    public void Dispose() => _disposed = true;

    private ApplicationRole? MapRead<T>(BaseResult<BaseRegisteredReadFirstResult<T>> result)
        where T : class, IAuthRoleIdentityProjectionV1
    {
        if (result is BaseFailure<BaseRegisteredReadFirstResult<T>> failure)
            throw new AuthBasePersistenceException(AuthBaseIdentityErrorMapper.SafeReadCode(failure.Error));
        BaseRegisteredReadFirstResult<T> read = result.RequireValue();
        T? row = read.Item;
        if (row is null || row.IsDeleted)
            return null;
        var role = new ApplicationRole
        {
            Id = row.Id,
            InstanceId = row.TenantId,
            Name = row.Name,
            NormalizedName = row.NormalizedName,
            ConcurrencyStamp = row.ConcurrencyStamp,
            Description = row.Description,
            Created = row.CreatedAt.UtcDateTime,
        };
        Remember(role, row.Revision, read.Authority);
        return role;
    }

    private async Task<AuthRoleAuthorityLease?> ResolveAuthorityAsync(
        ApplicationRole role,
        CancellationToken cancellationToken)
    {
        if (role.InstanceId != runtime.TenantId || role.Id == Guid.Empty)
            return null;
        if (_authority.TryGetValue(role, out AuthRoleAuthorityLease? attached))
            return !attached.Consumed
                && attached.TenantId == runtime.TenantId
                && attached.RecordId == role.Id
                && string.Equals(attached.Snapshot.ConcurrencyStamp, role.ConcurrencyStamp, StringComparison.Ordinal)
                    ? attached
                    : null;

        ApplicationRole? persisted = await FindByIdAsync(role.Id.ToString("D"), cancellationToken).ConfigureAwait(false);
        if (persisted is null
            || !_authority.TryGetValue(persisted, out AuthRoleAuthorityLease? fresh)
            || !string.Equals(persisted.ConcurrencyStamp, role.ConcurrencyStamp, StringComparison.Ordinal))
            return null;
        Remember(role, fresh.Revision, fresh.Authority);
        return _authority.TryGetValue(role, out AuthRoleAuthorityLease? resolved) ? resolved : null;
    }

    private async Task<bool> RefreshAuthorityAsync(
        ApplicationRole role,
        RevisionToken expectedRevision,
        CancellationToken cancellationToken)
    {
        ApplicationRole? committed = await FindByIdAsync(role.Id.ToString("D"), cancellationToken).ConfigureAwait(false);
        if (committed is null
            || !_authority.TryGetValue(committed, out AuthRoleAuthorityLease? authority)
            || authority.Revision != expectedRevision)
            return false;
        Remember(role, authority.Revision, authority.Authority);
        return true;
    }

    private void Remember(
        ApplicationRole role,
        RevisionToken revision,
        BaseRegisteredReadSnapshotAuthority authority)
    {
        _authority.Remove(role);
        _authority.Add(role, new AuthRoleAuthorityLease
        {
            TenantId = runtime.TenantId,
            RecordId = role.Id,
            Revision = revision,
            Authority = authority,
            Snapshot = AuthRoleOrdinarySnapshot.Capture(role),
        });
    }

    private AuthRoleAuthorityLease RequireAttachedAuthority(ApplicationRole role)
    {
        if (!_authority.TryGetValue(role, out AuthRoleAuthorityLease? authority)
            || authority.Consumed
            || authority.TenantId != runtime.TenantId
            || authority.RecordId != role.Id)
            throw new AuthBasePersistenceException("auth.persistence.invalidAuthority");
        return authority;
    }

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

}
