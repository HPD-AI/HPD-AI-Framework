using HPD.Base;
using Microsoft.AspNetCore.Identity;

namespace HPD.Auth.Infrastructure.Base;

/// <summary>
/// Converts bounded HPD Base failures into the closed ASP.NET Core Identity failure surface.
/// </summary>
internal static class AuthBaseIdentityErrorMapper
{
    private const string UserNameConstraint = "auth.idx.users.tenantUserName";
    private const string UserEmailConstraint = "auth.idx.users.tenantEmail";
    private const string RoleNameConstraint = "auth.idx.roles.tenantName";

    /// <summary>Maps one failed user mutation without disclosing provider details.</summary>
    internal static IdentityResult User<T>(
        BaseFailure<T> failure,
        IdentityErrorDescriber errors,
        string? userName,
        string? email)
    {
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentNullException.ThrowIfNull(errors);
        ThrowIfIndeterminate(failure.Error);

        return failure.Error.Conflict?.Constraint switch
        {
            UserNameConstraint => IdentityResult.Failed(errors.DuplicateUserName(userName ?? string.Empty)),
            UserEmailConstraint => IdentityResult.Failed(errors.DuplicateEmail(email ?? string.Empty)),
            _ when IsConcurrency(failure) => IdentityResult.Failed(errors.ConcurrencyFailure()),
            _ => IdentityResult.Failed(SafeIdentityError(failure.Error)),
        };
    }

    /// <summary>Maps one failed role mutation without disclosing provider details.</summary>
    internal static IdentityResult Role<T>(
        BaseFailure<T> failure,
        IdentityErrorDescriber errors,
        string? roleName)
    {
        ArgumentNullException.ThrowIfNull(failure);
        ArgumentNullException.ThrowIfNull(errors);
        ThrowIfIndeterminate(failure.Error);

        return failure.Error.Conflict?.Constraint switch
        {
            RoleNameConstraint => IdentityResult.Failed(errors.DuplicateRoleName(roleName ?? string.Empty)),
            _ when IsConcurrency(failure) => IdentityResult.Failed(errors.ConcurrencyFailure()),
            _ => IdentityResult.Failed(SafeIdentityError(failure.Error)),
        };
    }

    /// <summary>Returns the fixed Auth code for one failed non-Identity read.</summary>
    internal static string SafeReadCode(BaseError error)
    {
        ArgumentNullException.ThrowIfNull(error);
        ThrowIfIndeterminate(error);
        return error.Category switch
        {
            ErrorCategory.Authentication or ErrorCategory.Authorization => "auth.operation.unauthorized",
            ErrorCategory.NotFound => "auth.operation.notFound",
            ErrorCategory.Conflict => "auth.operation.conflict",
            ErrorCategory.Validation => "auth.operation.invalid",
            ErrorCategory.Unsupported or ErrorCategory.Capability => "auth.persistence.unavailable",
            _ => "auth.persistence.unavailable",
        };
    }

    /// <summary>Returns the fixed Auth code for one failed non-Identity mutation.</summary>
    internal static string SafeWriteCode(BaseError error) => SafeReadCode(error);

    private static bool IsConcurrency<T>(BaseFailure<T> failure) =>
        failure.Status == OperationStatus.Conflict
        && (failure.Error.Conflict?.Kind is ConflictKind.Revision or ConflictKind.State or ConflictKind.Transaction
            || failure.Error.Code is "base.moduleMutation.authorityChanged"
                or "base.moduleMutation.generationConflict"
                or "base.mutation.request.fingerprintConflict");

    private static IdentityError SafeIdentityError(BaseError error) => new()
    {
        Code = SafeReadCode(error),
        Description = "HPD Auth could not persist the identity record.",
    };

    private static void ThrowIfIndeterminate(BaseError error)
    {
        if (error.Code is "base.moduleMutation.commitIndeterminate"
            or "base.selection.commitIndeterminate"
            or "base.runtime.batch.indeterminate"
            or "base.subject.commitIndeterminate"
            or "base.subjectLifecycle.commitIndeterminate"
            or "base.semanticActivation.commitIndeterminate"
            || error.Code.EndsWith(".indeterminate", StringComparison.OrdinalIgnoreCase))
            throw new AuthBasePersistenceException("auth.persistence.indeterminate");
    }
}
