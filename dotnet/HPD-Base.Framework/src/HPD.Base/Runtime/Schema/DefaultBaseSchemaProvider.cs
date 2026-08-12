
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
        if (!result.IsSuccess() || result.Value?.Decision.Audit?.MatchedGrantIds is not { Length: > 0 } grants
            || result.Value.Decision.Audit.AdminBypass || result.Value.Decision.Audit.ServiceBypass)
            return false;
        return requiredGrantId is null || grants.Contains(requiredGrantId, StringComparer.Ordinal);
    }
}
