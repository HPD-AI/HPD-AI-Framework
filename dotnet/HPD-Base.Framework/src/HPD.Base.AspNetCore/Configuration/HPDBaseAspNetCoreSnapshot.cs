namespace HPD.Base.AspNetCore;

internal sealed class HPDBaseAspNetCoreSnapshot
{
    internal required HPDBaseProblemDetailsOptions ProblemDetails { get; init; }
    internal required HPDBaseHttpAuthOptions Auth { get; init; }
    internal required HPDBaseHttpRequestContextOptions RequestContext { get; init; }
    internal required HPDBaseHttpLimitOptions Limits { get; init; }
    internal required HPDBaseAdministrationHttpSnapshot Administration { get; init; }

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
        },
        Administration = HPDBaseAdministrationHttpSnapshot.Create(source.Administration)
    };

    private static string? Copy(string? value) => value is null ? null : new string(value.AsSpan());
}

internal sealed record HPDBaseAdministrationHttpSnapshot
{
    internal required string? StagingRoot { get; init; }
    internal required long MaxArtifactBytes { get; init; }
    internal required int MaxConcurrentStaging { get; init; }
    internal required TimeSpan CleanupTimeout { get; init; }

    internal static HPDBaseAdministrationHttpSnapshot Create(HPDBaseAdministrationHttpOptions source)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (source.MaxArtifactBytes is < 1024L * 1024 or > 1024L * 1024 * 1024 * 1024)
            throw new ArgumentOutOfRangeException(nameof(source.MaxArtifactBytes));
        if (source.MaxConcurrentStaging is < 1 or > 8)
            throw new ArgumentOutOfRangeException(nameof(source.MaxConcurrentStaging));
        if (source.CleanupTimeout < TimeSpan.FromMilliseconds(100) || source.CleanupTimeout > TimeSpan.FromMinutes(1))
            throw new ArgumentOutOfRangeException(nameof(source.CleanupTimeout));
        if (source.StagingRoot is not null && !Path.IsPathFullyQualified(source.StagingRoot))
            throw new ArgumentException("The administration staging root must be absolute.", nameof(source.StagingRoot));
        string? root = source.StagingRoot is null ? null : Path.GetFullPath(source.StagingRoot);
        return new HPDBaseAdministrationHttpSnapshot
        {
            StagingRoot = root is null ? null : new string(root.AsSpan()),
            MaxArtifactBytes = source.MaxArtifactBytes,
            MaxConcurrentStaging = source.MaxConcurrentStaging,
            CleanupTimeout = source.CleanupTimeout
        };
    }
}
