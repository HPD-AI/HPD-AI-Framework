using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
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

        if (methods.Any(method => HasAttribute(method, "SkillAttribute")))
        {
            error = $"ToolHarness '{toolharnessType.Name}' contains [Skill] declarations but was not found in ToolHarnessRegistry.All. " +
                "Skills require source generation so their capability graph and Native AOT delegates can be validated at compile time.";
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
            StableIdentity: $"{toolharnessType.Assembly.GetName().Name}:{toolharnessType.FullName ?? toolharnessType.Name}",
            Middleware: RejectReflectionMiddleware(collapseAttribute),
            CreateSubAgentActions: instance => CreateSubAgentActionDescriptors(methods, instance));

        return true;
    }

    private static IReadOnlyList<SubAgentActionDescriptor> CreateSubAgentActionDescriptors(
        IEnumerable<MethodInfo> methods,
        object instance)
    {
        return methods.Where(method => HasAttribute(method, "SubAgentAttribute")).Select(method =>
        {
            var definition = InvokeCapabilityMethod<SubAgent>(method, method.IsStatic ? null : instance);
            return new SubAgentActionDescriptor
            {
                Action = definition.Name,
                Description = definition.Description,
                CapabilityId = CapabilityId.Create($"reflection:{method.DeclaringType?.FullName}.{method.Name}"),
                Definition = definition,
                InvocationModePolicy = definition.InvocationModePolicy,
                InvocationModeHandling = AgentInvocationModeHandling.ToolBody,
                ContextPolicy = definition.ContextPolicy,
                RequiresPermission = true
            };
        }).ToArray();
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
            if (HasAttribute(method, "SubAgentAttribute"))
                continue;
            functions.Add(CreateCapability(method, method.IsStatic ? null : instance, context, serializerOptions));
        }

        if (collapseAttribute is not null)
        {
            functions.Add(CreateToolHarnessContainer(methods, collapseAttribute, serializerOptions));
        }

        return functions;
    }

    private static IReadOnlyList<ToolHarnessMiddlewareDescriptor>? RejectReflectionMiddleware(object? collapseAttribute)
    {
        if (collapseAttribute?.GetType().GetProperty("Middlewares")?.GetValue(collapseAttribute) is not Type[] middlewareTypes ||
            middlewareTypes.Length == 0)
        {
            return null;
        }

        throw new InvalidOperationException(
            "Reflection-registered ToolHarness middleware is not supported. ToolHarness middleware requires " +
            "source-generated stable identities, ownership, and Native AOT activation descriptors.");
    }

    private static AIFunction CreateToolHarnessContainer(
        MethodInfo[] methods,
        object collapseAttribute,
        JsonSerializerOptions serializerOptions)
    {
        var toolharnessType = methods[0].DeclaringType!;
        var toolharnessName = toolharnessType.Name;
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
                    ["ToolHarnessIdentity"] = $"{toolharnessType.Assembly.GetName().Name}:{toolharnessType.FullName ?? toolharnessType.Name}",
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
        var functionAttribute = GetAIFunctionAttribute(method);
        var invocationModePolicy = GetInvocationModePolicy(functionAttribute);
        var invocationModeHandling = GetInvocationModeHandling(functionAttribute);
        var permissionAttribute = method.GetCustomAttribute<RequiresPermissionAttribute>(inherit: false);
        var requiresPermission = permissionAttribute is not null;
        if (permissionAttribute is { PermissionPolicy: not null } or { PermissionInteraction: not null })
            throw new InvalidOperationException(
                $"Reflection-created function '{method.DeclaringType?.FullName}.{method.Name}' uses a custom permission policy or interaction. Register an explicit AIFunction permission descriptor instead of reflection activation.");
        var operationContract = CreateOperationContract(
            parameters, invocationModePolicy, invocationModeHandling, requiresPermission);
        var actionSchema = operationContract is null
            ? default
            : CreateSchema(method, parameters, serializerOptions);

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
                FunctionPermission = requiresPermission
                    ? new AIFunctionPermissionDeclaration
                    {
                        RequiresPermission = true,
                        Scope = permissionAttribute!.PermissionScope ??
                            $"function/{Uri.EscapeDataString(GetFunctionName(method))}",
                        Source = PermissionDeclarationSource.FunctionAttribute
                    }
                    : null,
                InvocationModePolicy = invocationModePolicy,
                InvocationModeHandling = invocationModeHandling,
                OperationContract = operationContract,
                VerifiedActionComposition = operationContract is null
                    ? null
                    : new VerifiedAIFunctionActionComposition(actionSchema, operationContract),
                SerializerOptions = serializerOptions,
                ResultType = resultType,
                Validator = (json, options) => ValidateArguments(json, options, parameters, parameterNames),
                SchemaProvider = () => CreateSchema(method, parameters, serializerOptions),
                AdditionalProperties = new Dictionary<string, object?>
                {
                    ["ParentToolHarness"] = method.DeclaringType?.Name,
                    ["SubAgentMember"] = method.Name,
                    ["SubAgentAssembly"] = method.DeclaringType?.Assembly.GetName().Name ?? string.Empty,
                    ["IsContainer"] = false,
                    ["CapabilityType"] = "Function",
                    ["Kind"] = GetToolKind(method).ToString()
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
                        Workflow = workflow as IMultiAgentWorkflow
                            ?? throw new InvalidOperationException("Multi-agent method must return an IMultiAgentWorkflow implementation."),
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
                FunctionPermission = new AIFunctionPermissionDeclaration
                {
                    RequiresPermission = true,
                    Scope = $"multiagent/{Uri.EscapeDataString(name)}",
                    Source = PermissionDeclarationSource.FrameworkDefault
                },
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
            properties[GetSerializedParameterName(parameter)] = CreateParameterSchema(parameter, serializerOptions);
            if (!parameter.HasDefaultValue && !IsNullable(parameter))
            {
                required.Add(CreateStringNode(GetSerializedParameterName(parameter)));
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

    private static AIFunctionOperationContract? CreateOperationContract(
        ParameterInfo[] parameters,
        AgentInvocationModePolicy defaultPolicy,
        AgentInvocationModeHandling defaultHandling,
        bool requiresPermissionByDefault)
    {
        var analyzed = parameters.Select(parameter => new
        {
            Parameter = parameter,
            Polymorphic = parameter.ParameterType.GetCustomAttribute<JsonPolymorphicAttribute>(inherit: false),
            Cases = parameter.ParameterType.GetCustomAttributes<JsonDerivedTypeAttribute>(inherit: false).ToArray()
        }).ToArray();
        foreach (var union in analyzed.Where(static candidate => candidate.Polymorphic is not null))
        {
            var declaredTypes = union.Cases.Select(static unionCase => unionCase.DerivedType).ToHashSet();
            var undeclared = GetLoadableTypes(union.Parameter.ParameterType.Assembly).FirstOrDefault(type =>
                type != union.Parameter.ParameterType &&
                union.Parameter.ParameterType.IsAssignableFrom(type) &&
                type.GetCustomAttribute<AIFunctionActionAttribute>(inherit: false) is not null &&
                !declaredTypes.Contains(type));
            if (undeclared is not null)
                throw new InvalidOperationException(
                    $"Action type '{undeclared.FullName}' is outside the function's declared closed union.");
        }
        var candidates = analyzed.Where(candidate => candidate.Polymorphic is not null && candidate.Cases.Any(unionCase =>
            unionCase.DerivedType.GetCustomAttribute<AIFunctionActionAttribute>(inherit: false) is not null)).ToArray();
        if (candidates.Length == 0) return null;
        if (candidates.Length != 1)
            throw new InvalidOperationException("An action-contracted function must have exactly one direct closed-union parameter.");

        var candidate = candidates[0];
        var discriminator = candidate.Polymorphic!.TypeDiscriminatorPropertyName;
        if (string.IsNullOrWhiteSpace(discriminator))
            throw new InvalidOperationException("The action union requires a string discriminator property name.");
        var actions = new Dictionary<string, AIFunctionActionPolicy>(StringComparer.Ordinal);
        foreach (var unionCase in candidate.Cases)
        {
            if (unionCase.TypeDiscriminator is not string serialized || string.IsNullOrWhiteSpace(serialized))
                throw new InvalidOperationException("Every action union case requires a non-empty string discriminator.");
            var declaration = unionCase.DerivedType.GetCustomAttribute<AIFunctionActionAttribute>(inherit: false)
                ?? throw new InvalidOperationException($"Action type '{unionCase.DerivedType.FullName}' requires AIFunctionActionAttribute.");
            if (declaration.PermissionPolicy is not null || declaration.PermissionInteraction is not null)
                throw new InvalidOperationException(
                    $"Reflection-created action '{unionCase.DerivedType.FullName}' uses a custom permission policy or interaction. Register an explicit AIFunction permission descriptor instead of reflection activation.");
            if (!string.Equals(declaration.Action, serialized, StringComparison.Ordinal))
                throw new InvalidOperationException($"Action declaration '{declaration.Action}' does not match serializer discriminator '{serialized}'.");
            var policy = declaration.InvocationModePolicy switch
            {
                AIFunctionActionInvocationModePolicy.Inherit => defaultPolicy,
                AIFunctionActionInvocationModePolicy.SynchronousOnly => AgentInvocationModePolicy.SynchronousOnly,
                AIFunctionActionInvocationModePolicy.BackgroundOnly => AgentInvocationModePolicy.BackgroundOnly,
                AIFunctionActionInvocationModePolicy.ModelChoice => AgentInvocationModePolicy.ModelChoice,
                _ => throw new InvalidOperationException("Unsupported action invocation-mode policy.")
            };
            var handling = declaration.InvocationModeHandling switch
            {
                AIFunctionActionInvocationModeHandling.Inherit => defaultHandling,
                AIFunctionActionInvocationModeHandling.Runtime => AgentInvocationModeHandling.Runtime,
                AIFunctionActionInvocationModeHandling.ToolBody => AgentInvocationModeHandling.ToolBody,
                _ => throw new InvalidOperationException("Unsupported action invocation-mode handling.")
            };
            var requiresPermission = declaration.Permission switch
            {
                PermissionRequirement.Inherit => requiresPermissionByDefault,
                PermissionRequirement.Required => true,
                PermissionRequirement.NotRequired => false,
                _ => throw new InvalidOperationException("Unsupported action permission requirement.")
            };
            if (!actions.TryAdd(serialized, new AIFunctionActionPolicy
                {
                    InvocationModePolicy = policy,
                    InvocationModeHandling = handling,
                    Permission = new AIFunctionPermissionDeclaration
                    {
                        RequiresPermission = requiresPermission,
                        Scope = declaration.PermissionScope ??
                            $"function/{Uri.EscapeDataString(GetFunctionName(candidate.Parameter.Member as MethodInfo ?? throw new InvalidOperationException()))}/action/{Uri.EscapeDataString(serialized)}",
                        Source = declaration.Permission == PermissionRequirement.Inherit
                            ? PermissionDeclarationSource.FunctionAttribute
                            : PermissionDeclarationSource.ActionOverride
                    }
                }))
                throw new InvalidOperationException($"Duplicate action discriminator '{serialized}'.");
        }
        return new AIFunctionOperationContract
        {
            ActionArgumentName = GetSerializedParameterName(candidate.Parameter),
            Discriminator = discriminator,
            Actions = actions
        };
    }

    private static string GetSerializedParameterName(ParameterInfo parameter) =>
        parameter.Name ?? throw new InvalidOperationException("Model-facing parameters require a serialized name.");

    private static IEnumerable<Type> GetLoadableTypes(Assembly assembly)
    {
        try
        {
            return assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException exception)
        {
            return exception.Types.OfType<Type>();
        }
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
