namespace HPD.Base.AspNetCore;

internal sealed class HPDBaseAspNetCoreSnapshot
{
    internal required HPDBaseProblemDetailsOptions ProblemDetails { get; init; }
    internal required HPDBaseHttpAuthOptions Auth { get; init; }
    internal required HPDBaseHttpRequestContextOptions RequestContext { get; init; }
    internal required HPDBaseHttpLimitOptions Limits { get; init; }

    internal static HPDBaseAspNetCoreSnapshot Create(HPDBaseAspNetCoreOptions source) => new()
    {
        ProblemDetails = new HPDBaseProblemDetailsOptions
        {
            IncludeSafeDiagnostics = source.ProblemDetails.IncludeSafeDiagnostics,
            IncludeWarnings = source.ProblemDetails.IncludeWarnings
        },
        Auth = new HPDBaseHttpAuthOptions
        {
            SubjectIdClaimTypes = [.. source.Auth.SubjectIdClaimTypes],
            DisplayNameClaimTypes = [.. source.Auth.DisplayNameClaimTypes],
            RoleClaimTypes = [.. source.Auth.RoleClaimTypes],
            TenantIdClaimType = Copy(source.Auth.TenantIdClaimType),
            TenantMembershipClaimType = Copy(source.Auth.TenantMembershipClaimType),
            SessionIdClaimType = Copy(source.Auth.SessionIdClaimType),
            AdminRoleNames = [.. source.Auth.AdminRoleNames],
            ServicePrincipalClaimTypes = [.. source.Auth.ServicePrincipalClaimTypes],
            MaxClaims = source.Auth.MaxClaims,
            MaxRoles = source.Auth.MaxRoles,
            CopiedClaimTypes = [.. source.Auth.CopiedClaimTypes]
        },
        RequestContext = new HPDBaseHttpRequestContextOptions
        {
            IncludeIpAddress = source.RequestContext.IncludeIpAddress,
            IncludeUserAgent = source.RequestContext.IncludeUserAgent,
            MaxClientMetadataLength = source.RequestContext.MaxClientMetadataLength,
            CorrelationIdHeaderName = Copy(source.RequestContext.CorrelationIdHeaderName)!,
            ClientNameHeaderName = Copy(source.RequestContext.ClientNameHeaderName)!,
            ClientVersionHeaderName = Copy(source.RequestContext.ClientVersionHeaderName)!
        },
        Limits = new HPDBaseHttpLimitOptions
        {
            MaxQueryStringLength = source.Limits.MaxQueryStringLength,
            MaxFilterLength = source.Limits.MaxFilterLength,
            MaxRouteIdLength = source.Limits.MaxRouteIdLength,
            MaxRepeatedParameterValues = source.Limits.MaxRepeatedParameterValues,
            MaxQueryListItems = source.Limits.MaxQueryListItems,
            MaxRequestBodyLength = source.Limits.MaxRequestBodyLength
        }
    };

    private static string? Copy(string? value) => value is null ? null : new string(value.AsSpan());
}
