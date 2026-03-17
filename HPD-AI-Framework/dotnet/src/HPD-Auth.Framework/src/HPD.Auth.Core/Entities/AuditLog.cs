using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace HPD.Auth.Core.Entities;

/// <summary>
/// Immutable audit log entry for security and compliance.
/// Once written, audit logs must never be modified or deleted.
/// v2.2: Added InstanceId for multi-tenancy.
/// </summary>
public class AuditLog
{
    /// <summary>
    /// Unique identifier.
    /// </summary>
    public Guid Id { get; init; } = Guid.NewGuid();

    /// <summary>
    /// v2.2: Multi-tenancy discriminator.
    /// </summary>
    public Guid InstanceId { get; init; } = Guid.Empty;

    /// <summary>
    /// When the action occurred (always UTC).
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// User who performed the action. Nullable for pre-authentication events
    /// (e.g., login attempts for unknown users).
    /// </summary>
    public Guid? UserId { get; init; }

    /// <summary>
    /// Specific action performed. Use dot-notation convention.
    /// Examples: "user.login", "user.created", "admin.password_reset"
    /// </summary>
    [MaxLength(100)]
    public string Action { get; init; } = string.Empty;

    /// <summary>
    /// Category grouping for the action.
    /// Values: "authentication", "authorization", "user_management", "admin"
    /// </summary>
    [MaxLength(50)]
    public string Category { get; init; } = string.Empty;

    /// <summary>
    /// Client IP address at the time of the action.
    /// </summary>
    [MaxLength(45)]
    public string? IpAddress { get; init; }

    /// <summary>
    /// User agent string from the request.
    /// </summary>
    [MaxLength(1024)]
    public string? UserAgent { get; init; }

    /// <summary>
    /// Whether the action succeeded.
    /// </summary>
    public bool Success { get; init; } = true;

    /// <summary>
    /// Error message or failure reason if Success is false.
    /// </summary>
    [MaxLength(2000)]
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Additional context as a JSON blob for structured extra data.
    /// Examples: IP geo-location, device fingerprint, affected resource ID.
    /// </summary>
    [Column(TypeName = "jsonb")]
    public string Metadata { get; init; } = "{}";
}

/// <summary>
/// Standard audit category values.
/// </summary>
public static class AuditCategories
{
    public const string Authentication = "authentication";
    public const string Authorization = "authorization";
    public const string UserManagement = "user_management";
    public const string Admin = "admin";
}

/// <summary>
/// Standard audit action name constants using dot-notation convention.
/// </summary>
public static class AuditActions
{
    // Authentication
    public const string UserLogin = "user.login";
    public const string UserLoginFailed = "user.login.failed";
    public const string UserLogout = "user.logout";
    public const string TokenRefresh = "token.refresh";
    public const string TokenRefreshFailed = "token.refresh.failed";

    // Registration
    public const string UserRegister = "user.register";
    public const string EmailConfirm = "email.confirm";
    public const string EmailConfirmResend = "email.confirm.resend";

    // Password
    public const string PasswordChange = "password.change";
    public const string PasswordResetRequest = "password.reset.request";
    public const string PasswordReset = "password.reset";

    // 2FA
    public const string TwoFactorSetup = "2fa.setup";
    public const string TwoFactorEnable = "2fa.enable";
    public const string TwoFactorDisable = "2fa.disable";
    public const string TwoFactorVerify = "2fa.verify";
    public const string TwoFactorVerifyFailed = "2fa.verify.failed";
    public const string RecoveryCodeUse = "recovery.code.use";
    public const string RecoveryCodeRegenerate = "recovery.code.regenerate";

    // Passkey
    public const string PasskeyRegister = "passkey.register";
    public const string PasskeyAuthenticate = "passkey.authenticate";
    public const string PasskeyAuthenticateFailed = "passkey.authenticate.failed";
    public const string PasskeyDelete = "passkey.delete";

    // OAuth
    public const string OAuthLogin = "oauth.login";
    public const string OAuthLink = "oauth.link";
    public const string OAuthUnlink = "oauth.unlink";

    // Session
    public const string SessionCreate = "session.create";
    public const string SessionRevoke = "session.revoke";
    public const string SessionRevokeAll = "session.revoke.all";

    // Security
    public const string AccountLockout = "account.lockout";
    public const string AccountUnlock = "account.unlock";
    public const string SecurityStampChange = "security.stamp.change";

    // Admin
    public const string AdminUserView = "admin.user.view";
    public const string AdminUserUpdate = "admin.user.update";
    public const string AdminUserDisable = "admin.user.disable";
    public const string AdminUserEnable = "admin.user.enable";
    public const string AdminUserDelete = "admin.user.delete";
    public const string AdminRoleAssign = "admin.role.assign";
    public const string AdminRoleRemove = "admin.role.remove";
    public const string AdminForceLogout = "admin.force.logout";
    public const string AdminPasswordReset = "admin.password_reset";
}
