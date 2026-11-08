using HPD.Agent.Hosting.Data;
using HPD.Agent.Serialization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;
using System.Text.Json.Nodes;

namespace HPD.Agent.AspNetCore.OpenApi;

internal sealed class HPDAgentOpenApiOperationTransformer : IOpenApiOperationTransformer
{
    private static readonly string[] EventNames =
    [
        "agent-event",
        "live-agent-event",
        "thread-journal-rebased"
    ];

    private static readonly string[] HierarchyValues =
    [
        "exactThread",
        "directChildren",
        "threadAndDirectChildren",
        "descendants",
        "threadAndDescendants"
    ];

    private static readonly (Type Type, string Discriminator)[] PublicResponseTypes =
    [
        (typeof(HPD.Agent.PermissionResponseEvent), EventTypes.Permission.PERMISSION_RESPONSE),
        (typeof(HPD.Agent.ContinuationResponseEvent), EventTypes.Permission.CONTINUATION_RESPONSE),
        (typeof(HPD.Agent.QuestionResponseEvent), EventTypes.Clarification.CLARIFICATION_RESPONSE),
        (typeof(HPD.Agent.ClientTools.ClientToolInvokeOutcomeEvent), EventTypes.ClientTool.CLIENT_TOOL_INVOKE_OUTCOME)
    ];

    public async Task TransformAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ApplyTag(operation, context);
        var operationName = GetOperationName(operation.OperationId);

        switch (operationName)
        {
            case "SubmitAgentInput":
                await DescribeInputSubmissionAsync(operation, context, false, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case "SubmitControlledSubAgentInput":
                await DescribeInputSubmissionAsync(operation, context, true, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case "EstimateAgentThreadContextUsage":
                await DescribeContextUsageAsync(operation, context, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case "GetAgentThreadRuntimeState":
                await DescribeTypedResponseAsync<ThreadRuntimeStateDto>(
                        operation, context, "200", "Authoritative thread state snapshot.", cancellationToken)
                    .ConfigureAwait(false);
                await AddCommonErrorsAsync(operation, context, includeNotFound: true, includeConflict: false, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                break;
            case "RespondToAgentRequest":
                await DescribeMiddlewareResponseAsync(operation, context, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case "CancelThreadExecution":
                await DescribeTypedResponseAsync<ThreadExecutionCancellationDto>(
                        operation, context, "200", "Cancellation decision and queue state.", cancellationToken)
                    .ConfigureAwait(false);
                await AddCommonErrorsAsync(operation, context, includeNotFound: true, includeConflict: false, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                break;
            case "StartQueuedThreadWork":
                await DescribeTypedResponseAsync<ThreadExecutionDto>(
                        operation, context, "200", "Promoted or remaining queued execution.", cancellationToken)
                    .ConfigureAwait(false);
                await AddCommonErrorsAsync(operation, context, includeNotFound: true, includeConflict: false, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                break;
            case "UploadContent":
                DescribeMultipartUpload(operation);
                break;
            case "DownloadContent":
                DescribeBinaryDownload(operation);
                break;
            case "ConnectClientToolProvider":
                DescribeClientToolWebSocket(operation);
                break;
            case "GetAgentEventCatalog":
                await DescribeTypedResponseAsync<HPD.Agent.Serialization.AgentEventCatalog>(
                        operation, context, "200", "Immutable event compatibility catalog.", cancellationToken)
                    .ConfigureAwait(false);
                break;
            case "GetAgentInputCatalog":
                await DescribeTypedResponseAsync<AgentInputCatalog>(
                        operation, context, "200", "Immutable input compatibility catalog.", cancellationToken)
                    .ConfigureAwait(false);
                break;
            case "GetRuntimeAgentExtensionCatalog":
                await DescribeTypedResponseAsync<RuntimeAgentExtensionCatalog>(
                        operation, context, "200", "Tenant-scoped runtime extension schema catalog.", cancellationToken)
                    .ConfigureAwait(false);
                break;
            case "RegisterRuntimeAgentExtensionSchema":
                await DescribeRuntimeSchemaRegistrationAsync(operation, context, cancellationToken)
                    .ConfigureAwait(false);
                break;
            case "ObserveLiveEventsWithSse":
                await DescribeEventStreamAsync(operation, context, cancellationToken)
                    .ConfigureAwait(false);
                break;
        }
    }

    private static void ApplyTag(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context)
    {
        var path = (context.Description.RelativePath ?? string.Empty).TrimStart('/');
        const string agentPrefix = "api/hpd-agent";
        if (path.StartsWith(agentPrefix, StringComparison.OrdinalIgnoreCase))
            path = path[agentPrefix.Length..].TrimStart('/');

        string? tag = GetOperationName(operation.OperationId) switch
        {
            "GetAgentEventCatalog" or "ObserveLiveEventsWithSse" => "Agent Events",
            "GetAgentInputCatalog" => "Agent Inputs",
            "GetRuntimeAgentExtensionCatalog" => "Runtime Extensions",
            "RegisterRuntimeAgentExtensionSchema" => "Runtime Extensions",
            "RespondToAgentRequest" => "Agent Interactions",
            "SubmitAgentInput" or "SubmitControlledSubAgentInput" or
                "EstimateAgentThreadContextUsage" or "GetAgentThreadRuntimeState" => "Agent Runtime",
            "CreateAgent" or "ListAgents" or "GetAgent" or
                "UpdateAgent" or "DeleteAgent" => "Agents",
            "ListThreadExecutions" or "GetThreadExecution" or
                "CancelThreadExecution" or "StartQueuedThreadWork" => "Executions",
            "UploadContent" or "ListContent" or "DownloadContent" or "DeleteContent" => "Content",
            "ListThreads" or "GetThreadGraph" or "GetThread" or
                "ListThreadSubAgents" or "CreateThread" or "ForkThread" or
                "UpdateThread" or "DeleteThread" => "Threads",
            "CreateSession" or "ListSessions" or "SearchSessions" or
                "GetSession" or "UpdateSession" or "DeleteSession" => "Sessions",
            _ => null
        };

        tag ??= path switch
        {
            _ when path.StartsWith("evals", StringComparison.OrdinalIgnoreCase) => "Evaluations",
            _ when path.Contains("client-tool-providers", StringComparison.OrdinalIgnoreCase) => "Client Tools",
            _ when path.Contains("/content", StringComparison.OrdinalIgnoreCase) => "Content",
            _ when path.Contains("/executions", StringComparison.OrdinalIgnoreCase) => "Executions",
            _ when path.Contains("/queue/", StringComparison.OrdinalIgnoreCase) => "Executions",
            _ when path.Contains("/responses", StringComparison.OrdinalIgnoreCase) => "Agent Interactions",
            _ when path.EndsWith("/events", StringComparison.OrdinalIgnoreCase) => "Agent Events",
            _ when path.EndsWith("/event-catalog", StringComparison.OrdinalIgnoreCase) => "Agent Events",
            _ when path.EndsWith("/runtime-extension-catalog", StringComparison.OrdinalIgnoreCase) => "Runtime Extensions",
            _ when path.Contains("thread-graph", StringComparison.OrdinalIgnoreCase) => "Threads",
            _ when path.Contains("/threads/", StringComparison.OrdinalIgnoreCase) => "Threads",
            _ when path.EndsWith("/threads", StringComparison.OrdinalIgnoreCase) => "Threads",
            _ when path.StartsWith("sessions", StringComparison.OrdinalIgnoreCase) => "Sessions",
            _ when path.StartsWith("agents", StringComparison.OrdinalIgnoreCase) => "Agents",
            _ => "Agent Runtime"
        };

        var document = context.Document;
        if (document is not null)
        {
            document.Tags ??= new HashSet<OpenApiTag>();
            if (!document.Tags.Any(existing => string.Equals(existing.Name, tag, StringComparison.Ordinal)))
                document.Tags.Add(new OpenApiTag { Name = tag });
        }

        operation.Tags = new HashSet<OpenApiTagReference>
        {
            new(tag, document)
        };
    }

    private static async Task DescribeInputSubmissionAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        bool controlledSubAgent,
        CancellationToken cancellationToken)
    {
        RemoveSuccessResponses(operation, keepStatusCode: "202");
        var inputComposition = context.ApplicationServices.GetService<AgentInputComposition>()
            ?? AgentInputCompositionHost.ResolveDefault();
        var publicInputs = inputComposition.Inputs
            .Where(static input => input.IsPublic)
            .OrderBy(static input => input.Discriminator, StringComparer.Ordinal)
            .Select(static input => (input.InputType, input.Discriminator))
            .ToArray();
        var inputSchema = await CreateEnvelopeUnionAsync(
                context, publicInputs, cancellationToken)
            .ConfigureAwait(false);

        operation.Description = controlledSubAgent
            ? "Submits a public input envelope to one durable child-agent controller entry."
            : "Accepts a typed public input envelope or the legacy { text, clientInputId, runConfig } convenience shape.";
        operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Description = "Public HPD agent input event envelope.",
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new OpenApiMediaType { Schema = inputSchema }
            }
        };

        await DescribeTypedResponseAsync<InputSubmissionDto>(
                operation, context, "202", "Input accepted for runtime-owned execution.", cancellationToken)
            .ConfigureAwait(false);
        await AddCommonErrorsAsync(operation, context, includeNotFound: true, includeConflict: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        if (controlledSubAgent)
        {
            EnsureParameter(
                operation,
                new OpenApiParameter
                {
                    Name = "childAgentId",
                    In = ParameterLocation.Query,
                    Required = true,
                    Description = "Durable child controller agent identifier.",
                    Schema = StringSchema()
                });
            EnsureParameter(
                operation,
                new OpenApiParameter
                {
                    Name = "childSessionId",
                    In = ParameterLocation.Query,
                    Required = true,
                    Description = "Durable child session identifier.",
                    Schema = StringSchema()
                });
            EnsureParameter(
                operation,
                new OpenApiParameter
                {
                    Name = "childThreadId",
                    In = ParameterLocation.Query,
                    Required = true,
                    Description = "Durable child thread identifier.",
                    Schema = StringSchema()
                });
        }
    }

    private static async Task DescribeContextUsageAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var requestSchema = await context.GetOrCreateSchemaAsync(
                typeof(ContextUsageRequest),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        operation.RequestBody = new OpenApiRequestBody
        {
            Required = false,
            Description = "Optional context estimation overrides.",
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new OpenApiMediaType { Schema = requestSchema }
            }
        };
        await DescribeTypedResponseAsync<ThreadContextUsage>(
                operation, context, "200", "Estimated context usage.", cancellationToken)
            .ConfigureAwait(false);
        await AddCommonErrorsAsync(operation, context, includeNotFound: true, includeConflict: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private static async Task DescribeMiddlewareResponseAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var schema = await CreateEnvelopeUnionAsync(
                context, PublicResponseTypes, cancellationToken)
            .ConfigureAwait(false);
        operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Description = "One permission, client-tool, or other response event envelope.",
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new OpenApiMediaType { Schema = schema }
            }
        };
        await DescribeTypedResponseAsync<AgentRespondResult>(
                operation, context, "200", "Response accepted and resolved.", cancellationToken)
            .ConfigureAwait(false);
        await AddCommonErrorsAsync(operation, context, includeNotFound: true, includeConflict: true, cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    private static void DescribeMultipartUpload(OpenApiOperation operation)
    {
        operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Description = "Multipart upload containing one file in the 'file' field.",
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["multipart/form-data"] = new OpenApiMediaType
                {
                    Schema = new OpenApiSchema
                    {
                        Type = JsonSchemaType.Object,
                        Required = new HashSet<string> { "file" },
                        Properties = new Dictionary<string, IOpenApiSchema>
                        {
                            ["file"] = new OpenApiSchema
                            {
                                Type = JsonSchemaType.String,
                                Format = "binary",
                                Description = "Thread content file."
                            }
                        }
                    }
                }
            }
        };
    }

    private static void DescribeBinaryDownload(OpenApiOperation operation)
    {
        operation.Responses ??= new OpenApiResponses();
        operation.Responses["200"] = new OpenApiResponse
        {
            Description = "Stored binary content.",
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/octet-stream"] = new OpenApiMediaType
                {
                    Schema = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                        Format = "binary",
                        Description = "Stored thread content."
                    }
                }
            }
        };
    }

    private static void DescribeClientToolWebSocket(OpenApiOperation operation)
    {
        operation.Description =
            "Upgrades the connection to the HPD client-tool-provider WebSocket " +
            "protocol. Send provider.hello first, then provider.manifest and " +
            "heartbeat/tool messages.";
        operation.Responses = new OpenApiResponses
        {
            ["101"] = new OpenApiResponse
            {
                Description = "WebSocket protocol upgrade."
            },
            ["400"] = new OpenApiResponse
            {
                Description = "The request is not a valid WebSocket upgrade."
            },
            ["401"] = new OpenApiResponse
            {
                Description = "The provider connection was not authorized."
            }
        };
        operation.Extensions ??= new Dictionary<string, IOpenApiExtension>();
        operation.Extensions["x-hpd-websocket-protocol"] = new JsonNodeExtension(
            JsonValue.Create("hpd.client-tool-provider.v2"));
        operation.Extensions["x-hpd-websocket-first-message"] = new JsonNodeExtension(
            JsonValue.Create("provider.hello"));
    }

    private static string? GetOperationName(string? operationId)
    {
        if (string.IsNullOrWhiteSpace(operationId))
            return operationId;
        var separator = operationId.LastIndexOf('.');
        return separator < 0 ? operationId : operationId[(separator + 1)..];
    }

    private static async Task DescribeEventStreamAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var eventComposition = ResolveEventComposition(context.ApplicationServices);
        var eventSchema = await CreateAgentEventUnionAsync(
                context,
                eventComposition.Fragments
                    .SelectMany(static fragment => fragment.Events)
                    .OrderBy(static descriptor => descriptor.Discriminator, StringComparer.Ordinal)
                    .ToArray(),
                cancellationToken)
            .ConfigureAwait(false);
        var routeSchema = await context.GetOrCreateSchemaAsync(
                typeof(AgentEventRoute),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        operation.Description =
            "Replays committed journal facts, then remains attached for live events. " +
            "Each SSE event has an id in generation:sequence form. Reconnect with " +
            "the after query parameter or Last-Event-ID header. Agent event payloads " +
            "are typed from the application's generated event composition; the catalog " +
            "operation provides the corresponding compatibility digest.";

        EnsureParameter(
            operation,
            new OpenApiParameter
            {
                Name = "hierarchy",
                In = ParameterLocation.Query,
                Required = false,
                Description = "Thread hierarchy selected for observation. Defaults to exactThread.",
                Schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Enum = [.. HierarchyValues.Select(static value => (JsonNode)JsonValue.Create(value)!)],
                    Default = JsonValue.Create("exactThread")
                }
            });
        EnsureParameter(
            operation,
            new OpenApiParameter
            {
                Name = "after",
                In = ParameterLocation.Query,
                Required = false,
                Description = "Applied event cursor in generation:sequence form.",
                Schema = StringSchema()
            });
        EnsureParameter(
            operation,
            new OpenApiParameter
            {
                Name = "Last-Event-ID",
                In = ParameterLocation.Header,
                Required = false,
                Description = "Applied event cursor in generation:sequence form.",
                Schema = StringSchema()
            });

        var eventDelivery = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Required = new HashSet<string> { "event", "route" },
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["event"] = eventSchema,
                ["route"] = routeSchema
            }
        };
        var journalRebased = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Required = new HashSet<string> { "previousGeneration", "currentGeneration" },
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["previousGeneration"] = new OpenApiSchema { Type = JsonSchemaType.Integer },
                ["currentGeneration"] = new OpenApiSchema { Type = JsonSchemaType.Integer }
            }
        };
        var eventEnvelope = new OpenApiSchema
        {
            Type = JsonSchemaType.Object,
            Required = new HashSet<string> { "event", "data" },
            Properties = new Dictionary<string, IOpenApiSchema>
            {
                ["id"] = StringSchema("generation:sequence cursor when the event belongs to the anchor thread."),
                ["event"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Enum = [.. EventNames.Select(static value => (JsonNode)JsonValue.Create(value)!)],
                    Description = "SSE event name."
                },
                ["data"] = new OpenApiSchema
                {
                    Description =
                        "Agent delivery data contains the typed event and immutable route. " +
                        "thread-journal-rebased data contains the previous and current generations.",
                    OneOf = [eventDelivery, journalRebased]
                }
            }
        };

        operation.Responses ??= new OpenApiResponses();
        operation.Responses["200"] = new OpenApiResponse
        {
            Description = "Committed replay and live observation stream.",
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["text/event-stream"] = new OpenApiMediaType { Schema = eventEnvelope }
            }
        };
        await AddCommonErrorsAsync(
                operation,
                context,
                includeNotFound: true,
                includeConflict: false,
                cancellationToken: cancellationToken,
                includeBadRequest: true)
            .ConfigureAwait(false);

        operation.Extensions ??= new Dictionary<string, IOpenApiExtension>();
        operation.Extensions["x-hpd-stream-protocol"] = new JsonNodeExtension(
            JsonValue.Create("committed-sse-v1"));
        operation.Extensions["x-hpd-event-catalog-operation"] = new JsonNodeExtension(
            JsonValue.Create("GetAgentEventCatalog"));
        operation.Extensions["x-hpd-runtime-extension-catalog-operation"] = new JsonNodeExtension(
            JsonValue.Create("GetRuntimeAgentExtensionCatalog"));
        operation.Extensions["x-hpd-cursor-format"] = new JsonNodeExtension(
            JsonValue.Create("generation:sequence"));
        operation.Extensions["x-hpd-heartbeat-seconds"] = new JsonNodeExtension(
            JsonValue.Create(15));
        operation.Extensions["x-hpd-event-count"] = new JsonNodeExtension(
            JsonValue.Create(eventComposition.Catalog.Events.Count));
        operation.Extensions["x-hpd-event-digest"] = new JsonNodeExtension(
            JsonValue.Create(eventComposition.Digest));
    }

    private static AgentEventComposition ResolveEventComposition(IServiceProvider services)
    {
        var composition = services.GetService<AgentEventComposition>();
        if (composition is not null)
            return composition;
        if (AgentEventCompositionHost.TryGetDefaultApplication(out composition))
            return composition;
        if (AgentEventCompositionHost.TryComposeRegisteredModules(out composition))
            return composition;
        throw new InvalidOperationException(
            "OpenAPI event documentation requires a generated AgentEventComposition.");
    }

    private static async Task<OpenApiSchema> CreateAgentEventUnionAsync(
        OpenApiOperationTransformerContext context,
        IReadOnlyList<AgentEventDescriptor> events,
        CancellationToken cancellationToken)
    {
        var branches = new List<IOpenApiSchema>(events.Count);
        foreach (var descriptor in events)
        {
            var branch = await context.GetOrCreateSchemaAsync(
                    descriptor.EventType,
                    cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var resolved = ResolveSchema(context, branch);
            if (resolved is not null)
            {
                resolved.Properties ??= new Dictionary<string, IOpenApiSchema>();
                resolved.Properties["type"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Enum = [JsonValue.Create(descriptor.Discriminator)]
                };
                resolved.Required ??= new HashSet<string>();
                resolved.Required.Add("type");
                ComponentizeProperty(
                    context,
                    resolved,
                    "metadata",
                    nameof(AgentMetadata));
                resolved.Extensions ??= new Dictionary<string, IOpenApiExtension>();
                resolved.Extensions["x-hpd-event-discriminator"] = new JsonNodeExtension(
                    JsonValue.Create(descriptor.Discriminator));
                resolved.Extensions["x-hpd-event-durability"] = new JsonNodeExtension(
                    JsonValue.Create(descriptor.Durability.ToString()));
                resolved.Extensions["x-hpd-event-module"] = new JsonNodeExtension(
                    JsonValue.Create(descriptor.ModuleId));
            }
            branches.Add(RegisterComponent(
                context,
                descriptor.EventType.FullName?.Replace('+', '.') ?? descriptor.EventType.Name,
                branch));
        }

        return new OpenApiSchema
        {
            OneOf = branches,
            Description = "A typed agent output event from the application event composition."
        };
    }

    private static async Task<OpenApiSchema> CreateEnvelopeUnionAsync(
        OpenApiOperationTransformerContext context,
        IReadOnlyList<(Type Type, string Discriminator)> variants,
        CancellationToken cancellationToken)
    {
        var branches = new List<IOpenApiSchema>(variants.Count);
        foreach (var (type, discriminator) in variants)
        {
            var branch = await context.GetOrCreateSchemaAsync(
                    type, cancellationToken: cancellationToken)
                .ConfigureAwait(false);
            var resolved = ResolveSchema(context, branch);
            if (resolved is not null)
            {
                resolved.Properties ??= new Dictionary<string, IOpenApiSchema>();
                resolved.Properties["type"] = new OpenApiSchema
                {
                    Type = JsonSchemaType.String,
                    Enum = [JsonValue.Create(discriminator)]
                };
                resolved.Required ??= new HashSet<string>();
                resolved.Required.Add("type");
                ComponentizeProperty(
                    context,
                    resolved,
                    "runConfig",
                    nameof(HPD.Agent.AgentRunConfig));
                ComponentizeProperty(
                    context,
                    resolved,
                    "subAgentRunConfig",
                    nameof(HPD.Agent.SubAgentRunConfig));
            }
            branches.Add(RegisterComponent(
                context,
                type.FullName?.Replace('+', '.') ?? type.Name,
                branch));
        }

        return new OpenApiSchema { OneOf = branches };
    }

    private static void ComponentizeProperty(
        OpenApiOperationTransformerContext context,
        OpenApiSchema schema,
        string propertyName,
        string componentName)
    {
        if (schema.Properties is null ||
            !schema.Properties.TryGetValue(propertyName, out var propertySchema) ||
            propertySchema is OpenApiSchemaReference)
        {
            return;
        }

        schema.Properties[propertyName] = RegisterComponent(
            context,
            componentName,
            propertySchema);
    }

    private static IOpenApiSchema RegisterComponent(
        OpenApiOperationTransformerContext context,
        string componentName,
        IOpenApiSchema schema)
    {
        if (schema is OpenApiSchemaReference)
            return schema;

        var document = context.Document;
        if (document is null)
            return schema;

        document.Components ??= new OpenApiComponents();
        document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
        document.Components.Schemas.TryAdd(componentName, schema);
        return new OpenApiSchemaReference(componentName, document);
    }

    private static OpenApiSchema? ResolveSchema(
        OpenApiOperationTransformerContext context,
        IOpenApiSchema schema)
    {
        if (schema is OpenApiSchema direct)
            return direct;
        if (schema is OpenApiSchemaReference reference &&
            reference.Reference.Id is { } id &&
            context.Document?.Components?.Schemas?.TryGetValue(id, out var target) == true)
        {
            return target as OpenApiSchema;
        }
        return null;
    }

    private static async Task DescribeTypedResponseAsync<T>(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        string statusCode,
        string description,
        CancellationToken cancellationToken)
    {
        var schema = await context.GetOrCreateSchemaAsync(
                typeof(T),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        operation.Responses ??= new OpenApiResponses();
        operation.Responses[statusCode] = new OpenApiResponse
        {
            Description = description,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new OpenApiMediaType { Schema = schema }
            }
        };
    }

    private static async Task DescribeRuntimeSchemaRegistrationAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        CancellationToken cancellationToken)
    {
        var requestSchema = await context.GetOrCreateSchemaAsync(
                typeof(RuntimeAgentExtensionSchema),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        operation.RequestBody = new OpenApiRequestBody
        {
            Required = true,
            Description = "Authorized immutable runtime extension schema.",
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/json"] = new OpenApiMediaType { Schema = requestSchema }
            }
        };
        await DescribeTypedResponseAsync<RuntimeAgentExtensionDescriptor>(
                operation, context, "201", "Runtime extension schema registered.", cancellationToken)
            .ConfigureAwait(false);
        await AddCommonErrorsAsync(
                operation,
                context,
                includeNotFound: false,
                includeConflict: true,
                cancellationToken,
                includeBadRequest: true)
            .ConfigureAwait(false);
        var problemSchema = await context.GetOrCreateSchemaAsync(
                typeof(ProblemDetails),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        operation.Responses ??= new OpenApiResponses();
        operation.Responses["403"] = new OpenApiResponse
        {
            Description = "Runtime schema management was denied.",
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/problem+json"] = new OpenApiMediaType { Schema = problemSchema }
            }
        };
        operation.Responses["503"] = new OpenApiResponse
        {
            Description = "Runtime extension registration is not configured.",
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/problem+json"] = new OpenApiMediaType { Schema = problemSchema }
            }
        };
    }

    private static async Task AddCommonErrorsAsync(
        OpenApiOperation operation,
        OpenApiOperationTransformerContext context,
        bool includeNotFound,
        bool includeConflict,
        CancellationToken cancellationToken,
        bool includeBadRequest = false)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var problemSchema = await context.GetOrCreateSchemaAsync(
                typeof(ProblemDetails),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);
        var validationSchema = await context.GetOrCreateSchemaAsync(
                typeof(ValidationProblemDetails),
                cancellationToken: cancellationToken)
            .ConfigureAwait(false);

        operation.Responses ??= new OpenApiResponses();
        if (includeBadRequest)
        {
            operation.Responses["400"] = ProblemResponse("Invalid request.", validationSchema);
        }
        if (includeNotFound)
        {
            operation.Responses["404"] = ProblemResponse("Resource not found.", problemSchema);
        }
        if (includeConflict)
        {
            operation.Responses["409"] = ProblemResponse("Current runtime state conflicts with the request.", problemSchema);
        }
        operation.Responses["500"] = ProblemResponse("Unexpected runtime failure.", problemSchema);
    }

    private static OpenApiResponse ProblemResponse(string description, IOpenApiSchema schema) =>
        new()
        {
            Description = description,
            Content = new Dictionary<string, OpenApiMediaType>
            {
                ["application/problem+json"] = new OpenApiMediaType { Schema = schema }
            }
        };

    private static OpenApiSchema StringSchema(string? description = null) =>
        new()
        {
            Type = JsonSchemaType.String,
            Description = description
        };

    private static void RemoveSuccessResponses(OpenApiOperation operation, string keepStatusCode)
    {
        if (operation.Responses is null)
            return;

        foreach (var statusCode in operation.Responses.Keys
                     .Where(code => code.StartsWith('2') &&
                         !string.Equals(code, keepStatusCode, StringComparison.Ordinal))
                     .ToArray())
        {
            operation.Responses.Remove(statusCode);
        }
    }

    private static void EnsureParameter(OpenApiOperation operation, OpenApiParameter parameter)
    {
        operation.Parameters ??= [];
        if (operation.Parameters.Any(existing =>
                string.Equals(existing.Name, parameter.Name, StringComparison.OrdinalIgnoreCase) &&
                existing.In == parameter.In))
        {
            return;
        }

        operation.Parameters.Add(parameter);
    }
}
