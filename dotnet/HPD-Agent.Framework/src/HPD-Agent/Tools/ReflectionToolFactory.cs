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
            CreateFunctions: (instance, context, serialization) => CreateFunctions(methods, collapseAttribute, instance, context, serialization),
            GetReferencedToolHarnesses: () => Array.Empty<string>(),
            GetReferencedFunctions: () => new Dictionary<string, string[]>(),
            HasDescription: collapseAttribute is not null,
            Description: GetStringProperty(collapseAttribute, "Description"),
            FunctionResult: GetStringProperty(collapseAttribute, "FunctionResult"),
            SystemPrompt: GetStringProperty(collapseAttribute, "SystemPrompt"),
            FunctionNames: methods.Select(GetCapabilityName).ToArray(),
            CollapseMiddlewareFactories: CreateCollapseMiddlewareFactories(collapseAttribute));

        return true;
    }

    [RequiresUnreferencedCode(ReflectionRequiresUnreferencedCodeMessage)]
    [RequiresDynamicCode("Reflection tool registration uses runtime method invocation and System.Text.Json runtime type metadata.")]
    private static List<AIFunction> CreateFunctions(
        MethodInfo[] methods,
        object? collapseAttribute,
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

        if (collapseAttribute is not null)
        {
            functions.Add(CreateToolHarnessContainer(methods, collapseAttribute, serializerOptions));
        }

        return functions;
    }

    private static IReadOnlyList<Func<IAgentMiddleware>>? CreateCollapseMiddlewareFactories(object? collapseAttribute)
    {
        if (collapseAttribute?.GetType().GetProperty("Middlewares")?.GetValue(collapseAttribute) is not Type[] middlewareTypes ||
            middlewareTypes.Length == 0)
        {
            return null;
        }

        var factories = new List<Func<IAgentMiddleware>>(middlewareTypes.Length);
        foreach (var middlewareType in middlewareTypes)
        {
            if (!typeof(IAgentMiddleware).IsAssignableFrom(middlewareType))
            {
                throw new InvalidOperationException(
                    $"Collapse middleware '{middlewareType.FullName}' must implement {nameof(IAgentMiddleware)}.");
            }

            if (middlewareType.GetConstructor(Type.EmptyTypes) is null)
            {
                throw new InvalidOperationException(
                    $"Reflection-registered collapse middleware '{middlewareType.FullName}' must have a public parameterless constructor. " +
                    "Use builder middleware configuration or the source generator for configured constructors.");
            }

            factories.Add(() => (IAgentMiddleware)(Activator.CreateInstance(middlewareType)
                ?? throw new InvalidOperationException($"Could not create collapse middleware '{middlewareType.FullName}'.")));
        }

        return factories;
    }

    private static AIFunction CreateToolHarnessContainer(
        MethodInfo[] methods,
        object collapseAttribute,
        JsonSerializerOptions serializerOptions)
    {
        var toolharnessName = methods[0].DeclaringType!.Name;
        var childFunctions = methods.Select(GetCapabilityName).ToArray();
        var description = GetStringProperty(collapseAttribute, "Description") ?? $"Tools in {toolharnessName}.";
        var functionResult = GetStringProperty(collapseAttribute, "FunctionResult");
        var systemPrompt = GetStringProperty(collapseAttribute, "SystemPrompt");
        var functionList = string.Join(", ", childFunctions);
        var result = string.IsNullOrWhiteSpace(functionResult)
            ? $"{toolharnessName} expanded. Available functions: {functionList}"
            : $"{toolharnessName} expanded. Available functions: {functionList}\n\n{functionResult}";

        return HPDAIFunctionFactory.Create(
            (_, _, _) => Task.FromResult<object?>(result),
            new HPDAIFunctionFactoryOptions
            {
                Name = toolharnessName,
                Description = $"{description} Contains {childFunctions.Length} functions: {functionList}",
                SerializerOptions = serializerOptions,
                ResultType = typeof(string),
                SchemaProvider = CreateEmptySchema,
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["IsContainer"] = true,
                    ["IsToolHarnessContainer"] = true,
                    ["ToolHarnessName"] = toolharnessName,
                    ["ChildFunctions"] = childFunctions,
                    ["FunctionResult"] = functionResult,
                    ["SystemPrompt"] = systemPrompt,
                    ["CapabilityType"] = "ToolHarnessContainer"
                }
            });
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
                InvocationModePolicy = GetInvocationModePolicy(GetAIFunctionAttribute(method)),
                InvocationModeHandling = GetInvocationModeHandling(GetAIFunctionAttribute(method)),
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
        var registrationDefinition = InvokeCapabilityMethod<SubAgent>(method, instance);

        return HPDAIFunctionFactory.Create(
            async (arguments, functionContext, cancellationToken) =>
            {
                var subAgent = InvokeCapabilityMethod<SubAgent>(method, instance);
                var jsonArgs = arguments.GetJson();
                var input = jsonArgs.TryGetProperty("input", out var inputProperty)
                    ? inputProperty.GetString() ?? string.Empty
                    : string.Empty;
                var taskName = jsonArgs.TryGetProperty("taskName", out var taskNameProperty)
                    ? taskNameProperty.GetString() ?? string.Empty
                    : string.Empty;
                var requestedMode = AgentInvocationModes.ReadRequestedMode(jsonArgs);

                var result = await SubAgentRuntime.InvokeAsync(
                    new SubAgentRuntime.SubAgentInvocationRequest
                    {
                        Definition = subAgent,
                        Input = input,
                        TaskName = taskName,
                        ParentContext = functionContext,
                        RequestedMode = requestedMode
                    },
                    cancellationToken).ConfigureAwait(false);

                return result.ToToolResult();
            },
            new HPDAIFunctionFactoryOptions
            {
                Name = registrationDefinition.Name,
                Description = registrationDefinition.Description,
                RequiresPermission = true,
                SerializerOptions = serializerOptions,
                ResultType = typeof(object),
                SchemaProvider = () => CreateSubAgentInputSchema(
                    registrationDefinition.InvocationModePolicy == AgentInvocationModePolicy.ModelChoice),
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
        var invocationModePolicy = GetInvocationModePolicy(attribute);

        return HPDAIFunctionFactory.Create(
            async (arguments, functionContext, cancellationToken) =>
            {
                var jsonArgs = arguments.GetJson();
                var input = jsonArgs.TryGetProperty("input", out var inputProperty)
                    ? inputProperty.GetString() ?? string.Empty
                    : string.Empty;
                var requestedMode = AgentInvocationModes.ReadRequestedMode(jsonArgs);

                var workflow = await InvokeCapabilityMethodAsync(method, instance).ConfigureAwait(false);
                var result = await MultiAgentRuntime.InvokeAsync(
                    new MultiAgentRuntime.MultiAgentInvocationRequest
                    {
                        Workflow = workflow ?? throw new InvalidOperationException("Multi-agent method returned null."),
                        Name = name,
                        Input = input,
                        ParentContext = functionContext,
                        StreamEvents = streamEvents,
                        InvocationModePolicy = invocationModePolicy,
                        RequestedMode = requestedMode
                    },
                    cancellationToken).ConfigureAwait(false);

                return result.ToToolResult();
            },
            new HPDAIFunctionFactoryOptions
            {
                Name = name,
                Description = description,
                RequiresPermission = true,
                SerializerOptions = serializerOptions,
                ResultType = typeof(object),
                SchemaProvider = () => CreateInputSchema(
                    invocationModePolicy == AgentInvocationModePolicy.ModelChoice),
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["CapabilityType"] = "MultiAgent",
                    ["IsContainer"] = false,
                    ["IsMultiAgent"] = true,
                    ["ParentToolHarness"] = method.DeclaringType?.Name,
                    ["StreamEvents"] = streamEvents,
                    ["TimeoutSeconds"] = timeoutSeconds,
                    ["InvocationModePolicy"] = invocationModePolicy.ToString(),
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

    private static AgentInvocationModePolicy GetInvocationModePolicy(object? attribute)
    {
        var value = attribute?.GetType().GetProperty("InvocationModePolicy")?.GetValue(attribute);
        return Enum.TryParse<AgentInvocationModePolicy>(value?.ToString(), out var policy)
            ? policy
            : AgentInvocationModePolicy.SynchronousOnly;
    }

    private static AgentInvocationModeHandling GetInvocationModeHandling(object? attribute)
    {
        var value = attribute?.GetType().GetProperty("InvocationModeHandling")?.GetValue(attribute);
        return Enum.TryParse<AgentInvocationModeHandling>(value?.ToString(), out var handling)
            ? handling
            : AgentInvocationModeHandling.Runtime;
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

    private static JsonElement CreateEmptySchema()
    {
        using var document = JsonDocument.Parse(
            """
            {"type":"object","properties":{},"required":[],"additionalProperties":false}
            """);
        return document.RootElement.Clone();
    }

    private static JsonElement CreateSubAgentInputSchema(bool includeInvocationMode = false)
    {
        var schema = includeInvocationMode
            ? """
              {"type":"object","properties":{"taskName":{"type":"string","description":"A short name for this delegated task, used to identify its thread in the current session."},"input":{"type":"string","description":"The user's question or task for the sub-agent. Pass the full request here."},"invocationMode":{"type":"string","enum":["synchronous","background"],"description":"Whether to wait for the result now or run in the background. Use synchronous unless the task can continue independently."}},"required":["taskName","input"],"additionalProperties":false}
              """
            : """
              {"type":"object","properties":{"taskName":{"type":"string","description":"A short name for this delegated task, used to identify its thread in the current session."},"input":{"type":"string","description":"The user's question or task for the sub-agent. Pass the full request here."}},"required":["taskName","input"],"additionalProperties":false}
              """;
        using var document = JsonDocument.Parse(schema);
        return document.RootElement.Clone();
    }

    private static JsonElement CreateInputSchema(bool includeInvocationMode = false)
    {
        var schema = includeInvocationMode
            ? """
              {"type":"object","properties":{"input":{"type":"string","description":"The user's request for the workflow."},"invocationMode":{"type":"string","enum":["synchronous","background"],"description":"Whether to wait for the workflow result now or run it in the background. Use synchronous unless the workflow can continue independently."}},"required":["input"],"additionalProperties":false}
              """
            : """
              {"type":"object","properties":{"input":{"type":"string","description":"The user's request for the workflow."}},"required":["input"],"additionalProperties":false}
              """;
        using var document = JsonDocument.Parse(schema);
        return document.RootElement.Clone();
    }
}
