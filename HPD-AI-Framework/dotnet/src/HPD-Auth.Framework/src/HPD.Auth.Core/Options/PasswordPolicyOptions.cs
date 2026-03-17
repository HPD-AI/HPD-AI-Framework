namespace HPD.Auth.Core.Options;

/// <summary>
/// Password complexity and history policy options.
/// These map to ASP.NET Identity's PasswordOptions and are applied
/// via TenantAwarePasswordValidator.
/// </summary>
public class PasswordPolicyOptions
{
    /// <summary>
    /// Minimum number of characters required. Defaults to 8.
    /// </summary>
    public int RequiredLength { get; set; } = 8;

    /// <summary>
    /// Whether the password must contain at least one digit (0-9).
    /// </summary>
    public bool RequireDigit { get; set; } = true;

    /// <summary>
    /// Whether the password must contain at least one uppercase letter.
    /// </summary>
    public bool RequireUppercase { get; set; } = true;

    /// <summary>
    /// Whether the password must contain at least one lowercase letter.
    /// </summary>
    public bool RequireLowercase { get; set; } = true;

    /// <summary>
    /// Whether the password must contain at least one non-alphanumeric character
    /// (e.g., !, @, #, $).
    /// </summary>
    public bool RequireNonAlphanumeric { get; set; } = true;

    /// <summary>
    /// Minimum number of unique characters in the password.
    /// </summary>
    public int RequiredUniqueChars { get; set; } = 1;

    /// <summary>
    /// Number of previous passwords to remember and block reuse of.
    /// Set to 0 to disable password history checks.
    /// </summary>
    public int PasswordHistoryCount { get; set; } = 5;

    /// <summary>
    /// Maximum password age in days. 0 means passwords never expire.
    /// </summary>
    public int MaxPasswordAgeDays { get; set; } = 0;
}
