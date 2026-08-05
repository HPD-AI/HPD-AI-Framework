using System.Collections.Immutable;
using HPD.Auth.Core.Audit;

namespace HPD.Auth.Admin;

internal enum AdminAuditOperation
{
    UserList, UserCount, UserView, UserCreate, UserUpdate, UserDelete,
    UserBan, UserUnban, UserUnlock, UserVerifyEmail, UserEnable, UserDisable,
    RoleList, RoleAdd, RoleRemove, ClaimList, ClaimAdd, ClaimRemove, ClaimReplace,
    LoginList, LoginRemove, PasswordReset, PasswordRemove, PasswordAdd,
    TwoFactorView, TwoFactorDisable, TwoFactorResetAuthenticator,
    TwoFactorGenerateRecoveryCodes, LinkGenerate, SessionList, SessionInvalidate,
    SessionRevokeAll, SessionRevoke, AuditList, AuditUserList
}

internal enum AdminAuditFailure
{
    InvalidRequest, SubjectNotFound, IdentityConflict, AuthorizationRoleNotFound,
    AuthorizationConflict, CredentialPolicyRejected, CredentialInvalid,
    OperationDenied, StoreFailed, OperationFailed
}

internal static class AdminAuditMapper
{
    public static ValueTask WriteAsync(
        IAuthAuditWriter writer,
        AdminAuditOperation operation,
        IAuthCorrelationContext correlationContext,
        Guid? subjectUserId = null,
        Guid? subjectSessionId = null,
        AdminAuditFailure? failure = null,
        CancellationToken cancellationToken = default)
    {
        var write = new AuthAuditWrite(
            Action(operation),
            "admin",
            failure is null,
            subjectUserId,
            subjectSessionId,
            null,
            null,
            failure is null ? null : Failure(failure.Value),
            correlationContext.CorrelationId is { } correlationId
                ? new string(correlationId.AsSpan())
                : null,
            ImmutableArray<AuthAuditFact>.Empty);
        return writer.WriteAsync(write, cancellationToken);
    }

    private static string Action(AdminAuditOperation operation) => operation switch
    {
        AdminAuditOperation.UserList => "admin.user.list",
        AdminAuditOperation.UserCount => "admin.user.count",
        AdminAuditOperation.UserView => "admin.user.view",
        AdminAuditOperation.UserCreate => "admin.user.create",
        AdminAuditOperation.UserUpdate => "admin.user.update",
        AdminAuditOperation.UserDelete => "admin.user.delete",
        AdminAuditOperation.UserBan => "admin.user.ban",
        AdminAuditOperation.UserUnban => "admin.user.unban",
        AdminAuditOperation.UserUnlock => "admin.user.unlock",
        AdminAuditOperation.UserVerifyEmail => "admin.user.verify-email",
        AdminAuditOperation.UserEnable => "admin.user.enable",
        AdminAuditOperation.UserDisable => "admin.user.disable",
        AdminAuditOperation.RoleList => "admin.role.list",
        AdminAuditOperation.RoleAdd => "admin.role.add",
        AdminAuditOperation.RoleRemove => "admin.role.remove",
        AdminAuditOperation.ClaimList => "admin.claim.list",
        AdminAuditOperation.ClaimAdd => "admin.claim.add",
        AdminAuditOperation.ClaimRemove => "admin.claim.remove",
        AdminAuditOperation.ClaimReplace => "admin.claim.replace",
        AdminAuditOperation.LoginList => "admin.login.list",
        AdminAuditOperation.LoginRemove => "admin.login.remove",
        AdminAuditOperation.PasswordReset => "admin.password.reset",
        AdminAuditOperation.PasswordRemove => "admin.password.remove",
        AdminAuditOperation.PasswordAdd => "admin.password.add",
        AdminAuditOperation.TwoFactorView => "admin.2fa.view",
        AdminAuditOperation.TwoFactorDisable => "admin.2fa.disable",
        AdminAuditOperation.TwoFactorResetAuthenticator => "admin.2fa.reset-authenticator",
        AdminAuditOperation.TwoFactorGenerateRecoveryCodes => "admin.2fa.generate-recovery-codes",
        AdminAuditOperation.LinkGenerate => "admin.link.generate",
        AdminAuditOperation.SessionList => "admin.session.list",
        AdminAuditOperation.SessionInvalidate => "admin.session.invalidate",
        AdminAuditOperation.SessionRevokeAll => "admin.session.revoke-all",
        AdminAuditOperation.SessionRevoke => "admin.session.revoke",
        AdminAuditOperation.AuditList => "admin.audit.list",
        AdminAuditOperation.AuditUserList => "admin.audit.user-list",
        _ => throw new ArgumentOutOfRangeException(nameof(operation))
    };

    private static string Failure(AdminAuditFailure failure) => failure switch
    {
        AdminAuditFailure.InvalidRequest => "admin.invalid-request",
        AdminAuditFailure.SubjectNotFound => "admin.subject-not-found",
        AdminAuditFailure.IdentityConflict => "admin.identity-conflict",
        AdminAuditFailure.AuthorizationRoleNotFound => "admin.authorization-role-not-found",
        AdminAuditFailure.AuthorizationConflict => "admin.authorization-conflict",
        AdminAuditFailure.CredentialPolicyRejected => "admin.credential-policy-rejected",
        AdminAuditFailure.CredentialInvalid => "admin.credential-invalid",
        AdminAuditFailure.OperationDenied => "admin.operation-denied",
        AdminAuditFailure.StoreFailed => "admin.store-failed",
        AdminAuditFailure.OperationFailed => "admin.operation-failed",
        _ => throw new ArgumentOutOfRangeException(nameof(failure))
    };
}
