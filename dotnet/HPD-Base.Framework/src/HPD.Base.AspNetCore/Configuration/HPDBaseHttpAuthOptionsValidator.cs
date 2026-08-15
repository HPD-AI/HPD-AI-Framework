namespace HPD.Base.AspNetCore;

internal static class HPDBaseHttpAuthOptionsValidator
{
    internal static void ValidateAndFreeze(HPDBaseHttpAuthOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.MaxClaims is < 0 or > 64 || options.MaxRoles is < 0 or > 64)
            throw new ArgumentOutOfRangeException(nameof(options), "Claim and role limits must be between zero and 64.");
        options.SubjectIdClaimTypes = Copy(options.SubjectIdClaimTypes, nameof(options.SubjectIdClaimTypes));
        options.DisplayNameClaimTypes = Copy(options.DisplayNameClaimTypes, nameof(options.DisplayNameClaimTypes));
        options.RoleClaimTypes = Copy(options.RoleClaimTypes, nameof(options.RoleClaimTypes));
        options.ServicePrincipalClaimTypes = Copy(options.ServicePrincipalClaimTypes, nameof(options.ServicePrincipalClaimTypes));
        options.AdminRoleNames = Copy(options.AdminRoleNames, nameof(options.AdminRoleNames));
        options.CopiedClaimTypes = Copy(options.CopiedClaimTypes, nameof(options.CopiedClaimTypes));
        options.TenantIdClaimType = CopyOptional(options.TenantIdClaimType, nameof(options.TenantIdClaimType));
        options.TenantMembershipClaimType = CopyOptional(options.TenantMembershipClaimType, nameof(options.TenantMembershipClaimType));
        options.SessionIdClaimType = CopyOptional(options.SessionIdClaimType, nameof(options.SessionIdClaimType));
        string[] forbidden = ["token", "secret", "password", "credential", "authorization", "securitystamp", "recovery"];
        if (options.CopiedClaimTypes.Any(type => forbidden.Any(fragment => type.Contains(fragment, StringComparison.OrdinalIgnoreCase))))
            throw new ArgumentException("Copied claim types contain a forbidden credential family.", nameof(options));
    }

    private static string[] Copy(string[]? values, string name)
    {
        if (values is null) throw new ArgumentNullException(name);
        string[] copy = values.Select(value => new string(value.AsSpan())).ToArray();
        if (copy.Any(value => string.IsNullOrWhiteSpace(value) || System.Text.Encoding.UTF8.GetByteCount(value) > 128 || value.Any(char.IsControl)) ||
            copy.Distinct(StringComparer.Ordinal).Count() != copy.Length)
            throw new ArgumentException("Values must be distinct bounded visible strings.", name);
        return copy;
    }

    private static string? CopyOptional(string? value, string name)
    {
        if (value is null) return null;
        string copy = new(value.AsSpan());
        if (string.IsNullOrWhiteSpace(copy) || System.Text.Encoding.UTF8.GetByteCount(copy) > 128 || copy.Any(char.IsControl))
            throw new ArgumentException("Claim type must be a bounded visible string.", name);
        return copy;
    }
}
