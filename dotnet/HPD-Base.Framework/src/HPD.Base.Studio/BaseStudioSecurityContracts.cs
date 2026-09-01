namespace HPD.Base.Studio;

internal static class BaseStudioSecurityContracts
{
    internal static bool IsRegistrationView(string viewId) =>
        (viewId.StartsWith("base.security.", StringComparison.Ordinal) ||
         viewId.StartsWith("base.policy.detail.", StringComparison.Ordinal) ||
         viewId.StartsWith("base.grant.detail.", StringComparison.Ordinal) ||
         viewId.StartsWith("base.policy.explain.", StringComparison.Ordinal));

    internal static string ItemDescriptor(string viewId)
    {
        string[] fields = Fields(viewId);
        return $"{{\"kind\":\"object\",\"properties\":[{string.Join(',', fields.Order(StringComparer.Ordinal).Select(Property))}],\"additionalProperties\":false}}";
    }

    internal static string[] Fields(string viewId) => viewId switch
    {
        "base.security.policies.list" => ["policyId", "version", "owningModuleId", "compositionOrder", "registrationChecksum"],
        "base.security.grants.list" => ["grantId", "version", "owningModuleId", "sourceContractId", "registrationChecksum"],
        "base.security.explanations.list" => ["queryKind", "requiredOperation", "authorityClass", "maximumResultBytes", "registrationChecksum"],
        "base.security.disclosure.detail" => ["policyOwnerGeneration", "policyOwnerChecksum", "disclosureClass", "nativeConditionsExposed", "registrationChecksum"],
        "base.policy.detail.summary.detail" => ["policyId", "version", "owningModuleId", "evaluatorContractId", "registrationChecksum"],
        "base.policy.detail.composition.detail" => ["policyId", "version", "compositionOrder", "policyOwnerGeneration", "registrationChecksum"],
        "base.policy.detail.constraints.detail" => ["policyId", "version", "constraintAuthority", "queryRequired", "registrationChecksum"],
        "base.policy.detail.masks.detail" => ["policyId", "version", "maskAuthority", "queryRequired", "registrationChecksum"],
        "base.policy.detail.obligations.detail" => ["policyId", "version", "obligationAuthority", "evaluatorContractId", "registrationChecksum"],
        "base.policy.detail.history.list" => ["policyId", "version", "policyOwnerGeneration", "policyOwnerChecksum", "registrationChecksum"],
        "base.grant.detail.summary.detail" => ["grantId", "version", "owningModuleId", "sourceContractId", "registrationChecksum"],
        "base.grant.detail.scope.detail" => ["grantId", "version", "subjectKind", "subjectId", "audience", "registrationChecksum"],
        "base.grant.detail.operations.list" => ["grantId", "version", "action", "effect", "registrationChecksum"],
        "base.grant.detail.conditions.detail" => ["grantId", "version", "staticSemantics", "readCondition", "writeCondition", "registrationChecksum"],
        "base.grant.detail.history.list" => ["grantId", "version", "policyOwnerGeneration", "policyOwnerChecksum", "registrationChecksum"],
        "base.policy.explain.operation.detail" => ["operationId", "effect", "outcome", "evaluationChecksum"],
        "base.policy.explain.resource.detail" => ["targetResourceKind", "targetResourceTokenChecksum", "effect", "evaluationChecksum"],
        "base.policy.explain.filters.list" => ["filterKind", "filterPresent", "authorityClass", "evaluationChecksum"],
        "base.policy.explain.constraints.detail" => ["effect", "outcome", "constraintCount", "evaluationChecksum"],
        "base.policy.explain.masks.detail" => ["readMaskPresent", "writeMaskPresent", "authorityClass", "evaluationChecksum"],
        "base.policy.explain.disclosure.detail" => ["reasonCode", "safeMessageAvailable", "nativeAuditExposed", "evaluationChecksum"],
        "base.policy.explain.decision.detail" => ["effect", "outcome", "policyOwnerGeneration", "evaluationChecksum"],
        _ => throw new InvalidOperationException("base.studio.securityViewUnknown"),
    };

    private static string Property(string name)
    {
        string type = name.EndsWith("Checksum", StringComparison.Ordinal) ? "base.studio.sha256" :
            name is "version" or "compositionOrder" or "policyOwnerGeneration" or "maximumResultBytes" or "constraintCount" ? "base.studio.nonnegative-long" : "base.studio.text";
        return $"{{\"name\":\"{name}\",\"wireName\":\"{name}\",\"typeId\":\"{type}\",\"required\":true,\"nullable\":false,\"disclosureShape\":\"none\"}}";
    }
}
