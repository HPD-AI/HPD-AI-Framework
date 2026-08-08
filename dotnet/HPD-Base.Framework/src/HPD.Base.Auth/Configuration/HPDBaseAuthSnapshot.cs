namespace HPD.Base.Auth;

internal sealed class HPDBaseAuthSnapshot
{
    internal required bool RequireAuthenticatedByDefault { get; init; }
    internal required bool AllowAdminBypass { get; init; }
    internal required bool AllowServiceBypass { get; init; }
    internal required bool RequireHPDAuthServices { get; init; }
    internal required HPDBaseAuthPolicyCompositionMode PolicyCompositionMode { get; init; }
    internal required string[] AdminRoleNames { get; init; }
    internal required string TenantClaimType { get; init; }
    internal required string SubscriptionTierClaimType { get; init; }
    internal required string SessionIdClaimType { get; init; }
    internal required string? CredentialIdClaimType { get; init; }
    internal required string[] SubjectIdClaimTypes { get; init; }
    internal required string[] DisplayNameClaimTypes { get; init; }
    internal required string[] RoleClaimTypes { get; init; }
    internal required string[] ServicePrincipalClaimTypes { get; init; }
    internal required string[] CopiedClaimTypes { get; init; }
    internal required int MaxClaims { get; init; }
    internal required int MaxRoles { get; init; }
    internal required bool UseTenantContextFallbackForApplicationEndpoints { get; init; }
    internal required bool EnrichFromUserManager { get; init; }
    internal required HPDBaseAuthCollectionRule[] CollectionRules { get; init; }
    internal required AccessGrant[] StaticGrants { get; init; }

    internal static HPDBaseAuthSnapshot Create(HPDBaseAuthOptions source) => new()
    {
        RequireAuthenticatedByDefault = source.RequireAuthenticatedByDefault,
        AllowAdminBypass = source.AllowAdminBypass,
        AllowServiceBypass = source.AllowServiceBypass,
        RequireHPDAuthServices = source.RequireHPDAuthServices,
        PolicyCompositionMode = source.PolicyCompositionMode,
        AdminRoleNames = [.. source.AdminRoleNames],
        TenantClaimType = Copy(source.TenantClaimType),
        SubscriptionTierClaimType = Copy(source.SubscriptionTierClaimType),
        SessionIdClaimType = Copy(source.SessionIdClaimType),
        CredentialIdClaimType = source.CredentialIdClaimType is null ? null : Copy(source.CredentialIdClaimType),
        SubjectIdClaimTypes = [.. source.SubjectIdClaimTypes],
        DisplayNameClaimTypes = [.. source.DisplayNameClaimTypes],
        RoleClaimTypes = [.. source.RoleClaimTypes],
        ServicePrincipalClaimTypes = [.. source.ServicePrincipalClaimTypes],
        CopiedClaimTypes = [.. source.CopiedClaimTypes],
        MaxClaims = source.MaxClaims,
        MaxRoles = source.MaxRoles,
        UseTenantContextFallbackForApplicationEndpoints = source.UseTenantContextFallbackForApplicationEndpoints,
        EnrichFromUserManager = source.EnrichFromUserManager,
        CollectionRules = source.CollectionRules.Select(CloneRule).ToArray(),
        StaticGrants = source.StaticGrants.Select(CloneGrant).ToArray()
    };

    private static HPDBaseAuthCollectionRule CloneRule(HPDBaseAuthCollectionRule rule) => rule with
    {
        CollectionId = Copy(rule.CollectionId), TenantFieldId = CopyNullable(rule.TenantFieldId),
        ReadRoles = Clone(rule.ReadRoles), WriteRoles = Clone(rule.WriteRoles),
        ReadIncludeFields = Clone(rule.ReadIncludeFields), ReadExcludeFields = Clone(rule.ReadExcludeFields),
        WriteIncludeFields = Clone(rule.WriteIncludeFields), WriteExcludeFields = Clone(rule.WriteExcludeFields)
    };

    private static AccessGrant CloneGrant(AccessGrant grant) => grant with
    {
        Id = Copy(grant.Id), Action = Copy(grant.Action), Source = CopyNullable(grant.Source),
        Subject = grant.Subject with { Id = CopyNullable(grant.Subject.Id), Qualifier = CopyNullable(grant.Subject.Qualifier), TenantId = CopyNullable(grant.Subject.TenantId), Source = CopyNullable(grant.Subject.Source) },
        Scope = grant.Scope with { CollectionId = CopyNullable(grant.Scope.CollectionId), RecordId = CopyNullable(grant.Scope.RecordId), FieldPath = CopyNullable(grant.Scope.FieldPath), TenantId = CopyNullable(grant.Scope.TenantId) },
        Condition = CloneFilter(grant.Condition), WriteCondition = CloneFilter(grant.WriteCondition)
    };

    private static FilterExpression? CloneFilter(FilterExpression? filter) => filter is null ? null : filter with
    {
        Field = CopyNullable(filter.Field), ModuleId = CopyNullable(filter.ModuleId), Name = CopyNullable(filter.Name),
        Value = CloneQueryValue(filter.Value),
        Values = filter.Values?.Select(CloneQueryValueRequired).ToArray(),
        Arguments = filter.Arguments?.Select(CloneQueryValueRequired).ToArray(),
        Children = filter.Children?.Select(child => CloneFilter(child)!).ToArray()
    };
    private static QueryValue? CloneQueryValue(QueryValue? value) => value is null ? null : CloneQueryValueRequired(value);
    private static QueryValue CloneQueryValueRequired(QueryValue value) => value with
    {
        String = CopyNullable(value.String), Decimal = CopyNullable(value.Decimal), Id = CopyNullable(value.Id),
        Array = value.Array?.Select(CloneQueryValueRequired).ToArray()
    };
    private static string[]? Clone(string[]? values) => values?.Select(Copy).ToArray();
    private static string Copy(string value) => new(value.AsSpan());
    private static string? CopyNullable(string? value) => value is null ? null : Copy(value);
}
