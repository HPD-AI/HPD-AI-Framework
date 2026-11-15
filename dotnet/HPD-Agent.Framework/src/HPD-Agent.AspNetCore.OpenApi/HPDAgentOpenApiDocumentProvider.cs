using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace HPD.Agent.AspNetCore.OpenApi;

/// <summary>
/// Applies HPD schema corrections after the framework finishes generating the
/// document and before an endpoint or SDK generator serializes it.
/// </summary>
internal sealed class HPDAgentOpenApiDocumentProvider(
    IOpenApiDocumentProvider inner) : IOpenApiDocumentProvider
{
    public async Task<OpenApiDocument> GetOpenApiDocumentAsync(
        CancellationToken cancellationToken = default)
    {
        var document = await inner.GetOpenApiDocumentAsync(cancellationToken)
            .ConfigureAwait(false);
        HPDAgentOpenApiPortableSchema.ApplyGeneratedDocumentSchemas(document);
        return document;
    }
}
