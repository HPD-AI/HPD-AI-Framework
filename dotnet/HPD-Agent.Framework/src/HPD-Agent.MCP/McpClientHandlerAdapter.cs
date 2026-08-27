using System.Text.Json;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace HPD.Agent.MCP;

/// <summary>Adapts SDK-owned client callbacks to bounded HPD application policy.</summary>
internal static class McpClientHandlerAdapter
{
    internal static McpClientHandlers Create(string serverName, McpInvocationOptions options)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(serverName);
        ArgumentNullException.ThrowIfNull(options);

        var handlers = new McpClientHandlers();
        if (options.InputResolver is not null)
        {
            handlers.ElicitationHandler = async (request, cancellationToken) =>
            {
                ArgumentNullException.ThrowIfNull(request);
                var schema = request.RequestedSchema is null
                    ? "{}"
                    : JsonSerializer.Serialize(
                        request.RequestedSchema,
                        McpJsonUtilities.DefaultOptions.GetTypeInfo(
                            typeof(ElicitRequestParams.RequestSchema)));
                if (schema.Length > options.MaxInputPayloadCharacters)
                    throw new InvalidOperationException("MCP input schema exceeds the configured payload limit.");

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(options.HandlerTimeout);
                var context = new McpInputResolutionContext
                {
                    ServerName = serverName,
                    ToolName = McpInvocationContextScope.Current?.ToolName,
                    InvocationId = McpInvocationContextScope.Current?.InvocationId,
                    Description = Bound(request.Message, options.MaxInputPayloadCharacters),
                    Schema = schema,
                    IsSensitive = true
                };
                if (options.InputAuthorizer is not null &&
                    !await options.InputAuthorizer.AuthorizeAsync(context, timeout.Token).ConfigureAwait(false))
                    throw new UnauthorizedAccessException("mcp_mrtr_permission_denied");

                var resolution = await options.InputResolver.ResolveAsync(
                    context, timeout.Token).ConfigureAwait(false);

                if (!resolution.Resolved)
                    return new ElicitResult { Action = "decline" };

                return new ElicitResult
                {
                    Action = "accept",
                    Content = NormalizeContent(resolution.Value)
                };
            };
        }

        return handlers;
    }

    private static Dictionary<string, JsonElement> NormalizeContent(JsonElement? value)
    {
        var element = value ?? default;
        if (element.ValueKind != JsonValueKind.Object)
            throw new InvalidOperationException("MCP elicitation input must resolve to a JSON object.");
        return element.EnumerateObject().ToDictionary(
            static property => property.Name,
            static property => property.Value.Clone(),
            StringComparer.Ordinal);
    }

    private static string Bound(string? value, int maximumCharacters)
    {
        value ??= string.Empty;
        return value.Length <= maximumCharacters ? value : value[..maximumCharacters];
    }
}
