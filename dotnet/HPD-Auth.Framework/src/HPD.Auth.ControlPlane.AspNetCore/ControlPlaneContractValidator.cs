namespace HPD.Auth.ControlPlane;

internal static class ControlPlaneContractValidator
{
    public static void ValidateConfiguration(HPDControlPlaneOptions options)
    {
        foreach (var profile in options.Profiles.Values)
        {
            RequireCapabilityName(profile.Name, "profile");
            RequireBounded(profile.AuthenticationScheme, 1, 128, "authentication scheme");
            RequireCapabilityName(profile.AuthenticationProfile, "authentication profile");
            if (profile.AuthenticationProfile.Length > 64)
                throw new InvalidOperationException("The authentication profile exceeds 64 characters.");
            RequireBounded(profile.ActorIdentifierClaim, 1, 256, "actor identifier claim");
            ValidateOptional(profile.TenantClaim, 256, "tenant claim");
            ValidateOptional(profile.AuthenticationMethodClaim, 256, "authentication-method claim");
            ValidateOptional(profile.AssuranceClaim, 256, "assurance claim");
            ValidateOptional(profile.RateLimitPolicy, 128, "rate-limit policy");
            ValidateOptional(profile.RequestTimeoutPolicy, 128, "request-timeout policy");
            ValidateOptional(profile.OpenApiSecurityScheme, 128, "OpenAPI security scheme");

            var claimTypes = new[]
            {
                profile.ActorIdentifierClaim,
                profile.TenantClaim,
                profile.AuthenticationMethodClaim,
                profile.AssuranceClaim
            }.Where(static value => value is not null).ToArray();
            if (claimTypes.Distinct(StringComparer.Ordinal).Count() != claimTypes.Length)
                throw new InvalidOperationException("A claim type cannot represent multiple control-plane facts.");
        }

        foreach (var mapping in options.Capabilities)
        {
            RequireCapabilityName(mapping.Key, "capability");
            RequireBounded(mapping.Value, 1, 128, "authorization policy");
            if (!string.Equals(mapping.Value, mapping.Value.Trim(), StringComparison.Ordinal))
                throw new InvalidOperationException("An authorization policy name cannot have surrounding whitespace.");
        }
    }

    private static void RequireCapabilityName(string value, string kind)
    {
        if (value.Length is < 3 or > 128 || !IsEdge(value[0]) || !IsEdge(value[^1]))
            throw new InvalidOperationException($"The control-plane {kind} name is invalid.");

        var previousPeriod = false;
        foreach (var character in value)
        {
            var valid = IsEdge(character) || character is >= 'A' and <= 'Z' || character is '.' or '-';
            if (!valid || (character == '.' && previousPeriod))
                throw new InvalidOperationException($"The control-plane {kind} name is invalid.");
            previousPeriod = character == '.';
        }
    }

    private static bool IsEdge(char value) => value is >= 'a' and <= 'z' or >= '0' and <= '9';

    private static void ValidateOptional(string? value, int maximum, string kind)
    {
        if (value is not null)
            RequireBounded(value, 1, maximum, kind);
    }

    private static void RequireBounded(string value, int minimum, int maximum, string kind)
    {
        if (value.Length < minimum || value.Length > maximum ||
            value.Any(char.IsControl) || string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"The control-plane {kind} is invalid.");
    }
}
