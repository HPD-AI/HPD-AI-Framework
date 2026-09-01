namespace HPD.Auth.Base;

/// <summary>Owns the closed validation boundary used by the internal reconciliation projection.</summary>
internal static class AuthReconciliationProjectionContract
{
    internal const string InvalidRequest = "auth.cleanup.reconciliationRequestInvalid";
    internal const string InvalidResult = "auth.cleanup.reconciliationResultInvalid";

    internal static void ValidateRequest(
        Guid? afterTenantId,
        AuthCleanupSubjectKindV1? afterSubjectKind,
        Guid? afterSubjectId,
        int take)
    {
        bool any = afterTenantId.HasValue || afterSubjectKind.HasValue || afterSubjectId.HasValue;
        bool all = afterTenantId.HasValue && afterSubjectKind.HasValue && afterSubjectId.HasValue;
        if (take is < 1 or > 200 || any != all)
            throw new InvalidOperationException(InvalidRequest);
    }

    internal static DateTimeOffset RequireTombstonedAt(DateTimeOffset? value) =>
        value ?? throw new InvalidOperationException(InvalidResult);
}
