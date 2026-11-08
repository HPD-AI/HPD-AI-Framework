using System.Reflection;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace HPD.Agent.AspNetCore.OpenApi;

internal sealed class HPDAgentOpenApiDocumentTransformer : IOpenApiDocumentTransformer
{
    public Task TransformAsync(
        OpenApiDocument document,
        OpenApiDocumentTransformerContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        document.Info = new OpenApiInfo
        {
            Title = "HPD Agent API",
            Version = typeof(HPDAgentOpenApiDocumentTransformer).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
                .InformationalVersion
                ?? "0.0.0",
            Description =
                "HTTP resources, runtime-owned thread execution, and resumable " +
                "committed agent events for HPD-Agent."
        };

        document.Extensions ??= new Dictionary<string, IOpenApiExtension>();
        document.Extensions["x-hpd-protocol"] = new JsonNodeExtension(
            JsonValue.Create("hpd-agent-http+committed-sse-v1"));
        document.Extensions["x-hpd-document-name"] = new JsonNodeExtension(
            JsonValue.Create(context.DocumentName));

        return Task.CompletedTask;
    }
}
