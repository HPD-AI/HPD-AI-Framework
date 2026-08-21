
using System.Security.Cryptography;

namespace HPD.Base;

internal sealed class DefaultBaseSchemaProvider : IBaseSchemaProvider
{
    private readonly IBaseDescriptorRegistry _registry;
    private readonly IBasePolicyOrchestrator _policy;

    /// <summary>Initializes a new instance.</summary>
    public DefaultBaseSchemaProvider(IBaseDescriptorRegistry registry, IBasePolicyOrchestrator policy)
    {
        _registry = registry;
        _policy = policy;
    }

    /// <summary>Executes the get schema async operation.</summary>
    public ValueTask<OperationResult<SchemaMetadata>> GetSchemaAsync(
        PrincipalContext principal,
        OperationContext operation,
        VisibilityLevel view,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = principal;
        return HPDBaseRuntimeTelemetry.TraceRuntimeReadAsync(
            HPDBaseTelemetrySpans.RuntimeSchemaGet,
            BaseOperationKind.SchemaRead,
            operation.CollectionId,
            view,
            !string.IsNullOrWhiteSpace(operation.CorrelationId),
            countAsHealthRead: false,
            countAsDiagnosticRead: false,
            () => ValueTask.FromResult(OperationResults.Ok(DescriptorViewFilter.Schema(_registry.Current, view))));
    }

    /// <summary>Executes the get collection async operation.</summary>
    public async ValueTask<OperationResult<CollectionDefinition>> GetCollectionAsync(
        string collectionId,
        PrincipalContext principal,
        OperationContext operation,
        VisibilityLevel view,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _ = principal;
        return await HPDBaseRuntimeTelemetry.TraceRuntimeReadAsync(
            HPDBaseTelemetrySpans.RuntimeSchemaCollectionGet,
            BaseOperationKind.SchemaRead,
            collectionId,
            view,
            !string.IsNullOrWhiteSpace(operation.CorrelationId),
            countAsHealthRead: false,
            countAsDiagnosticRead: false,
            async () =>
            {
                CollectionDefinition? authoritative = _registry.Current.Schema.Collections?.FirstOrDefault(
                    item => string.Equals(item.Id, collectionId, StringComparison.Ordinal));
                if (authoritative?.System == true)
                {
                    if (!BaseSystemCollectionGate.Allows(principal))
                        return OperationResults.NotFound<CollectionDefinition>(new BaseError
                        {
                            Code = "base.systemCollection.accessForbidden",
                            Message = "Collection was not found.",
                            Category = ErrorCategory.NotFound
                        });
                    OperationResult<BasePolicyEvaluation> authorization = await _policy.EvaluateReadAsync(new BasePolicyRequest
                    {
                        Principal = principal,
                        Operation = operation with { CollectionId = collectionId },
                        Collection = authoritative,
                        ResourceKind = PolicyResourceKind.Schema,
                    }, cancellationToken).ConfigureAwait(false);
                    if (!BaseSystemCollectionGate.HasExactGrant(authorization))
                        return OperationResults.NotFound<CollectionDefinition>(new BaseError
                        {
                            Code = "base.systemCollection.accessForbidden",
                            Message = "Collection was not found.",
                            Category = ErrorCategory.NotFound
                        });
                }

                CollectionDefinition? collection = authoritative is null ? null : DescriptorViewFilter.Collection(authoritative, view);
                return collection is null
                    ? OperationResults.NotFound<CollectionDefinition>(new BaseError
                    {
                        Code = "base.runtime.collection.notFound",
                        Message = "Collection was not found.",
                        Category = ErrorCategory.NotFound,
                        Target = collectionId
                    })
                    : OperationResults.Ok(collection);
            }).ConfigureAwait(false);
    }
}

internal static class BaseSystemCollectionGate
{
    internal static bool Allows(PrincipalContext principal) =>
        principal.AuthenticationState is PrincipalAuthenticationState.Service or PrincipalAuthenticationState.System
        && principal.SubjectKind is AccessSubjectKind.ServicePrincipal or AccessSubjectKind.System;

    internal static bool HasExactGrant(OperationResult<BasePolicyEvaluation> result, string? requiredGrantId = null)
    {
        if (!result.IsSuccess() || result.Value?.Authority?.AdmittedGrants is not { Length: > 0 } grants)
            return false;
        return requiredGrantId is null || grants.Any(grant => string.Equals(grant.GrantId, requiredGrantId, StringComparison.Ordinal));
    }

    internal static bool HasExactModuleGrant(
        OperationResult<BasePolicyEvaluation> result,
        string requiredGrantId,
        string owningModuleId,
        PrincipalContext principal,
        OperationContext operation)
    {
        if (!result.IsSuccess() || result.Value?.Authority is not { } authority)
            return false;
        return authority.GrantSemantics.Any(semantics =>
        {
            AccessGrant grant = semantics.Grant;
            return ReceiptMatches(authority, semantics)
                && string.Equals(semantics.GrantId, requiredGrantId, StringComparison.Ordinal)
                && string.Equals(grant.Id, requiredGrantId, StringComparison.Ordinal)
                && grant.Effect == GrantEffect.Allow
                && string.Equals(grant.ApplicationId, operation.ApplicationId, StringComparison.Ordinal)
                && string.Equals(grant.ModuleId, owningModuleId, StringComparison.Ordinal)
                && grant.Audience == operation.Audience
                && string.Equals(grant.Action, operation.CollectionId, StringComparison.Ordinal)
                && grant.Subject.Kind == principal.SubjectKind
                && string.Equals(grant.Subject.Id, principal.SubjectId, StringComparison.Ordinal)
                && string.Equals(grant.Subject.TenantId, principal.CurrentTenantId, StringComparison.Ordinal)
                && grant.Scope.Kind == ResourceScopeKind.Runtime
                && grant.Scope.CollectionId is null && grant.Scope.RecordId is null
                && grant.Scope.FieldPath is null && grant.Scope.VectorIndexId is null && grant.Scope.TextIndexId is null
                && grant.Scope.SubjectContractId is null && grant.Scope.SubjectContractVersion is null
                && string.Equals(grant.Scope.TenantId, operation.TenantId, StringComparison.Ordinal)
                && string.Equals(grant.Scope.ProjectId, operation.ProjectId, StringComparison.Ordinal)
                && grant.Condition is null && grant.WriteCondition is null
                && (grant.ExpiresAt is null || grant.ExpiresAt > operation.Now);
        });
    }

    internal static bool HasExactModuleSourceGrant(
        OperationResult<BasePolicyEvaluation> result,
        string requiredGrantId,
        string owningModuleId,
        PrincipalContext principal,
        OperationContext operation,
        string collectionId)
    {
        if (!result.IsSuccess() || result.Value?.Authority is not { } authority)
            return false;
        return authority.GrantSemantics.Any(semantics =>
        {
            AccessGrant grant = semantics.Grant;
            return ReceiptMatches(authority, semantics)
                && string.Equals(semantics.GrantId, requiredGrantId, StringComparison.Ordinal)
                && string.Equals(grant.Id, requiredGrantId, StringComparison.Ordinal)
                && grant.Effect == GrantEffect.Allow
                && string.Equals(grant.ApplicationId, operation.ApplicationId, StringComparison.Ordinal)
                && string.Equals(grant.ModuleId, owningModuleId, StringComparison.Ordinal)
                && grant.Audience == operation.Audience
                && string.Equals(grant.Action, collectionId, StringComparison.Ordinal)
                && grant.Subject.Kind == principal.SubjectKind
                && string.Equals(grant.Subject.Id, principal.SubjectId, StringComparison.Ordinal)
                && string.Equals(grant.Subject.TenantId, principal.CurrentTenantId, StringComparison.Ordinal)
                && grant.Scope.Kind == ResourceScopeKind.Collection
                && string.Equals(grant.Scope.CollectionId, collectionId, StringComparison.Ordinal)
                && grant.Scope.RecordId is null && grant.Scope.FieldPath is null && grant.Scope.VectorIndexId is null && grant.Scope.TextIndexId is null
                && grant.Scope.SubjectContractId is null && grant.Scope.SubjectContractVersion is null
                && string.Equals(grant.Scope.TenantId, operation.TenantId, StringComparison.Ordinal)
                && string.Equals(grant.Scope.ProjectId, operation.ProjectId, StringComparison.Ordinal)
                && grant.Condition is null && grant.WriteCondition is null
                && (grant.ExpiresAt is null || grant.ExpiresAt > operation.Now);
        });
    }

    internal static bool HasExactTextGrant(
        OperationResult<BasePolicyEvaluation> result,
        string requiredGrantId,
        PrincipalContext principal,
        OperationContext operation,
        string collectionId,
        string textIndexId)
    {
        if (!result.IsSuccess() || result.Value?.Authority is not { } authority) return false;
        return authority.GrantSemantics.Any(semantics =>
        {
            AccessGrant grant = semantics.Grant;
            return ReceiptMatches(authority, semantics)
                && semantics.GrantId == requiredGrantId && grant.Id == requiredGrantId
                && grant.Effect == GrantEffect.Allow && grant.ApplicationId == operation.ApplicationId
                && grant.Audience == operation.Audience && grant.Action == requiredGrantId
                && grant.Subject.Kind == principal.SubjectKind && grant.Subject.Id == principal.SubjectId
                && grant.Subject.TenantId == principal.CurrentTenantId
                && grant.Scope.Kind == ResourceScopeKind.TextIndex && grant.Scope.CollectionId == collectionId
                && grant.Scope.TextIndexId == textIndexId && grant.Scope.RecordId is null && grant.Scope.FieldPath is null
                && grant.Scope.VectorIndexId is null && grant.Scope.SubjectContractId is null && grant.Scope.SubjectContractVersion is null
                && grant.Scope.TenantId == operation.TenantId && grant.Scope.ProjectId == operation.ProjectId
                && grant.Condition is null && grant.WriteCondition is null && (grant.ExpiresAt is null || grant.ExpiresAt > operation.Now);
        });
    }

    internal static bool HasExactSubjectLifecycleGrant(
        OperationResult<BasePolicyEvaluation> result,
        string requiredGrantId,
        string owningModuleId,
        string action,
        string contractId,
        int contractVersion,
        PrincipalContext principal,
        OperationContext operation)
    {
        if (!result.IsSuccess() || result.Value?.Authority is not { } authority)
            return false;
        return authority.GrantSemantics.Any(semantics =>
        {
            AccessGrant grant = semantics.Grant;
            return ReceiptMatches(authority, semantics)
                && string.Equals(semantics.GrantId, requiredGrantId, StringComparison.Ordinal)
                && string.Equals(grant.Id, requiredGrantId, StringComparison.Ordinal)
                && grant.Effect == GrantEffect.Allow
                && string.Equals(grant.ApplicationId, operation.ApplicationId, StringComparison.Ordinal)
                && string.Equals(grant.ModuleId, owningModuleId, StringComparison.Ordinal)
                && grant.Audience == operation.Audience
                && string.Equals(grant.Action, action, StringComparison.Ordinal)
                && grant.Subject.Kind == principal.SubjectKind
                && string.Equals(grant.Subject.Id, principal.SubjectId, StringComparison.Ordinal)
                && string.Equals(grant.Subject.TenantId, principal.CurrentTenantId, StringComparison.Ordinal)
                && grant.Scope.Kind == ResourceScopeKind.SubjectContract
                && string.Equals(grant.Scope.SubjectContractId, contractId, StringComparison.Ordinal)
                && grant.Scope.SubjectContractVersion == contractVersion
                && grant.Scope.CollectionId is null && grant.Scope.RecordId is null
                && grant.Scope.FieldPath is null && grant.Scope.VectorIndexId is null && grant.Scope.TextIndexId is null
                && string.Equals(grant.Scope.TenantId, operation.TenantId, StringComparison.Ordinal)
                && string.Equals(grant.Scope.ProjectId, operation.ProjectId, StringComparison.Ordinal)
                && grant.Condition is null && grant.WriteCondition is null
                && (grant.ExpiresAt is null || grant.ExpiresAt > operation.Now);
        });
    }

    private static bool ReceiptMatches(BasePolicyEvaluationAuthority authority, BaseAdmittedGrantSemantics semantics) =>
        authority.AdmittedGrants.Any(receipt =>
            string.Equals(receipt.GrantId, semantics.GrantId, StringComparison.Ordinal)
            && receipt.GrantVersion == semantics.GrantVersion
            && CryptographicOperations.FixedTimeEquals(receipt.GrantRegistrationChecksum.AsSpan(), semantics.GrantRegistrationChecksum.AsSpan())
            && CryptographicOperations.FixedTimeEquals(receipt.GrantChecksum.AsSpan(), semantics.GrantChecksum.AsSpan()));

    internal static bool AllowsSource(
        CollectionDefinition collection,
        OperationResult<BasePolicyEvaluation> result,
        string requiredGrantId) =>
        !collection.System || HasExactGrant(result, requiredGrantId);
}
