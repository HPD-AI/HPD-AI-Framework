using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

#pragma warning disable IL2070, IL2075

namespace HPD.Agent;

internal static class ReflectionToolFactory
{
    private const string ReflectionRequiresUnreferencedCodeMessage =
        "Reflection tool registration inspects toolharness methods and attributes at runtime. Use the HPD Agent source generator for Native AOT or trimmed apps.";

    [RequiresUnreferencedCode(ReflectionRequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode("Reflection tool registration uses runtime method invocation and System.Text.Json runtime type metadata.")]
    internal static bool TryCreateToolHarnessFactory(
        Type toolharnessType,
        [NotNullWhen(true)] out ToolHarnessFactory? factory,
        out string? error)
    {
        ArgumentNullException.ThrowIfNull(toolharnessType);

        factory = null;
        error = null;

        var methods = GetCapabilityMethods(toolharnessType).ToArray();
        if (methods.Length == 0)
        {
            error = $"ToolHarness '{toolharnessType.Name}' was not found in ToolHarnessRegistry.All, and no public methods with [AIFunction], [Skill], [SubAgent], or [MultiAgent] were found for reflection fallback.";
            return false;
        }

        var hasInstanceMethods = methods.Any(method => !method.IsStatic);
        if (hasInstanceMethods && toolharnessType.GetConstructor(Type.EmptyTypes) is null)
        {
            error = $"ToolHarness '{toolharnessType.Name}' has instance [AIFunction] methods but no public parameterless constructor. Use the source generator or add a parameterless constructor.";
            return false;
        }

        var collapseAttribute = GetCollapseAttribute(toolharnessType);

        factory = new ToolHarnessFactory(
            Name: toolharnessType.Name,
            ToolHarnessType: toolharnessType,
            CreateInstance: () => Activator.CreateInstance(toolharnessType)
                ?? throw new InvalidOperationException($"Could not create toolharness '{toolharnessType.Name}'."),
            CreateFunctions: (instance, context, serialization) => CreateFunctions(methods, instance, context, serialization),
            GetReferencedToolHarnesses: () => Array.Empty<string>(),
            GetReferencedFunctions: () => new Dictionary<string, string[]>(),
            HasDescription: collapseAttribute is not null,
            Description: GetStringProperty(collapseAttribute, "Description"),
            FunctionResult: GetStringProperty(collapseAttribute, "FunctionResult"),
            SystemPrompt: GetStringProperty(collapseAttribute, "SystemPrompt"),
            FunctionNames: methods.Select(GetCapabilityName).ToArray());

        return true;
    }

    [RequiresUnreferencedCode(ReflectionRequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode("Reflection tool registration uses runtime method invocation and System.Text.Json runtime type metadata.")]
    private static List<AIFunction> CreateFunctions(
        MethodInfo[] methods,
        object instance,
        IToolMetadata? context,
        HPDToolSerializationOptions? serialization)
    {
        var serializerOptions = serialization?.SerializerOptions ?? HPDToolArgumentBinder.DefaultSerializerOptions;
        var functions = new List<AIFunction>(methods.Length);

        foreach (var method in methods)
        {
            ThrowIfGeneratorOnlyFeaturesAreUsed(method);
            functions.Add(CreateCapability(method, method.IsStatic ? null : instance, context, serializerOptions));
        }

        return functions;
    }

    [RequiresUnreferencedCode(ReflectionRequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode("Reflection tool registration uses runtime method invocation and System.Text.Json runtime type metadata.")]
    private static AIFunction CreateCapability(
        MethodInfo method,
        object? instance,
        IToolMetadata? context,
        JsonSerializerOptions serializerOptions)
    {
        if (HasAttribute(method, "SkillAttribute"))
        {
            return CreateSkill(method, instance, serializerOptions);
        }

        if (HasAttribute(method, "SubAgentAttribute"))
        {
            return CreateSubAgent(method, instance, serializerOptions);
        }

        if (HasAttribute(method, "MultiAgentAttribute"))
        {
            return CreateMultiAgent(method, instance, serializerOptions);
        }

        return CreateFunction(method, instance, context, serializerOptions);
    }

    private static AIFunction CreateFunction(
        MethodInfo method,
        object? instance,
        IToolMetadata? context,
        JsonSerializerOptions serializerOptions)
    {
        var parameters = GetModelParameters(method).ToArray();
        var parameterNames = parameters.Select(parameter => parameter.Name!).ToArray();
        var resultType = UnwrapReturnType(method.ReturnType);

        return HPDAIFunctionFactory.Create(
            async (arguments, functionContext, cancellationToken) =>
            {
                var json = arguments.GetJson();
                var values = BindParameters(method, json, arguments, functionContext, cancellationToken, serializerOptions);
                var result = method.Invoke(instance, values);
                return await AwaitIfNeededAsync(result).ConfigureAwait(false);
            },
            new HPDAIFunctionFactoryOptions
            {
                Name = GetFunctionName(method),
                Description = GetDescription(method),
                ParameterDescriptions = parameters
                    .Where(parameter => GetDescription(parameter) is not null)
                    .ToDictionary(parameter => parameter.Name!, parameter => GetDescription(parameter)!),
                RequiresPermission = HasAttribute(method, "RequiresPermissionAttribute"),
                SerializerOptions = serializerOptions,
                ResultType = resultType,
                Validator = (json, options) => ValidateArguments(json, options, parameters, parameterNames),
                SchemaProvider = () => CreateSchema(method, parameters, serializerOptions),
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["ParentToolHarness"] = method.DeclaringType?.Name,
                    ["IsContainer"] = false,
                    ["CapabilityType"] = "Function",
                    ["Kind"] = GetToolKind(method).ToString()
                }
            });
    }

    private static AIFunction CreateSkill(
        MethodInfo method,
        object? instance,
        JsonSerializerOptions serializerOptions)
    {
        var skill = InvokeCapabilityMethod<Skill>(method, instance);
        var references = skill.References ?? Array.Empty<string>();
        var functionList = references.Length == 0 ? "(none)" : string.Join(", ", references);
        var mode = AgentConfig.GlobalConfig?.Collapsing?.SkillInstructionMode ?? SkillInstructionMode.PromptMiddlewareOnly;
        var returnMessage = mode == SkillInstructionMode.Both && !string.IsNullOrWhiteSpace(skill.FunctionResult)
            ? $"{skill.Name} skill activated. Available functions: {functionList}\n\n{skill.FunctionResult}"
            : $"{skill.Name} skill activated. Available functions: {functionList}";

        return HPDAIFunctionFactory.Create(
            async (arguments, functionContext, cancellationToken) => returnMessage,
            new HPDAIFunctionFactoryOptions
            {
                Name = skill.Name,
                Description = $"{skill.Description}. References {references.Length} functions: {functionList}",
                RequiresPermission = HasAttribute(method, "RequiresPermissionAttribute"),
                SerializerOptions = serializerOptions,
                ResultType = typeof(string),
                SchemaProvider = CreateEmptySchema,
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["CapabilityType"] = "Skill",
                    ["IsContainer"] = true,
                    ["IsSkill"] = true,
                    ["ParentContainer"] = method.DeclaringType?.Name,
                    ["ReferencedFunctions"] = references,
                    ["ReferencedToolHarnesses"] = references
                        .Select(reference => reference.Split('.')[0])
                        .Where(name => !string.IsNullOrWhiteSpace(name))
                        .Distinct(StringComparer.Ordinal)
                        .ToArray(),
                    ["SystemPrompt"] = skill.SystemPrompt,
                    ["FunctionResult"] = skill.FunctionResult
                }
            });
    }

    private static AIFunction CreateSubAgent(
        MethodInfo method,
        object? instance,
        JsonSerializerOptions serializerOptions)
    {
        var subAgent = InvokeCapabilityMethod<SubAgent>(method, instance);

        return HPDAIFunctionFactory.Create(
            async (arguments, functionContext, cancellationToken) =>
            {
                var jsonArgs = arguments.GetJson();
                var query = jsonArgs.TryGetProperty("query", out var queryProperty)
                    ? queryProperty.GetString() ?? string.Empty
                    : string.Empty;

                var agentBuilder = subAgent.SourceKind == SubAgentSourceKind.StoredAgent
                    ? CreateStoredSubAgentBuilder(subAgent, functionContext)
                    : CreateInlineSubAgentBuilder(subAgent, functionContext);

                foreach (var toolType in subAgent.ToolHarnessTypes ?? Array.Empty<Type>())
                {
                    agentBuilder.WithToolHarness(toolType);
                }

                var parentStore = functionContext?.GetParentSessionStore();
                if (parentStore != null)
                {
                    agentBuilder.WithSessionStore(parentStore);
                }

                var agent = await agentBuilder.BuildAsync(cancellationToken).ConfigureAwait(false);

                var parentCoordinator = functionContext?.GetParentEventCoordinator();
                if (parentCoordinator != null)
                {
                    agent.EventCoordinator.SetParent(parentCoordinator);
                }

                agent.AgentMetadata = CreateSubAgentMetadata(agent, subAgent, functionContext?.GetParentAgentMetadata());

                var textResult = new StringBuilder();
                var route = await SubAgentRuntime.ResolveRouteAsync(agent, subAgent, functionContext, cancellationToken)
                    .ConfigureAwait(false);

                try
                {
                    using var outputSubscription = agent.SubscribeAny(evt =>
                    {
                        if (evt is TextDeltaEvent textDelta)
                        {
                            textResult.Append(textDelta.Text);
                        }

                        return ValueTask.CompletedTask;
                    });

                    await agent.RunAsync(new UserTextInputEvent(query)
                    {
                        SessionId = route.SessionId,
                        ThreadId = route.ThreadId
                    }, cancellationToken).ConfigureAwait(false);

                    SubAgentRuntime.MarkCompleted(functionContext, route);
                    return textResult.Length > 0 ? textResult.ToString() : string.Empty;
                }
                catch (Exception ex)
                {
                    SubAgentRuntime.MarkFailed(functionContext, route, ex);
                    throw;
                }
            },
            new HPDAIFunctionFactoryOptions
            {
                Name = subAgent.Name,
                Description = subAgent.Description,
                RequiresPermission = true,
                SerializerOptions = serializerOptions,
                ResultType = typeof(string),
                SchemaProvider = CreateQuerySchema,
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["CapabilityType"] = "SubAgent",
                    ["IsContainer"] = false,
                    ["IsSubAgent"] = true,
                    ["ExecutionModel"] = "ThreadNative",
                    ["ParentToolHarness"] = method.DeclaringType?.Name,
                    ["RequiresPermission"] = true
                }
            });
    }

    private static AgentMetadata CreateSubAgentMetadata(
        Agent agent,
        SubAgent subAgent,
        AgentMetadata? parentMetadata)
    {
        var agentChain = parentMetadata is not null
            ? parentMetadata.AgentChain.Concat([subAgent.Name]).ToArray()
            : [subAgent.Name];

        return new AgentMetadata
        {
            AgentName = subAgent.Name,
            AgentId = agent.AgentId,
            ParentAgentId = parentMetadata?.AgentId,
            AgentChain = agentChain,
            Depth = (parentMetadata?.Depth ?? -1) + 1
        };
    }

    private static AIFunction CreateMultiAgent(
        MethodInfo method,
        object? instance,
        JsonSerializerOptions serializerOptions)
    {
        var attribute = GetAttribute(method, "MultiAgentAttribute");
        var name = GetStringProperty(attribute, "Name") ?? method.Name;
        var description = GetStringProperty(attribute, "Description")
            ?? GetDescription((MemberInfo)method)
            ?? $"Runs the {method.Name} workflow.";
        var streamEvents = GetBooleanProperty(attribute, "StreamEvents") ?? true;
        var timeoutSeconds = GetIntProperty(attribute, "TimeoutSeconds") ?? 300;

        return HPDAIFunctionFactory.Create(
            async (arguments, functionContext, cancellationToken) =>
            {
                var jsonArgs = arguments.GetJson();
                var input = jsonArgs.TryGetProperty("input", out var inputProperty)
                    ? inputProperty.GetString() ?? string.Empty
                    : string.Empty;

                var workflow = await InvokeCapabilityMethodAsync(method, instance).ConfigureAwait(false);
                return await RunWorkflowAsync(workflow, input, cancellationToken).ConfigureAwait(false);
            },
            new HPDAIFunctionFactoryOptions
            {
                Name = name,
                Description = description,
                RequiresPermission = true,
                SerializerOptions = serializerOptions,
                ResultType = typeof(string),
                SchemaProvider = CreateInputSchema,
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["CapabilityType"] = "MultiAgent",
                    ["IsContainer"] = false,
                    ["IsMultiAgent"] = true,
                    ["ParentToolHarness"] = method.DeclaringType?.Name,
                    ["StreamEvents"] = streamEvents,
                    ["TimeoutSeconds"] = timeoutSeconds,
                    ["RequiresPermission"] = true
                }
            });
    }

    private static object?[] BindParameters(
        MethodInfo method,
        JsonElement json,
        AIFunctionArguments arguments,
        FunctionExecutionContext functionContext,
        CancellationToken cancellationToken,
        JsonSerializerOptions serializerOptions)
    {
        var parameters = method.GetParameters();
        var values = new object?[parameters.Length];

        for (var i = 0; i < parameters.Length; i++)
        {
            var parameter = parameters[i];
            var parameterType = parameter.ParameterType;

            if (parameterType == typeof(CancellationToken))
            {
                values[i] = cancellationToken;
            }
            else if (parameterType == typeof(AIFunctionArguments))
            {
                values[i] = arguments;
            }
            else if (parameterType == typeof(FunctionExecutionContext))
            {
                values[i] = functionContext;
            }
            else if (parameterType == typeof(IServiceProvider))
            {
                values[i] = functionContext.Services;
            }
            else
            {
                values[i] = BindModelParameter(json, parameter, serializerOptions);
            }
        }

        return values;
    }

    private static object? BindModelParameter(JsonElement json, ParameterInfo parameter, JsonSerializerOptions serializerOptions)
    {
        var parameterName = parameter.Name ?? throw new InvalidOperationException("Tool parameters must be named.");

        if (!HPDToolArgumentBinder.TryGetProperty(json, parameterName, out var property))
        {
            if (parameter.HasDefaultValue)
            {
                return parameter.DefaultValue;
            }

            throw new HPDToolArgumentException(
                parameterName,
                $"Required property '{parameterName}' is missing.",
                "missing_required_property");
        }

        if (property.ValueKind == JsonValueKind.Null)
        {
            if (IsNullable(parameter))
            {
                return null;
            }

            throw new HPDToolArgumentException(
                parameterName,
                $"Property '{parameterName}' is required and cannot be null.",
                "null_required_property");
        }

        try
        {
            return JsonSerializer.Deserialize(property, parameter.ParameterType, serializerOptions);
        }
        catch (JsonException ex)
        {
            throw new HPDToolArgumentException(parameterName, ex.Message, "type_conversion_error", ex);
        }
        catch (NotSupportedException ex)
        {
            throw new HPDToolArgumentException(parameterName, ex.Message, "unsupported_parameter_type", ex);
        }
    }

    private static List<ValidationError> ValidateArguments(
        JsonElement json,
        JsonSerializerOptions serializerOptions,
        ParameterInfo[] parameters,
        string[] parameterNames)
    {
        var errors = new List<ValidationError>();

        try
        {
            HPDToolArgumentBinder.ValidateNoUnmappedProperties(json, serializerOptions, parameterNames);
        }
        catch (HPDToolArgumentException ex)
        {
            errors.Add(ToValidationError(ex));
        }

        foreach (var parameter in parameters)
        {
            var name = parameter.Name!;
            if (!HPDToolArgumentBinder.TryGetProperty(json, name, out var property))
            {
                if (!parameter.HasDefaultValue)
                {
                    errors.Add(new ValidationError
                    {
                        Property = name,
                        ErrorMessage = $"Required property '{name}' is missing.",
                        ErrorCode = "missing_required_property"
                    });
                }

                continue;
            }

            if (property.ValueKind == JsonValueKind.Null && !IsNullable(parameter))
            {
                errors.Add(new ValidationError
                {
                    Property = name,
                    ErrorMessage = $"Property '{name}' is required and cannot be null.",
                    ErrorCode = "null_required_property"
                });
            }
        }

        return errors;
    }

    private static JsonElement CreateSchema(MethodInfo method, ParameterInfo[] parameters, JsonSerializerOptions serializerOptions)
    {
        var properties = new JsonObject();
        var required = new JsonArray();

        foreach (var parameter in parameters)
        {
            properties[parameter.Name!] = CreateParameterSchema(parameter, serializerOptions);
            if (!parameter.HasDefaultValue && !IsNullable(parameter))
            {
                required.Add(CreateStringNode(parameter.Name!));
            }
        }

        var schema = new JsonObject
        {
            ["type"] = CreateStringNode("object"),
            ["description"] = CreateStringNode(GetDescription(method)),
            ["properties"] = properties,
            ["required"] = required,
            ["additionalProperties"] = CreateBooleanNode(false)
        };

        using var document = JsonDocument.Parse(schema.ToJsonString());

        return document.RootElement.Clone();
    }

    private static JsonObject CreateParameterSchema(ParameterInfo parameter, JsonSerializerOptions serializerOptions)
    {
        var description = GetDescription(parameter);

        var schema = TryCreateParameterSchemaWithAIJsonUtilities(parameter, description, serializerOptions)
            ?? CreateTypeSchema(parameter.ParameterType);

        if (!string.IsNullOrWhiteSpace(description))
        {
            schema["description"] = CreateStringNode(description);
        }

        return schema;
    }

    private static JsonObject? TryCreateParameterSchemaWithAIJsonUtilities(
        ParameterInfo parameter,
        string? description,
        JsonSerializerOptions serializerOptions)
    {
        try
        {
            var schema = AIJsonUtilities.CreateJsonSchema(
                parameter.ParameterType,
                description: description,
                hasDefaultValue: parameter.HasDefaultValue,
                defaultValue: parameter.HasDefaultValue ? parameter.DefaultValue : null,
                serializerOptions: serializerOptions,
                inferenceOptions: new AIJsonSchemaCreateOptions { IncludeSchemaKeyword = false });

            return JsonNode.Parse(schema.GetRawText()) as JsonObject;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static JsonObject CreateTypeSchema(Type type)
    {
        var nullableType = Nullable.GetUnderlyingType(type);
        if (nullableType is not null)
        {
            type = nullableType;
        }

        if (type == typeof(string) || type == typeof(char))
        {
            return new JsonObject { ["type"] = CreateStringNode("string") };
        }

        if (type == typeof(bool))
        {
            return new JsonObject { ["type"] = CreateStringNode("boolean") };
        }

        if (type.IsEnum)
        {
            var enumValues = new JsonArray();
            foreach (var name in Enum.GetNames(type))
            {
                enumValues.Add(CreateStringNode(name));
            }

            return new JsonObject
            {
                ["type"] = CreateStringNode("string"),
                ["enum"] = enumValues
            };
        }

        if (type == typeof(byte) || type == typeof(short) || type == typeof(int) || type == typeof(long) ||
            type == typeof(sbyte) || type == typeof(ushort) || type == typeof(uint) || type == typeof(ulong))
        {
            return new JsonObject { ["type"] = CreateStringNode("integer") };
        }

        if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
        {
            return new JsonObject { ["type"] = CreateStringNode("number") };
        }

        if (type.IsArray)
        {
            return new JsonObject
            {
                ["type"] = CreateStringNode("array"),
                ["items"] = CreateTypeSchema(type.GetElementType() ?? typeof(object))
            };
        }

        return new JsonObject { ["type"] = CreateStringNode("object") };
    }

    private static JsonNode CreateStringNode(string value)
    {
        return JsonNode.Parse($"\"{EscapeJsonString(value)}\"")!;
    }

    private static JsonNode CreateBooleanNode(bool value)
    {
        return JsonNode.Parse(value ? "true" : "false")!;
    }

    private static string EscapeJsonString(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '"':
                    builder.Append("\\\"");
                    break;
                case '\\':
                    builder.Append("\\\\");
                    break;
                case '\b':
                    builder.Append("\\b");
                    break;
                case '\f':
                    builder.Append("\\f");
                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\r':
                    builder.Append("\\r");
                    break;
                case '\t':
                    builder.Append("\\t");
                    break;
                default:
                    if (char.IsControl(ch))
                    {
                        builder.Append("\\u");
                        builder.Append(((int)ch).ToString("x4"));
                    }
                    else
                    {
                        builder.Append(ch);
                    }
                    break;
            }
        }

        return builder.ToString();
    }

    private static async Task<object?> AwaitIfNeededAsync(object? value)
    {
        if (value is Task task)
        {
            await task.ConfigureAwait(false);
            return task.GetType().IsGenericType
                ? task.GetType().GetProperty("Result")?.GetValue(task)
                : null;
        }

        if (value is ValueTask valueTask)
        {
            await valueTask.ConfigureAwait(false);
            return null;
        }

        var valueType = value?.GetType();
        if (valueType is not null && valueType.IsGenericType && valueType.GetGenericTypeDefinition() == typeof(ValueTask<>))
        {
            var valueTaskAsTask = (Task)valueType.GetMethod(nameof(ValueTask.AsTask))!.Invoke(value, null)!;
            await valueTaskAsTask.ConfigureAwait(false);
            return valueTaskAsTask.GetType().GetProperty("Result")?.GetValue(valueTaskAsTask);
        }

        return value;
    }

    private static IEnumerable<MethodInfo> GetAIFunctionMethods(Type toolharnessType)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        return toolharnessType.GetMethods(flags).Where(HasAIFunctionAttribute);
    }

    private static ParameterInfo[] GetModelParameters(MethodInfo method)
    {
        return method.GetParameters()
            .Where(parameter =>
                parameter.ParameterType != typeof(CancellationToken) &&
                parameter.ParameterType != typeof(AIFunctionArguments) &&
                parameter.ParameterType != typeof(FunctionExecutionContext) &&
                parameter.ParameterType != typeof(IServiceProvider))
            .ToArray();
    }

    private static void ThrowIfGeneratorOnlyFeaturesAreUsed(MethodInfo method)
    {
        if (HasAttribute(method, "ConditionalFunctionAttribute"))
        {
            throw new InvalidOperationException(
                $"Function '{GetCapabilityName(method)}' uses [ConditionalFunction], which requires the HPD Agent source generator.");
        }

        foreach (var parameter in method.GetParameters())
        {
            if (HasAttribute(parameter, "ConditionalParameterAttribute"))
            {
                throw new InvalidOperationException(
                    $"Parameter '{parameter.Name}' on function '{GetCapabilityName(method)}' uses [ConditionalParameter], which requires the HPD Agent source generator.");
            }
        }

        if (HasAttribute(method, "ConditionalSkillAttribute") || HasAttribute(method, "ConditionalSubAgentAttribute"))
        {
            throw new InvalidOperationException(
                $"Capability '{GetCapabilityName(method)}' uses conditional metadata, which requires the HPD Agent source generator.");
        }

        if (method.GetCustomAttributes(inherit: false)
            .Any(attribute => attribute.GetType().IsGenericType))
        {
            throw new InvalidOperationException(
                $"Capability '{GetCapabilityName(method)}' uses typed metadata attributes, which require the HPD Agent source generator.");
        }
    }

    private static string GetCapabilityName(MethodInfo method)
    {
        if (HasAttribute(method, "SkillAttribute") && TryInvokeCapabilityMethod<Skill>(method, null, out var skill))
        {
            return skill.Name;
        }

        if (HasAttribute(method, "SubAgentAttribute") && TryInvokeCapabilityMethod<SubAgent>(method, null, out var subAgent))
        {
            return subAgent.Name;
        }

        if (HasAttribute(method, "MultiAgentAttribute"))
        {
            return GetStringProperty(GetAttribute(method, "MultiAgentAttribute"), "Name") ?? method.Name;
        }

        return GetFunctionName(method);
    }

    private static string GetFunctionName(MethodInfo method)
    {
        var attribute = GetAIFunctionAttribute(method);
        return GetStringProperty(attribute, "Name") ?? method.Name;
    }

    private static string GetDescription(MethodInfo method)
    {
        return GetStringProperty(GetAIFunctionAttribute(method), "Description")
            ?? GetDescription((MemberInfo)method)
            ?? $"Function: {method.Name}";
    }

    private static string? GetDescription(ParameterInfo parameter)
    {
        return parameter.GetCustomAttributes(inherit: false)
            .Select(attribute => GetStringProperty(attribute, "Description"))
            .FirstOrDefault(description => !string.IsNullOrWhiteSpace(description));
    }

    private static string? GetDescription(MemberInfo member)
    {
        return member.GetCustomAttributes(inherit: false)
            .Select(attribute => GetStringProperty(attribute, "Description"))
            .FirstOrDefault(description => !string.IsNullOrWhiteSpace(description));
    }

    private static object GetToolKind(MethodInfo method)
    {
        var attribute = GetAIFunctionAttribute(method);
        return attribute?.GetType().GetProperty("Kind")?.GetValue(attribute) ?? ToolKind.Function;
    }

    private static bool HasAIFunctionAttribute(MethodInfo method)
    {
        return GetAIFunctionAttribute(method) is not null;
    }

    private static object? GetAIFunctionAttribute(MethodInfo method)
    {
        return method.GetCustomAttributes(inherit: false)
            .FirstOrDefault(attribute => attribute.GetType().Name.StartsWith("AIFunctionAttribute", StringComparison.Ordinal));
    }

    private static object? GetCollapseAttribute(Type toolharnessType)
    {
        return toolharnessType.GetCustomAttributes(inherit: false)
            .FirstOrDefault(attribute => attribute.GetType().Name == "CollapseAttribute");
    }

    private static bool HasAttribute(MemberInfo member, string attributeTypeName)
    {
        return member.GetCustomAttributes(inherit: false)
            .Any(attribute => attribute.GetType().Name == attributeTypeName);
    }

    private static object? GetAttribute(MemberInfo member, string attributeTypeName)
    {
        return member.GetCustomAttributes(inherit: false)
            .FirstOrDefault(attribute => attribute.GetType().Name.StartsWith(attributeTypeName, StringComparison.Ordinal));
    }

    private static bool HasAttribute(ParameterInfo parameter, string attributeTypeName)
    {
        return parameter.GetCustomAttributes(inherit: false)
            .Any(attribute => attribute.GetType().Name == attributeTypeName);
    }

    private static string? GetStringProperty(object? attribute, string propertyName)
    {
        return attribute?.GetType().GetProperty(propertyName)?.GetValue(attribute) as string;
    }

    private static bool? GetBooleanProperty(object? attribute, string propertyName)
    {
        return attribute?.GetType().GetProperty(propertyName)?.GetValue(attribute) as bool?;
    }

    private static int? GetIntProperty(object? attribute, string propertyName)
    {
        return attribute?.GetType().GetProperty(propertyName)?.GetValue(attribute) as int?;
    }

    private static IEnumerable<MethodInfo> GetCapabilityMethods(Type toolharnessType)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly;
        return toolharnessType.GetMethods(flags).Where(method =>
            HasAIFunctionAttribute(method) ||
            HasAttribute(method, "SkillAttribute") ||
            HasAttribute(method, "SubAgentAttribute") ||
            HasAttribute(method, "MultiAgentAttribute"));
    }

    private static Type? UnwrapReturnType(Type type)
    {
        if (type == typeof(void) || type == typeof(Task) || type == typeof(ValueTask))
        {
            return null;
        }

        if (type.IsGenericType &&
            (type.GetGenericTypeDefinition() == typeof(Task<>) || type.GetGenericTypeDefinition() == typeof(ValueTask<>)))
        {
            return type.GetGenericArguments()[0];
        }

        return type;
    }

    private static bool IsNullable(ParameterInfo parameter)
    {
        var type = parameter.ParameterType;
        if (!type.IsValueType)
        {
            return new NullabilityInfoContext().Create(parameter).ReadState == NullabilityState.Nullable;
        }

        return Nullable.GetUnderlyingType(type) is not null;
    }

    private static ValidationError ToValidationError(HPDToolArgumentException exception)
    {
        return new ValidationError
        {
            Property = exception.PropertyName,
            ErrorMessage = exception.Message,
            ErrorCode = exception.ErrorCode
        };
    }

    private static AgentBuilder CreateStoredSubAgentBuilder(SubAgent subAgent, FunctionExecutionContext? functionContext)
    {
        if (string.IsNullOrWhiteSpace(subAgent.AgentId))
        {
            throw new InvalidOperationException("Stored-agent subagents require AgentId.");
        }

        var builder = new AgentBuilder().WithAgentId(subAgent.AgentId);
        var parentAgentStore = functionContext?.GetParentAgentStore();
        if (parentAgentStore != null)
        {
            builder.WithAgentStore(parentAgentStore);
        }

        return builder;
    }

    private static AgentBuilder CreateInlineSubAgentBuilder(SubAgent subAgent, FunctionExecutionContext? functionContext)
    {
        if (subAgent.AgentConfig == null)
        {
            throw new InvalidOperationException("Inline-config subagents require AgentConfig.");
        }

        var builder = new AgentBuilder(subAgent.AgentConfig);
        var parentChatClient = functionContext?.GetParentChatClient();
        if (subAgent.AgentConfig.ResolveClientConfig(Providers.ProviderClientFamily.Chat) == null && parentChatClient != null)
        {
            builder.WithChatClient(parentChatClient);
        }

        return builder;
    }

    private static T InvokeCapabilityMethod<T>(MethodInfo method, object? instance)
    {
        if (!typeof(T).IsAssignableFrom(UnwrapReturnType(method.ReturnType) ?? method.ReturnType))
        {
            throw new InvalidOperationException(
                $"Method '{method.Name}' must return {typeof(T).Name} or Task<{typeof(T).Name}>.");
        }

        var result = method.Invoke(method.IsStatic ? null : instance, Array.Empty<object?>());
        return AwaitIfNeededAsync(result).GetAwaiter().GetResult() is T value
            ? value
            : throw new InvalidOperationException($"Method '{method.Name}' did not return {typeof(T).Name}.");
    }

    private static bool TryInvokeCapabilityMethod<T>(MethodInfo method, object? instance, [NotNullWhen(true)] out T? value)
    {
        value = default;
        if (!method.IsStatic && instance == null)
        {
            return false;
        }

        try
        {
            value = InvokeCapabilityMethod<T>(method, instance);
            return value is not null;
        }
        catch
        {
            value = default;
            return false;
        }
    }

    private static async Task<object?> InvokeCapabilityMethodAsync(MethodInfo method, object? instance)
    {
        if (method.GetParameters().Length != 0)
        {
            throw new InvalidOperationException($"Method '{method.Name}' must not declare parameters.");
        }

        var result = method.Invoke(method.IsStatic ? null : instance, Array.Empty<object?>());
        return await AwaitIfNeededAsync(result).ConfigureAwait(false);
    }

    private static async Task<string> RunWorkflowAsync(object? workflow, string input, CancellationToken cancellationToken)
    {
        if (workflow == null)
        {
            throw new InvalidOperationException("Multi-agent method returned null.");
        }

        var runAsync = workflow.GetType().GetMethod("RunAsync", new[] { typeof(string), typeof(CancellationToken) })
            ?? throw new InvalidOperationException("Multi-agent workflow must expose RunAsync(string, CancellationToken).");

        var result = await AwaitIfNeededAsync(runAsync.Invoke(workflow, new object?[] { input, cancellationToken })).ConfigureAwait(false);
        if (result == null)
        {
            return string.Empty;
        }

        return result.GetType().GetProperty("FinalAnswer")?.GetValue(result) as string
            ?? result.GetType().GetProperty("Outputs")?.GetValue(result)?.ToString()
            ?? string.Empty;
    }

    private static JsonElement CreateEmptySchema()
    {
        using var document = JsonDocument.Parse(
            """
            {"type":"object","properties":{},"required":[],"additionalProperties":false}
            """);
        return document.RootElement.Clone();
    }

    private static JsonElement CreateQuerySchema()
    {
        using var document = JsonDocument.Parse(
            """
            {"type":"object","properties":{"query":{"type":"string","description":"The user's question or task for the sub-agent."}},"required":["query"],"additionalProperties":false}
            """);
        return document.RootElement.Clone();
    }

    private static JsonElement CreateInputSchema()
    {
        using var document = JsonDocument.Parse(
            """
            {"type":"object","properties":{"input":{"type":"string","description":"The user's request for the workflow."}},"required":["input"],"additionalProperties":false}
            """);
        return document.RootElement.Clone();
    }
}
