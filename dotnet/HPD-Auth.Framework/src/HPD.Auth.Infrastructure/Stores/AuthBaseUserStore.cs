using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using HPD.Auth.Base;
using HPD.Auth.Core.Entities;
using HPD.Auth.Infrastructure.Base;
using HPD.Auth.Infrastructure.Serialization;
using HPD.Base;
using Microsoft.AspNetCore.Identity;

namespace HPD.Auth.Infrastructure.Stores;

/// <summary>
/// Persists detached ASP.NET Core Identity users through the installed HPD Auth Base graph.
/// </summary>
internal sealed partial class AuthBaseUserStore(
    AuthBaseRuntime runtime,
    IAuthRecoveryCodeDigestKeyRing recoveryCodeKeys,
    IdentityErrorDescriber errors) :
    IUserStore<ApplicationUser>,
    IUserEmailStore<ApplicationUser>,
    IUserPhoneNumberStore<ApplicationUser>,
    IUserPasswordStore<ApplicationUser>,
    IUserSecurityStampStore<ApplicationUser>,
    IUserLockoutStore<ApplicationUser>,
    IUserTwoFactorStore<ApplicationUser>,
    IUserAuthenticatorKeyStore<ApplicationUser>,
    IUserTwoFactorRecoveryCodeStore<ApplicationUser>,
    IUserPasskeyStore<ApplicationUser>
{
    private readonly ConditionalWeakTable<ApplicationUser, AuthUserAuthorityLease> _authority = new();
    private bool _disposed;

    /// <inheritdoc />
    public async Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        cancellationToken.ThrowIfCancellationRequested();

        if (user.Id == Guid.Empty)
            user.Id = Guid.NewGuid();
        user.SecurityStamp ??= Guid.NewGuid().ToString("N");
        user.ConcurrencyStamp ??= Guid.NewGuid().ToString("N");
        user.InstanceId = runtime.TenantId;
        DateTimeOffset now = runtime.GetUtcNow();

        AuthCreateUserV1 request;
        try
        {
            request = new AuthCreateUserV1
            {
                TenantId = runtime.TenantId,
                UserId = user.Id,
                UserName = user.UserName,
                NormalizedUserName = user.NormalizedUserName,
                Email = user.Email,
                NormalizedEmail = user.NormalizedEmail,
                PasswordHash = user.PasswordHash,
                SecurityStamp = user.SecurityStamp,
                ConcurrencyStamp = user.ConcurrencyStamp,
                LockoutEnabled = user.LockoutEnabled,
                EmailConfirmed = user.EmailConfirmed,
                EmailConfirmedAt = Utc(user.EmailConfirmedAt),
                PhoneNumber = user.PhoneNumber,
                PhoneNumberConfirmed = user.PhoneNumberConfirmed,
                TwoFactorEnabled = user.TwoFactorEnabled,
                LockoutEnd = user.LockoutEnd?.ToUniversalTime(),
                AccessFailedCount = user.AccessFailedCount,
                Audience = user.Audience,
                UserMetadata = CanonicalObject(user.UserMetadata),
                AppMetadata = CanonicalObject(user.AppMetadata),
                RequiredActions = CanonicalActions(user.RequiredActions),
                FirstName = user.FirstName,
                LastName = user.LastName,
                DisplayName = user.DisplayName,
                AvatarUrl = user.AvatarUrl,
                IsActive = user.IsActive,
                LastLoginAt = Utc(user.LastLoginAt),
                LastLoginIp = user.LastLoginIp,
                SubscriptionTier = user.SubscriptionTier,
                OperationTime = now,
            };
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or InvalidOperationException)
        {
            return IdentityResult.Failed(errors.InvalidUserName(user.UserName ?? string.Empty));
        }

        BaseInstalledModuleMutationHandle<AuthCreateUserV1, AuthCreateUserResultV1> operation = runtime
            .OpenServiceSession().ModuleMutations.Get(AuthCreateUserOperationV1.Identity);
        BaseMutationRequestIdentity identity = operation.CreateRequestIdentity(
            request, $"user:{user.Id:D}:create");
        BaseResult<BaseModuleMutationExecutionResult<AuthCreateUserResultV1>> result = await operation
            .ExecuteAsync(request, identity, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (result is BaseFailure<BaseModuleMutationExecutionResult<AuthCreateUserResultV1>> failure)
            return AuthBaseIdentityErrorMapper.User(failure, errors, user.UserName, user.Email);

        AuthCreateUserResultV1 committed = result.RequireValue().Result;
        user.Created = now.UtcDateTime;
        user.Updated = now.UtcDateTime;
        if (!await RefreshAuthorityAsync(user, committed.Revision, cancellationToken).ConfigureAwait(false))
            throw new AuthBasePersistenceException("auth.persistence.authorityUnavailable");
        return IdentityResult.Success;
    }

    /// <inheritdoc />
    public async Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        cancellationToken.ThrowIfCancellationRequested();

        AuthUserAuthorityLease? authority = await ResolveAuthorityAsync(user, cancellationToken).ConfigureAwait(false);
        if (authority is null)
            return IdentityResult.Failed(errors.ConcurrencyFailure());

        string nextConcurrencyStamp = Guid.NewGuid().ToString("N");
        if ((authority.DirtyFields & AuthUserDirtyFields.PasswordHash) != 0)
            return await UpdatePasswordAsync(user, authority, nextConcurrencyStamp, cancellationToken).ConfigureAwait(false);
        if ((authority.DirtyFields & (AuthUserDirtyFields.SecurityStamp
            | AuthUserDirtyFields.SecurityState
            | AuthUserDirtyFields.AuthenticatorKey)) != 0)
            return await UpdateSecurityStateAsync(user, authority, nextConcurrencyStamp, cancellationToken).ConfigureAwait(false);
        DateTimeOffset now = runtime.GetUtcNow();
        AuthUpdateUserProfileV1 request;
        try
        {
            request = new AuthUpdateUserProfileV1
            {
                TenantId = runtime.TenantId,
                UserId = user.Id,
                ExpectedRevision = authority.Revision,
                AppMetadata = CanonicalObject(user.AppMetadata),
                Audience = user.Audience,
                UserName = user.UserName,
                NormalizedUserName = user.NormalizedUserName,
                Email = user.Email,
                NormalizedEmail = user.NormalizedEmail,
                EmailConfirmed = user.EmailConfirmed,
                EmailConfirmedAt = Utc(user.EmailConfirmedAt),
                PhoneNumber = user.PhoneNumber,
                PhoneNumberConfirmed = user.PhoneNumberConfirmed,
                DisplayName = user.DisplayName,
                FirstName = user.FirstName,
                LastName = user.LastName,
                AvatarUrl = user.AvatarUrl,
                IsActive = user.IsActive,
                UserMetadata = CanonicalObject(user.UserMetadata),
                LastLoginAt = Utc(user.LastLoginAt),
                LastLoginIp = user.LastLoginIp,
                RequiredActions = CanonicalActions(user.RequiredActions),
                SubscriptionTier = user.SubscriptionTier,
                ConcurrencyStamp = nextConcurrencyStamp,
                OperationTime = now,
            };
        }
        catch (Exception exception) when (exception is ArgumentException or JsonException or InvalidOperationException)
        {
            return IdentityResult.Failed(errors.InvalidUserName(user.UserName ?? string.Empty));
        }

        BaseInstalledModuleMutationHandle<AuthUpdateUserProfileV1, AuthUpdateUserProfileResultV1> operation = runtime
            .OpenServiceSession().ModuleMutations.Get(AuthUpdateUserProfileOperationV1.Identity);
        BaseMutationRequestIdentity identity = operation.CreateRequestIdentity(
            request, $"user:{user.Id:D}:revision:{authority.Revision.Value}:profile");
        BaseResult<BaseModuleMutationExecutionResult<AuthUpdateUserProfileResultV1>> result = await operation
            .ExecuteAsync(request, identity, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (result is BaseFailure<BaseModuleMutationExecutionResult<AuthUpdateUserProfileResultV1>> failure)
            return AuthBaseIdentityErrorMapper.User(failure, errors, user.UserName, user.Email);

        user.ConcurrencyStamp = nextConcurrencyStamp;
        user.Updated = now.UtcDateTime;
        if (!await RefreshAuthorityAsync(user, result.RequireValue().Result.Revision, cancellationToken).ConfigureAwait(false))
            throw new AuthBasePersistenceException("auth.persistence.authorityUnavailable");
        return IdentityResult.Success;
    }

    /// <inheritdoc />
    public async Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        cancellationToken.ThrowIfCancellationRequested();
        ApplicationUser? persisted = await FindByIdAsync(user.Id.ToString("D"), cancellationToken).ConfigureAwait(false);
        if (persisted is null || !_authority.TryGetValue(persisted, out AuthUserAuthorityLease? authority))
            return IdentityResult.Failed(errors.ConcurrencyFailure());
        if (persisted.IsDeleted)
            return IdentityResult.Success;

        BaseSession session = runtime.OpenServiceSession();
        BaseResult<AuthUserSubjectAcquisitionReadV1.Row?> acquired = await session.Reads.FirstAsync(
            AuthUserSubjectAcquisitionReadV1.Handle,
            new AuthUserSubjectAcquisitionReadV1
            {
                UserId = BaseRecordId<AuthUserRecordV1>.Create(user.Id.ToString("D")),
            }, cancellationToken).ConfigureAwait(false);
        if (acquired is BaseFailure<AuthUserSubjectAcquisitionReadV1.Row?> acquisitionFailure)
            return AuthBaseIdentityErrorMapper.User(acquisitionFailure, errors, user.UserName, user.Email);
        if (acquired.RequireValue() is not { } acquiredRow)
            return IdentityResult.Failed(errors.ConcurrencyFailure());
        BaseSubjectReference<AuthUserSubject> subject = acquiredRow.Reference;
        BaseExportedSubjectContract<AuthUserSubject> contract = AuthSubjects.Users(session);
        BaseMutationRequestIdentity identity = AuthBaseRuntime.MutationIdentity(
            "hpd.auth.user-subject.tombstone.v1", runtime.TenantId, user.Id.ToString("D"),
            authority.Revision.Value, subject.Incarnation.ToBase64Url());
        BaseResult<BaseSubjectTombstoneResult<AuthUserSubject>> result = await contract.TombstoneAsync(new()
        {
            Subject = subject,
            ExpectedPrivateRevision = authority.Revision,
            Identity = identity,
        }, cancellationToken).ConfigureAwait(false);
        if (result is BaseFailure<BaseSubjectTombstoneResult<AuthUserSubject>> failure)
            return AuthBaseIdentityErrorMapper.User(failure, errors, user.UserName, user.Email);

        BaseSubjectTombstoneResult<AuthUserSubject> tombstone = result.RequireValue();
        string cleanupWorkId = AuthBaseDeterministicId.CreateCleanupWork(
            runtime.TenantId,
            "user",
            user.Id,
            contract,
            subject.Incarnation,
            tombstone.Fact.Fact.SubjectSequence);
        AuthUserCleanupInitializeV1 initialize = new()
        {
            CleanupWorkId = cleanupWorkId,
            TenantId = runtime.TenantId,
            SubjectId = user.Id,
            Subject = subject,
            Incarnation = subject.Incarnation,
            TombstoneSequence = tombstone.Fact.Fact.SubjectSequence,
            TombstoneRevision = tombstone.PrivateRevision.Value,
            WorkflowVersion = 1,
            TombstonedAt = tombstone.TombstonedAt,
            RetirementReceiptScope = "auth.cleanup.initialize",
            OperationTime = tombstone.TombstonedAt,
        };
        BaseSession dispatcher = runtime.OpenLifecycleDispatcherSession();
        BaseInstalledActivationHandle<AuthUserCleanupInitializeV1, AuthCleanupInitializeResultV1> bootstrap =
            dispatcher.Activations.Get(AuthLifecycleActivationDeclarations.BootstrapUser.Identity);
        BaseMutationRequestIdentity bootstrapIdentity = AuthBaseRuntime.MutationIdentity(
            "hpd.auth.cleanup.bootstrap.user.v1", runtime.TenantId, cleanupWorkId,
            tombstone.PrivateRevision.Value, tombstone.Fact.Fact.SubjectSequence.ToString(System.Globalization.CultureInfo.InvariantCulture));
        OperationResult<BaseActivationEnqueueResult> enqueued = await bootstrap
            .EnqueueAsync(initialize, bootstrapIdentity, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (!enqueued.IsSuccess())
            return IdentityResult.Failed(new IdentityError
            {
                Code = "auth.persistence.unavailable",
                Description = "The durable cleanup workflow could not be scheduled.",
            });

        user.IsActive = false;
        user.IsDeleted = true;
        user.DeletedAt = tombstone.TombstonedAt.UtcDateTime;
        authority.Consumed = true;
        return IdentityResult.Success;
    }

    /// <inheritdoc />
    public async Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (!Guid.TryParseExact(userId, "D", out Guid id))
            return null;
        BaseResult<BaseRegisteredReadFirstResult<AuthUserByIdReadV1.Row>> result = await runtime.OpenServiceSession().Reads.FirstWithAuthorityAsync(
            AuthUserByIdReadV1.Handle,
            new AuthUserByIdReadV1 { TenantId = runtime.TenantId, UserId = id },
            cancellationToken).ConfigureAwait(false);
        return await VisibleUserAsync(MapRead(result), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(normalizedUserName))
            return null;
        BaseResult<BaseRegisteredReadFirstResult<AuthUserByNormalizedNameReadV1.Row>> result = await runtime.OpenServiceSession().Reads.FirstWithAuthorityAsync(
            AuthUserByNormalizedNameReadV1.Handle,
            new AuthUserByNormalizedNameReadV1
            {
                TenantId = runtime.TenantId,
                NormalizedName = normalizedUserName,
            }, cancellationToken).ConfigureAwait(false);
        return await VisibleUserAsync(MapRead(result), cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async Task<ApplicationUser?> FindByEmailAsync(string normalizedEmail, CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        cancellationToken.ThrowIfCancellationRequested();
        if (string.IsNullOrWhiteSpace(normalizedEmail))
            return null;
        BaseResult<BaseRegisteredReadFirstResult<AuthUserByNormalizedEmailReadV1.Row>> result = await runtime.OpenServiceSession().Reads.FirstWithAuthorityAsync(
            AuthUserByNormalizedEmailReadV1.Handle,
            new AuthUserByNormalizedEmailReadV1
            {
                TenantId = runtime.TenantId,
                NormalizedEmail = normalizedEmail,
            }, cancellationToken).ConfigureAwait(false);
        return await VisibleUserAsync(MapRead(result), cancellationToken).ConfigureAwait(false);
    }

    private async Task<ApplicationUser?> VisibleUserAsync(
        ApplicationUser? user,
        CancellationToken cancellationToken)
    {
        if (user is null || user.IsDeleted)
            return null;
        return await HydrateSecretsAsync(user, cancellationToken).ConfigureAwait(false);
    }

    /// <inheritdoc />
    public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(user.Id.ToString("D"));
    }

    /// <inheritdoc />
    public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(user.UserName);
    }

    /// <inheritdoc />
    public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken cancellationToken)
    {
        ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); cancellationToken.ThrowIfCancellationRequested();
        user.UserName = userName; MarkDirty(user, AuthUserDirtyFields.Profile); return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(user.NormalizedUserName);
    }

    /// <inheritdoc />
    public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken cancellationToken)
    {
        ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); cancellationToken.ThrowIfCancellationRequested();
        user.NormalizedUserName = normalizedName; MarkDirty(user, AuthUserDirtyFields.Profile); return Task.CompletedTask;
    }

    /// <inheritdoc />
    public Task SetEmailAsync(ApplicationUser user, string? email, CancellationToken cancellationToken)
    { ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); cancellationToken.ThrowIfCancellationRequested(); user.Email = email; MarkDirty(user, AuthUserDirtyFields.Profile); return Task.CompletedTask; }

    /// <inheritdoc />
    public Task<string?> GetEmailAsync(ApplicationUser user, CancellationToken cancellationToken)
    { ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(user.Email); }

    /// <inheritdoc />
    public Task<bool> GetEmailConfirmedAsync(ApplicationUser user, CancellationToken cancellationToken)
    { ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(user.EmailConfirmed); }

    /// <inheritdoc />
    public Task SetEmailConfirmedAsync(ApplicationUser user, bool confirmed, CancellationToken cancellationToken)
    { ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); cancellationToken.ThrowIfCancellationRequested(); user.EmailConfirmed = confirmed; MarkDirty(user, AuthUserDirtyFields.EmailConfirmation); return Task.CompletedTask; }

    /// <inheritdoc />
    public Task<string?> GetNormalizedEmailAsync(ApplicationUser user, CancellationToken cancellationToken)
    { ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(user.NormalizedEmail); }

    /// <inheritdoc />
    public Task SetNormalizedEmailAsync(ApplicationUser user, string? normalizedEmail, CancellationToken cancellationToken)
    { ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); cancellationToken.ThrowIfCancellationRequested(); user.NormalizedEmail = normalizedEmail; MarkDirty(user, AuthUserDirtyFields.Profile); return Task.CompletedTask; }

    /// <inheritdoc />
    public Task SetPhoneNumberAsync(ApplicationUser user, string? phoneNumber, CancellationToken cancellationToken)
    { ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); cancellationToken.ThrowIfCancellationRequested(); user.PhoneNumber = phoneNumber; MarkDirty(user, AuthUserDirtyFields.Profile); return Task.CompletedTask; }

    /// <inheritdoc />
    public Task<string?> GetPhoneNumberAsync(ApplicationUser user, CancellationToken cancellationToken)
    { ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(user.PhoneNumber); }

    /// <inheritdoc />
    public Task<bool> GetPhoneNumberConfirmedAsync(ApplicationUser user, CancellationToken cancellationToken)
    { ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(user.PhoneNumberConfirmed); }

    /// <inheritdoc />
    public Task SetPhoneNumberConfirmedAsync(ApplicationUser user, bool confirmed, CancellationToken cancellationToken)
    { ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); cancellationToken.ThrowIfCancellationRequested(); user.PhoneNumberConfirmed = confirmed; MarkDirty(user, AuthUserDirtyFields.PhoneConfirmation); return Task.CompletedTask; }

    /// <inheritdoc />
    public Task SetPasswordHashAsync(ApplicationUser user, string? passwordHash, CancellationToken cancellationToken)
    { ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); cancellationToken.ThrowIfCancellationRequested(); user.PasswordHash = passwordHash; MarkDirty(user, AuthUserDirtyFields.PasswordHash); return Task.CompletedTask; }

    /// <inheritdoc />
    public Task<string?> GetPasswordHashAsync(ApplicationUser user, CancellationToken cancellationToken)
    { ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(user.PasswordHash); }

    /// <inheritdoc />
    public Task<bool> HasPasswordAsync(ApplicationUser user, CancellationToken cancellationToken)
    { ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(user.PasswordHash is not null); }

    /// <inheritdoc />
    public Task SetSecurityStampAsync(ApplicationUser user, string stamp, CancellationToken cancellationToken)
    { ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); ArgumentException.ThrowIfNullOrWhiteSpace(stamp); cancellationToken.ThrowIfCancellationRequested(); user.SecurityStamp = stamp; MarkDirty(user, AuthUserDirtyFields.SecurityStamp); return Task.CompletedTask; }

    /// <inheritdoc />
    public Task<string?> GetSecurityStampAsync(ApplicationUser user, CancellationToken cancellationToken)
    { ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(user.SecurityStamp); }

    /// <inheritdoc />
    public Task<DateTimeOffset?> GetLockoutEndDateAsync(ApplicationUser user, CancellationToken cancellationToken)
    { ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(user.LockoutEnd); }

    /// <inheritdoc />
    public Task SetLockoutEndDateAsync(ApplicationUser user, DateTimeOffset? lockoutEnd, CancellationToken cancellationToken)
    { ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); cancellationToken.ThrowIfCancellationRequested(); user.LockoutEnd = lockoutEnd; MarkDirty(user, AuthUserDirtyFields.SecurityState); return Task.CompletedTask; }

    /// <inheritdoc />
    public Task<int> IncrementAccessFailedCountAsync(ApplicationUser user, CancellationToken cancellationToken)
    { ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); cancellationToken.ThrowIfCancellationRequested(); user.AccessFailedCount = checked(user.AccessFailedCount + 1); MarkDirty(user, AuthUserDirtyFields.SecurityState); return Task.FromResult(user.AccessFailedCount); }

    /// <inheritdoc />
    public Task ResetAccessFailedCountAsync(ApplicationUser user, CancellationToken cancellationToken)
    { ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); cancellationToken.ThrowIfCancellationRequested(); user.AccessFailedCount = 0; MarkDirty(user, AuthUserDirtyFields.SecurityState); return Task.CompletedTask; }

    /// <inheritdoc />
    public Task<int> GetAccessFailedCountAsync(ApplicationUser user, CancellationToken cancellationToken)
    { ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(user.AccessFailedCount); }

    /// <inheritdoc />
    public Task<bool> GetLockoutEnabledAsync(ApplicationUser user, CancellationToken cancellationToken)
    { ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(user.LockoutEnabled); }

    /// <inheritdoc />
    public Task SetLockoutEnabledAsync(ApplicationUser user, bool enabled, CancellationToken cancellationToken)
    { ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); cancellationToken.ThrowIfCancellationRequested(); user.LockoutEnabled = enabled; MarkDirty(user, AuthUserDirtyFields.SecurityState); return Task.CompletedTask; }

    /// <inheritdoc />
    public Task SetTwoFactorEnabledAsync(ApplicationUser user, bool enabled, CancellationToken cancellationToken)
    { ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); cancellationToken.ThrowIfCancellationRequested(); user.TwoFactorEnabled = enabled; MarkDirty(user, AuthUserDirtyFields.SecurityState); return Task.CompletedTask; }

    /// <inheritdoc />
    public Task<bool> GetTwoFactorEnabledAsync(ApplicationUser user, CancellationToken cancellationToken)
    { ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(user.TwoFactorEnabled); }

    /// <inheritdoc />
    public Task SetAuthenticatorKeyAsync(ApplicationUser user, string? key, CancellationToken cancellationToken)
    { ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); cancellationToken.ThrowIfCancellationRequested(); AuthUserAuthorityLease authority = RequireAttachedAuthority(user); authority.AuthenticatorKey = key; authority.DirtyFields |= AuthUserDirtyFields.AuthenticatorKey; return Task.CompletedTask; }

    /// <inheritdoc />
    public Task<string?> GetAuthenticatorKeyAsync(ApplicationUser user, CancellationToken cancellationToken)
    { ThrowIfDisposed(); ArgumentNullException.ThrowIfNull(user); cancellationToken.ThrowIfCancellationRequested(); return Task.FromResult(RequireAttachedAuthority(user).AuthenticatorKey); }

    /// <inheritdoc />
    public void Dispose() => _disposed = true;

    private ApplicationUser? MapRead<T>(BaseResult<BaseRegisteredReadFirstResult<T>> result)
        where T : class, IAuthUserIdentityProjectionV1
    {
        if (result is BaseFailure<BaseRegisteredReadFirstResult<T>> failure)
            throw new AuthBasePersistenceException(AuthBaseIdentityErrorMapper.SafeReadCode(failure.Error));
        BaseRegisteredReadFirstResult<T> read = result.RequireValue();
        T? row = read.Item;
        if (row is null)
            return null;
        var user = new ApplicationUser
        {
            Id = row.Id,
            InstanceId = row.TenantId,
            UserName = row.UserName,
            NormalizedUserName = row.NormalizedUserName,
            Email = row.Email,
            NormalizedEmail = row.NormalizedEmail,
            EmailConfirmed = row.EmailConfirmed,
            ConcurrencyStamp = row.ConcurrencyStamp,
            PhoneNumber = row.PhoneNumber,
            PhoneNumberConfirmed = row.PhoneNumberConfirmed,
            TwoFactorEnabled = row.TwoFactorEnabled,
            LockoutEnd = row.LockoutEnd,
            LockoutEnabled = row.LockoutEnabled,
            AccessFailedCount = row.AccessFailedCount,
            Audience = row.Audience,
            UserMetadata = CanonicalText(row.UserMetadata),
            AppMetadata = CanonicalText(row.AppMetadata),
            RequiredActions = JsonSerializer.Deserialize(
                row.RequiredActions.Utf8.Span,
                HPDAuthInfrastructureJsonSerializerContext.Default.ListString) ?? [],
            FirstName = row.FirstName,
            LastName = row.LastName,
            DisplayName = row.DisplayName,
            AvatarUrl = row.AvatarUrl,
            IsActive = row.IsActive,
            IsDeleted = row.IsDeleted,
            DeletedAt = row.DeletedAt?.UtcDateTime,
            Created = row.CreatedAt.UtcDateTime,
            Updated = row.UpdatedAt.UtcDateTime,
            LastLoginAt = row.LastLoginAt?.UtcDateTime,
            LastLoginIp = row.LastLoginIp,
            SubscriptionTier = row.SubscriptionTier,
            EmailConfirmedAt = row.EmailConfirmedAt?.UtcDateTime,
        };
        Remember(user, row.Revision, read.Authority);
        return user;
    }

    private async Task<ApplicationUser?> HydrateSecretsAsync(
        ApplicationUser? user,
        CancellationToken cancellationToken)
    {
        if (user is null)
            return null;
        BaseResult<BaseRegisteredReadFirstResult<AuthUserPasswordReadV1.Row>> result = await runtime.OpenServiceSession().Reads.FirstWithAuthorityAsync(
            AuthUserPasswordReadV1.Handle,
            new AuthUserPasswordReadV1 { TenantId = runtime.TenantId, UserId = user.Id },
            cancellationToken).ConfigureAwait(false);
        if (result is BaseFailure<BaseRegisteredReadFirstResult<AuthUserPasswordReadV1.Row>> failure)
            throw new AuthBasePersistenceException(AuthBaseIdentityErrorMapper.SafeReadCode(failure.Error));
        BaseRegisteredReadFirstResult<AuthUserPasswordReadV1.Row> secretRead = result.RequireValue();
        AuthUserPasswordReadV1.Row? secret = secretRead.Item;
        if (secret is null)
            return null;
        user.PasswordHash = secret.PasswordHash;
        user.SecurityStamp = secret.SecurityStamp;
        AuthUserAuthorityLease authority = _authority.TryGetValue(user, out AuthUserAuthorityLease? attached)
            ? attached
            : throw new AuthBasePersistenceException("auth.persistence.invalidAuthority");
        if (secret.Revision != authority.Revision
            || !secretRead.Authority.AuthorityChecksum.AsSpan().SequenceEqual(authority.Authority.AuthorityChecksum.AsSpan()))
            throw new AuthBasePersistenceException("auth.persistence.authorityChanged");
        RevisionToken revision = authority is not null
            ? authority.Revision
            : throw new AuthBasePersistenceException("auth.persistence.invalidAuthority");
        BaseResult<BaseRegisteredReadFirstResult<AuthUserTwoFactorSecretsReadV1.Row>> twoFactorResult = await runtime
            .OpenServiceSession().Reads.FirstWithAuthorityAsync(
                AuthUserTwoFactorSecretsReadV1.Handle,
                new AuthUserTwoFactorSecretsReadV1 { TenantId = runtime.TenantId, UserId = user.Id },
                cancellationToken).ConfigureAwait(false);
        if (twoFactorResult is BaseFailure<BaseRegisteredReadFirstResult<AuthUserTwoFactorSecretsReadV1.Row>> twoFactorFailure)
            throw new AuthBasePersistenceException(AuthBaseIdentityErrorMapper.SafeReadCode(twoFactorFailure.Error));
        BaseRegisteredReadFirstResult<AuthUserTwoFactorSecretsReadV1.Row> twoFactorRead = twoFactorResult.RequireValue();
        AuthUserTwoFactorSecretsReadV1.Row? twoFactor = twoFactorRead.Item;
        if (twoFactor is null
            || twoFactor.Revision != revision
            || !twoFactorRead.Authority.AuthorityChecksum.AsSpan().SequenceEqual(authority.Authority.AuthorityChecksum.AsSpan()))
            throw new AuthBasePersistenceException("auth.persistence.authorityChanged");
        Remember(user, revision, authority.Authority, secret.PasswordHash, secret.SecurityStamp, twoFactor.AuthenticatorKey);
        return user;
    }

    private async Task<IdentityResult> UpdatePasswordAsync(
        ApplicationUser user,
        AuthUserAuthorityLease authority,
        string nextConcurrencyStamp,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(user.SecurityStamp))
            return IdentityResult.Failed(errors.ConcurrencyFailure());
        DateTimeOffset now = runtime.GetUtcNow();
        BaseResult<BaseModuleMutationExecutionResult<AuthSecurityMutationResultV1>> result;
        if (user.PasswordHash is null)
        {
            var request = new AuthRemovePasswordV1
            {
                TenantId = runtime.TenantId, UserId = user.Id, ExpectedRevision = authority.Revision,
                SecurityStamp = user.SecurityStamp, ConcurrencyStamp = nextConcurrencyStamp, OperationTime = now,
            };
            BaseInstalledModuleMutationHandle<AuthRemovePasswordV1, AuthSecurityMutationResultV1> operation = runtime
                .OpenServiceSession().ModuleMutations.Get(AuthRemovePasswordOperationV1.Identity);
            BaseMutationRequestIdentity identity = operation.CreateRequestIdentity(
                request, $"user:{user.Id:D}:revision:{authority.Revision.Value}:remove-password");
            result = await operation
                .ExecuteAsync(request, identity, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        else
        {
            var request = new AuthChangePasswordV1
            {
                TenantId = runtime.TenantId, UserId = user.Id, ExpectedRevision = authority.Revision,
                PasswordHash = user.PasswordHash, SecurityStamp = user.SecurityStamp,
                ConcurrencyStamp = nextConcurrencyStamp, OperationTime = now,
            };
            BaseInstalledModuleMutationHandle<AuthChangePasswordV1, AuthSecurityMutationResultV1> operation = runtime
                .OpenServiceSession().ModuleMutations.Get(AuthChangePasswordOperationV1.Identity);
            BaseMutationRequestIdentity identity = operation.CreateRequestIdentity(
                request, $"user:{user.Id:D}:revision:{authority.Revision.Value}:change-password");
            result = await operation
                .ExecuteAsync(request, identity, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        if (result is BaseFailure<BaseModuleMutationExecutionResult<AuthSecurityMutationResultV1>> failure)
            return AuthBaseIdentityErrorMapper.User(failure, errors, user.UserName, user.Email);
        user.ConcurrencyStamp = nextConcurrencyStamp;
        user.Updated = now.UtcDateTime;
        if (!await RefreshAuthorityAsync(user, result.RequireValue().Result.Revision, cancellationToken).ConfigureAwait(false))
            throw new AuthBasePersistenceException("auth.persistence.authorityUnavailable");
        return IdentityResult.Success;
    }

    /// <summary>
    /// Atomically resets password, stamp, lockout, and generation authority through
    /// the installed reset-password operation.
    /// </summary>
    internal async Task<IdentityResult> ResetPasswordAsync(
        ApplicationUser user,
        string passwordHash,
        string securityStamp,
        string concurrencyStamp,
        CancellationToken cancellationToken)
    {
        ThrowIfDisposed();
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(passwordHash);
        ArgumentException.ThrowIfNullOrWhiteSpace(securityStamp);
        ArgumentException.ThrowIfNullOrWhiteSpace(concurrencyStamp);
        cancellationToken.ThrowIfCancellationRequested();

        AuthUserAuthorityLease? authority = await ResolveAuthorityAsync(user, cancellationToken)
            .ConfigureAwait(false);
        if (authority is null)
            return IdentityResult.Failed(errors.ConcurrencyFailure());

        DateTimeOffset now = runtime.GetUtcNow();
        var request = new AuthResetPasswordV1
        {
            TenantId = runtime.TenantId,
            UserId = user.Id,
            ExpectedRevision = authority.Revision,
            PasswordHash = passwordHash,
            SecurityStamp = securityStamp,
            ConcurrencyStamp = concurrencyStamp,
            LockoutEnabled = user.LockoutEnabled,
            OperationTime = now,
        };
        BaseInstalledModuleMutationHandle<AuthResetPasswordV1, AuthSecurityMutationResultV1> operation =
            runtime.OpenServiceSession().ModuleMutations.Get(AuthResetPasswordOperationV1.Identity);
        BaseMutationRequestIdentity identity = operation.CreateRequestIdentity(
            request,
            $"user:{user.Id:D}:revision:{authority.Revision.Value}:reset-password");
        BaseResult<BaseModuleMutationExecutionResult<AuthSecurityMutationResultV1>> result = await operation
            .ExecuteAsync(request, identity, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        if (result is BaseFailure<BaseModuleMutationExecutionResult<AuthSecurityMutationResultV1>> failure)
            return AuthBaseIdentityErrorMapper.User(failure, errors, user.UserName, user.Email);

        AuthSecurityMutationResultV1 committed = result.RequireValue().Result;
        user.PasswordHash = passwordHash;
        user.SecurityStamp = securityStamp;
        user.ConcurrencyStamp = concurrencyStamp;
        user.AccessFailedCount = 0;
        user.LockoutEnd = null;
        user.Updated = now.UtcDateTime;
        if (!await RefreshAuthorityAsync(user, committed.Revision, cancellationToken).ConfigureAwait(false))
            throw new AuthBasePersistenceException("auth.persistence.authorityUnavailable");
        return IdentityResult.Success;
    }

    private async Task<IdentityResult> UpdateSecurityStateAsync(
        ApplicationUser user,
        AuthUserAuthorityLease authority,
        string nextConcurrencyStamp,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(user.SecurityStamp))
            return IdentityResult.Failed(errors.ConcurrencyFailure());
        DateTimeOffset now = runtime.GetUtcNow();
        var request = new AuthSetSecurityStateV1
        {
            TenantId = runtime.TenantId,
            UserId = user.Id,
            ExpectedRevision = authority.Revision,
            TwoFactorEnabled = user.TwoFactorEnabled,
            AuthenticatorKey = authority.AuthenticatorKey,
            ClearLockoutEnd = user.LockoutEnd is null,
            LockoutEnd = user.LockoutEnd,
            LockoutEnabled = user.LockoutEnabled,
            AccessFailedCount = user.AccessFailedCount,
            SecurityStamp = user.SecurityStamp,
            ConcurrencyStamp = nextConcurrencyStamp,
            OperationTime = now,
        };
        BaseInstalledModuleMutationHandle<AuthSetSecurityStateV1, AuthSecurityMutationResultV1> operation = runtime
            .OpenServiceSession().ModuleMutations.Get(AuthSetSecurityStateOperationV1.Identity);
        BaseMutationRequestIdentity identity = operation.CreateRequestIdentity(
            request, $"user:{user.Id:D}:revision:{authority.Revision.Value}:security-state");
        BaseResult<BaseModuleMutationExecutionResult<AuthSecurityMutationResultV1>> result = await operation
            .ExecuteAsync(request, identity, cancellationToken: cancellationToken).ConfigureAwait(false);
        if (result is BaseFailure<BaseModuleMutationExecutionResult<AuthSecurityMutationResultV1>> failure)
            return AuthBaseIdentityErrorMapper.User(failure, errors, user.UserName, user.Email);
        user.ConcurrencyStamp = nextConcurrencyStamp;
        user.Updated = now.UtcDateTime;
        if (!await RefreshAuthorityAsync(user, result.RequireValue().Result.Revision, cancellationToken).ConfigureAwait(false))
            throw new AuthBasePersistenceException("auth.persistence.authorityUnavailable");
        return IdentityResult.Success;
    }

    private async Task<bool> RefreshAuthorityAsync(
        ApplicationUser user,
        RevisionToken expectedRevision,
        CancellationToken cancellationToken)
    {
        ApplicationUser? committed = await FindByIdAsync(user.Id.ToString("D"), cancellationToken)
            .ConfigureAwait(false);
        if (committed is null
            || !_authority.TryGetValue(committed, out AuthUserAuthorityLease? authority)
            || authority.Revision != expectedRevision)
            return false;

        Remember(
            user,
            authority.Revision,
            authority.Authority,
            authority.PasswordHash,
            authority.SecurityStamp,
            authority.AuthenticatorKey);
        return true;
    }

    private async Task<AuthUserAuthorityLease?> ResolveAuthorityAsync(
        ApplicationUser user,
        CancellationToken cancellationToken)
    {
        if (user.InstanceId != runtime.TenantId || user.Id == Guid.Empty)
            return null;
        if (_authority.TryGetValue(user, out AuthUserAuthorityLease? attached))
            return !attached.Consumed
                && attached.TenantId == runtime.TenantId
                && attached.RecordId == user.Id
                && string.Equals(attached.Snapshot.ConcurrencyStamp, user.ConcurrencyStamp, StringComparison.Ordinal)
                    ? attached
                    : null;

        ApplicationUser? persisted = await FindByIdAsync(user.Id.ToString("D"), cancellationToken).ConfigureAwait(false);
        if (persisted is null
            || !_authority.TryGetValue(persisted, out AuthUserAuthorityLease? fresh)
            || !string.Equals(persisted.ConcurrencyStamp, user.ConcurrencyStamp, StringComparison.Ordinal))
            return null;
        Remember(user, fresh.Revision, fresh.Authority, fresh.PasswordHash, fresh.SecurityStamp, fresh.AuthenticatorKey);
        return _authority.TryGetValue(user, out AuthUserAuthorityLease? resolved) ? resolved : null;
    }

    private void MarkDirty(ApplicationUser user, AuthUserDirtyFields field)
    {
        if (_authority.TryGetValue(user, out AuthUserAuthorityLease? authority))
            authority.DirtyFields |= field;
    }

    private void Remember(
        ApplicationUser user,
        RevisionToken revision,
        BaseRegisteredReadSnapshotAuthority authority,
        string? passwordHash = null,
        string? securityStamp = null,
        string? authenticatorKey = null)
    {
        _authority.Remove(user);
        _authority.Add(user, new AuthUserAuthorityLease
        {
            TenantId = runtime.TenantId,
            RecordId = user.Id,
            Revision = revision,
            Authority = authority,
            Snapshot = AuthUserOrdinarySnapshot.Capture(user),
            PasswordHash = passwordHash,
            SecurityStamp = securityStamp,
            AuthenticatorKey = authenticatorKey,
        });
    }

    private AuthUserAuthorityLease RequireAttachedAuthority(ApplicationUser user)
    {
        if (!_authority.TryGetValue(user, out AuthUserAuthorityLease? authority)
            || authority.Consumed
            || authority.TenantId != runtime.TenantId
            || authority.RecordId != user.Id)
            throw new AuthBasePersistenceException("auth.persistence.invalidAuthority");
        return authority;
    }

    private static BaseCanonicalJson CanonicalObject(string? value) => BaseCanonicalJson.ParseAndCanonicalize(
        Encoding.UTF8.GetBytes(string.IsNullOrWhiteSpace(value) ? "{}" : value),
        new BaseCanonicalJsonLimits
        {
            MaximumCanonicalBytes = 32_768, MaximumDepth = 16,
            MaximumArrayItemsPerContainer = 1_024, MaximumObjectPropertiesPerContainer = 1_024,
            MaximumTotalNodes = 4_096, MaximumTotalStringUtf8Bytes = 32_768,
            MaximumTotalNameUtf8Bytes = 32_768,
        });

    private static BaseCanonicalJson CanonicalActions(List<string>? actions)
    {
        byte[] utf8 = JsonSerializer.SerializeToUtf8Bytes(
            actions ?? [], HPDAuthInfrastructureJsonSerializerContext.Default.ListString);
        return BaseCanonicalJson.ParseAndValidate(utf8, new BaseCanonicalJsonLimits
        {
            MaximumCanonicalBytes = 4_096, MaximumDepth = 2,
            MaximumArrayItemsPerContainer = 32, MaximumObjectPropertiesPerContainer = 1,
            MaximumTotalNodes = 33, MaximumTotalStringUtf8Bytes = 4_096,
            MaximumTotalNameUtf8Bytes = 1,
        });
    }

    private static string CanonicalText(BaseCanonicalJson value) => Encoding.UTF8.GetString(value.Utf8.Span);

    private static DateTimeOffset? Utc(DateTime? value) => value is null
        ? null
        : new DateTimeOffset(DateTime.SpecifyKind(value.Value, DateTimeKind.Utc));

    private void ThrowIfDisposed() => ObjectDisposedException.ThrowIf(_disposed, this);

}
