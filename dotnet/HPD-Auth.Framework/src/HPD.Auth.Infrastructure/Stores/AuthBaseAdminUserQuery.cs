using System.Text;
using System.Text.Json;
using HPD.Auth.Base;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Infrastructure.Base;
using HPD.Auth.Infrastructure.Serialization;
using HPD.Base;
using Microsoft.AspNetCore.Identity;

namespace HPD.Auth.Infrastructure.Stores;

/// <summary>Executes bounded administrative identity reads through the installed Auth Base graph.</summary>
internal sealed class AuthBaseAdminUserQuery(
    AuthBaseRuntime runtime,
    ILookupNormalizer normalizer) : IAuthAdminUserQuery
{
    /// <inheritdoc />
    public async Task<AuthAdminUserQueryResult> ExecuteAsync(
        AuthAdminUserQuery request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegative(request.Offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.Limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(request.Limit, 200);
        if (request.Offset > 100_000)
            throw new ArgumentOutOfRangeException(nameof(request), "The administrative offset exceeds its installed bound.");

        Guid roleId = Guid.Empty;
        bool applyRole = !string.IsNullOrWhiteSpace(request.Role);
        if (applyRole)
        {
            string normalizedRole = normalizer.NormalizeName(request.Role!)
                ?? throw new ArgumentException("The role filter is invalid.", nameof(request));
            BaseResult<AuthRoleByNormalizedNameReadV1.Row?> role = await runtime.OpenServiceSession().Reads.FirstAsync(
                AuthRoleByNormalizedNameReadV1.Handle,
                new AuthRoleByNormalizedNameReadV1 { TenantId = runtime.TenantId, NormalizedName = normalizedRole },
                cancellationToken).ConfigureAwait(false);
            if (role is BaseFailure<AuthRoleByNormalizedNameReadV1.Row?> roleFailure)
                throw new AuthBasePersistenceException(AuthBaseIdentityErrorMapper.SafeReadCode(roleFailure.Error));
            if (role.RequireValue() is not { } roleRow)
                return new AuthAdminUserQueryResult { Users = [], Total = 0 };
            roleId = roleRow.Id;
        }

        return (request.Sort, request.Direction) switch
        {
            (AuthAdminUserSort.CreatedAt, AuthAdminSortDirection.Ascending) => await ExecuteAsync(
                AuthAdminUsersCreatedAtAscReadV1.Handle, Parameters<AuthAdminUsersCreatedAtAscReadV1>(request, roleId), request, cancellationToken).ConfigureAwait(false),
            (AuthAdminUserSort.CreatedAt, AuthAdminSortDirection.Descending) => await ExecuteAsync(
                AuthAdminUsersCreatedAtDescReadV1.Handle, Parameters<AuthAdminUsersCreatedAtDescReadV1>(request, roleId), request, cancellationToken).ConfigureAwait(false),
            (AuthAdminUserSort.Email, AuthAdminSortDirection.Ascending) => await ExecuteAsync(
                AuthAdminUsersEmailAscReadV1.Handle, Parameters<AuthAdminUsersEmailAscReadV1>(request, roleId), request, cancellationToken).ConfigureAwait(false),
            (AuthAdminUserSort.Email, AuthAdminSortDirection.Descending) => await ExecuteAsync(
                AuthAdminUsersEmailDescReadV1.Handle, Parameters<AuthAdminUsersEmailDescReadV1>(request, roleId), request, cancellationToken).ConfigureAwait(false),
            (AuthAdminUserSort.LastLoginAt, AuthAdminSortDirection.Ascending) => await ExecuteAsync(
                AuthAdminUsersLastLoginAtAscReadV1.Handle, Parameters<AuthAdminUsersLastLoginAtAscReadV1>(request, roleId), request, cancellationToken).ConfigureAwait(false),
            (AuthAdminUserSort.LastLoginAt, AuthAdminSortDirection.Descending) => await ExecuteAsync(
                AuthAdminUsersLastLoginAtDescReadV1.Handle, Parameters<AuthAdminUsersLastLoginAtDescReadV1>(request, roleId), request, cancellationToken).ConfigureAwait(false),
            _ => throw new ArgumentOutOfRangeException(nameof(request), "The administrative sort contract is invalid."),
        };
    }

    private async Task<AuthAdminUserQueryResult> ExecuteAsync<TParameters, TRow>(
        BaseReadHandle<TParameters, TRow> handle,
        TParameters parameters,
        AuthAdminUserQuery request,
        CancellationToken cancellationToken)
        where TParameters : class
        where TRow : class, IAuthAdminUserReadRow
    {
        BaseResult<BasePage<TRow>> result = await runtime.OpenServiceSession().Reads.ExecuteOffsetAsync(
            handle, parameters, BaseReadOffsetRequest.Create(request.Offset, request.Limit), cancellationToken)
            .ConfigureAwait(false);
        if (result is BaseFailure<BasePage<TRow>> failure)
            throw new AuthBasePersistenceException(AuthBaseIdentityErrorMapper.SafeReadCode(failure.Error));
        BasePage<TRow> page = result.RequireValue();
        return new AuthAdminUserQueryResult
        {
            Users = page.Items.Select(Map).ToArray(),
            Total = page.Count is { IsExact: true, Total: { } total } ? total
                : throw new AuthBasePersistenceException("auth.persistence.countUnavailable"),
        };
    }

    private TParameters Parameters<TParameters>(AuthAdminUserQuery request, Guid roleId)
        where TParameters : class
    {
        object value = typeof(TParameters) == typeof(AuthAdminUsersCreatedAtAscReadV1) ? new AuthAdminUsersCreatedAtAscReadV1
        {
            TenantId = runtime.TenantId, ApplySearch = Has(request.Search), Search = request.Search ?? string.Empty,
            ApplyEmail = Has(request.Email), Email = request.Email ?? string.Empty,
            ApplyEmailVerified = request.EmailVerified.HasValue, EmailVerified = request.EmailVerified ?? false,
            ApplyEnabled = request.Enabled.HasValue, Enabled = request.Enabled ?? false,
            ApplyRole = Has(request.Role), RoleId = roleId,
        } : typeof(TParameters) == typeof(AuthAdminUsersCreatedAtDescReadV1) ? new AuthAdminUsersCreatedAtDescReadV1
        {
            TenantId = runtime.TenantId, ApplySearch = Has(request.Search), Search = request.Search ?? string.Empty,
            ApplyEmail = Has(request.Email), Email = request.Email ?? string.Empty,
            ApplyEmailVerified = request.EmailVerified.HasValue, EmailVerified = request.EmailVerified ?? false,
            ApplyEnabled = request.Enabled.HasValue, Enabled = request.Enabled ?? false,
            ApplyRole = Has(request.Role), RoleId = roleId,
        } : typeof(TParameters) == typeof(AuthAdminUsersEmailAscReadV1) ? new AuthAdminUsersEmailAscReadV1
        {
            TenantId = runtime.TenantId, ApplySearch = Has(request.Search), Search = request.Search ?? string.Empty,
            ApplyEmail = Has(request.Email), Email = request.Email ?? string.Empty,
            ApplyEmailVerified = request.EmailVerified.HasValue, EmailVerified = request.EmailVerified ?? false,
            ApplyEnabled = request.Enabled.HasValue, Enabled = request.Enabled ?? false,
            ApplyRole = Has(request.Role), RoleId = roleId,
        } : typeof(TParameters) == typeof(AuthAdminUsersEmailDescReadV1) ? new AuthAdminUsersEmailDescReadV1
        {
            TenantId = runtime.TenantId, ApplySearch = Has(request.Search), Search = request.Search ?? string.Empty,
            ApplyEmail = Has(request.Email), Email = request.Email ?? string.Empty,
            ApplyEmailVerified = request.EmailVerified.HasValue, EmailVerified = request.EmailVerified ?? false,
            ApplyEnabled = request.Enabled.HasValue, Enabled = request.Enabled ?? false,
            ApplyRole = Has(request.Role), RoleId = roleId,
        } : typeof(TParameters) == typeof(AuthAdminUsersLastLoginAtAscReadV1) ? new AuthAdminUsersLastLoginAtAscReadV1
        {
            TenantId = runtime.TenantId, ApplySearch = Has(request.Search), Search = request.Search ?? string.Empty,
            ApplyEmail = Has(request.Email), Email = request.Email ?? string.Empty,
            ApplyEmailVerified = request.EmailVerified.HasValue, EmailVerified = request.EmailVerified ?? false,
            ApplyEnabled = request.Enabled.HasValue, Enabled = request.Enabled ?? false,
            ApplyRole = Has(request.Role), RoleId = roleId,
        } : typeof(TParameters) == typeof(AuthAdminUsersLastLoginAtDescReadV1) ? new AuthAdminUsersLastLoginAtDescReadV1
        {
            TenantId = runtime.TenantId, ApplySearch = Has(request.Search), Search = request.Search ?? string.Empty,
            ApplyEmail = Has(request.Email), Email = request.Email ?? string.Empty,
            ApplyEmailVerified = request.EmailVerified.HasValue, EmailVerified = request.EmailVerified ?? false,
            ApplyEnabled = request.Enabled.HasValue, Enabled = request.Enabled ?? false,
            ApplyRole = Has(request.Role), RoleId = roleId,
        } : throw new InvalidOperationException("auth.admin.userRead.invalidType");
        return (TParameters)value;
    }

    private ApplicationUser Map(IAuthAdminUserReadRow row) => new()
    {
        Id = row.Id, InstanceId = runtime.TenantId, UserName = row.Email, Email = row.Email,
        EmailConfirmed = row.EmailConfirmed, FirstName = row.FirstName, LastName = row.LastName,
        DisplayName = row.DisplayName, SubscriptionTier = row.SubscriptionTier,
        IsActive = row.IsActive, IsDeleted = row.IsDeleted, LastLoginAt = row.LastLoginAt?.UtcDateTime,
        LastLoginIp = row.LastLoginIp, Created = row.CreatedAt.UtcDateTime,
        UserMetadata = Encoding.UTF8.GetString(row.UserMetadata.Utf8.Span),
        AppMetadata = Encoding.UTF8.GetString(row.AppMetadata.Utf8.Span),
        RequiredActions = JsonSerializer.Deserialize(row.RequiredActions.Utf8.Span,
            HPDAuthInfrastructureJsonSerializerContext.Default.ListString) ?? [],
        LockoutEnd = row.LockoutEnd,
    };

    private static bool Has(string? value) => !string.IsNullOrWhiteSpace(value);
}
