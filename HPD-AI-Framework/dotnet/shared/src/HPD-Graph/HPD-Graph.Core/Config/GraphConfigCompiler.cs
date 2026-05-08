using System.Globalization;
using System.Text.Json;
using HPDAgent.Graph.Abstractions.Artifacts;
using HPDAgent.Graph.Abstractions.Caching;
using HPDAgent.Graph.Abstractions.Config;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Graph;
using HPDAgent.Graph.Abstractions.Serialization;
using HPDAgent.Graph.Abstractions.Validation;
using HPDAgent.Graph.Core.Validation;
using RuntimeGraph = HPDAgent.Graph.Abstractions.Graph.Graph;

namespace HPDAgent.Graph.Core.Config;

/// <summary>
/// Compiles serializable graph definitions into runtime graph instances.
/// </summary>
public sealed class GraphConfigCompiler
{
    private readonly GraphConfigCompilerOptions _options;

    public GraphConfigCompiler(GraphConfigCompilerOptions? options = null)
    {
        _options = options ?? new GraphConfigCompilerOptions();
    }

    public RuntimeGraph Compile(GraphConfig config)
    {
        ArgumentNullException.ThrowIfNull(config);

        ValidateGraphConfig(config);

        var nodes = config.Nodes.Values
            .Select(CompileNode)
            .ToList();

        EnsureEndpointNode(nodes, config.EntryNodeId, NodeType.Start, "Start");
        EnsureEndpointNode(nodes, config.ExitNodeId, NodeType.End, "End");

        return new RuntimeGraph
        {
            Id = config.GraphId,
            Name = config.Name,
            Version = config.GraphVersion,
            Nodes = nodes,
            Edges = config.Edges.Select(CompileEdge).ToList(),
            EntryNodeId = config.EntryNodeId,
            ExitNodeId = config.ExitNodeId,
            Metadata = config.Metadata,
            MaxIterations = config.MaxIterations,
            ExecutionTimeout = config.ExecutionTimeout,
            CloningPolicy = CompileCloningPolicy(config.CloningPolicy),
            IterationOptions = CompileIterationOptions(config.IterationOptions)
        };
    }

    private static void ValidateGraphConfig(GraphConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.GraphId))
        {
            throw new InvalidOperationException("GraphConfig.GraphId is required.");
        }

        if (string.IsNullOrWhiteSpace(config.Name))
        {
            throw new InvalidOperationException("GraphConfig.Name is required.");
        }

        var nodeIds = config.Nodes.Keys.ToHashSet(StringComparer.Ordinal);
        nodeIds.Add(config.EntryNodeId);
        nodeIds.Add(config.ExitNodeId);

        foreach (var edge in config.Edges)
        {
            if (!nodeIds.Contains(edge.From))
            {
                throw new InvalidOperationException($"Edge references missing source node '{edge.From}'.");
            }

            if (!nodeIds.Contains(edge.To))
            {
                throw new InvalidOperationException($"Edge references missing target node '{edge.To}'.");
            }
        }
    }

    private static void EnsureEndpointNode(List<Node> nodes, string id, NodeType type, string name)
    {
        if (nodes.Any(node => string.Equals(node.Id, id, StringComparison.Ordinal)))
        {
            return;
        }

        nodes.Add(new Node
        {
            Id = id,
            Name = name,
            Type = type
        });
    }

    private RuntimeGraph CompileSubGraph(GraphConfig subGraph)
    {
        return Compile(subGraph);
    }

    private Node CompileNode(NodeConfig config)
    {
        return new Node
        {
            Id = config.Id,
            Name = config.Name,
            Type = CompileNodeType(config.Type),
            HandlerName = config.HandlerName,
            Config = CompileNodeConfig(config.Config),
            Timeout = config.Timeout,
            RetryPolicy = CompileRetryPolicy(config.RetryPolicy),
            ErrorPolicy = CompileErrorPolicy(config.ErrorPolicy),
            SuspensionOptions = CompileSuspensionOptions(config.SuspensionOptions),
            EnableCheckpointing = config.EnableCheckpointing,
            MaxExecutions = config.MaxExecutions,
            MaxParallelExecutions = config.MaxParallelExecutions,
            OutputPortCount = config.OutputPortCount,
            SubGraphRef = config.SubGraphRef,
            SubGraph = config.SubGraph is null ? null : CompileSubGraph(config.SubGraph),
            MapProcessorGraph = config.MapProcessorGraph is null ? null : CompileSubGraph(config.MapProcessorGraph),
            MapProcessorGraphRef = config.MapProcessorGraphRef,
            MaxParallelMapTasks = config.MaxParallelMapTasks,
            MapInputChannel = config.MapInputChannel,
            MapOutputChannel = config.MapOutputChannel,
            MapErrorMode = CompileMapErrorMode(config.MapErrorMode),
            MapItemType = config.MapItemType,
            MapResultType = config.MapResultType,
            MapProcessorGraphs = config.MapProcessorGraphs?.ToDictionary(
                kvp => kvp.Key,
                kvp => CompileSubGraph(kvp.Value),
                StringComparer.Ordinal),
            MapRouterName = config.MapRouterName,
            MapDefaultGraph = config.MapDefaultGraph is null ? null : CompileSubGraph(config.MapDefaultGraph),
            ProducesArtifact = config.Artifacts?.ProducesArtifact is null ? null : ArtifactKey.Parse(config.Artifacts.ProducesArtifact),
            RequiresArtifacts = config.Artifacts?.RequiresArtifacts?.Select(ArtifactKey.Parse).ToList(),
            Partitions = CompilePartitionDefinition(config.Partitions),
            PartitionDependencies = CompilePartitionDependencies(config.PartitionDependencies),
            Cache = CompileCacheOptions(config.Cache),
            ArtifactNamespace = config.ArtifactNamespace,
            InputSchemas = CompileInputSchemas(config.InputSchemas),
            Metadata = config.Metadata
        };
    }

    private static JsonElement? CompileNodeConfig(JsonElement? config)
    {
        return config?.Clone();
    }

    private static Edge CompileEdge(EdgeConfig config)
    {
        return new Edge
        {
            From = config.From,
            To = config.To,
            FromPort = config.FromPort,
            ToPort = config.ToPort,
            Priority = config.Priority,
            Condition = CompileCondition(config.Condition),
            Delay = config.Delay,
            Schedule = CompileSchedule(config.Schedule),
            RetryPolicy = CompileEdgeRetryPolicy(config.RetryPolicy),
            CloningPolicy = config.CloningPolicy is null ? null : CompileCloningPolicy(config.CloningPolicy),
            Metadata = config.Metadata
        };
    }

    private static NodeType CompileNodeType(NodeKindConfig type) => type switch
    {
        NodeKindConfig.Start => NodeType.Start,
        NodeKindConfig.End => NodeType.End,
        NodeKindConfig.Handler => NodeType.Handler,
        NodeKindConfig.Router => NodeType.Router,
        NodeKindConfig.SubGraph => NodeType.SubGraph,
        NodeKindConfig.Map => NodeType.Map,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported node type.")
    };

    private RetryPolicy? CompileRetryPolicy(RetryPolicyConfig? config)
    {
        if (config is null)
        {
            return null;
        }

        return new RetryPolicy
        {
            MaxAttempts = config.MaxAttempts,
            InitialDelay = config.InitialDelay,
            Strategy = config.Strategy switch
            {
                BackoffStrategyConfig.Constant => BackoffStrategy.Constant,
                BackoffStrategyConfig.Exponential => BackoffStrategy.Exponential,
                BackoffStrategyConfig.Linear => BackoffStrategy.Linear,
                BackoffStrategyConfig.JitteredExponential => BackoffStrategy.JitteredExponential,
                _ => BackoffStrategy.Exponential
            },
            MaxDelay = config.MaxDelay,
            RetryableExceptions = ResolveExceptionTypes(config.RetryableExceptionTypeNames)
        };
    }

    private IReadOnlyList<Type>? ResolveExceptionTypes(IReadOnlyList<string>? typeNames)
    {
        if (typeNames is null || typeNames.Count == 0)
        {
            return null;
        }

        var types = new List<Type>(typeNames.Count);
        foreach (var typeName in typeNames)
        {
            var type = ResolveType(typeName);
            if (type is null)
            {
                throw new InvalidOperationException($"Retryable exception type '{typeName}' could not be resolved.");
            }

            if (!typeof(Exception).IsAssignableFrom(type))
            {
                throw new InvalidOperationException($"Retryable exception type '{typeName}' is not an exception type.");
            }

            types.Add(type);
        }

        return types;
    }

    private static ErrorPropagationPolicy? CompileErrorPolicy(ErrorPropagationPolicyConfig? config)
    {
        if (config is null)
        {
            return null;
        }

        return new ErrorPropagationPolicy
        {
            Mode = config.Mode switch
            {
                PropagationModeConfig.StopGraph => PropagationMode.StopGraph,
                PropagationModeConfig.SkipDependents => PropagationMode.SkipDependents,
                PropagationModeConfig.ExecuteFallback => PropagationMode.ExecuteFallback,
                PropagationModeConfig.Isolate => PropagationMode.Isolate,
                _ => PropagationMode.StopGraph
            },
            AffectedNodes = config.AffectedNodes,
            FallbackNodeId = config.FallbackNodeId
        };
    }

    private static SuspensionOptions? CompileSuspensionOptions(SuspensionOptionsConfig? config)
    {
        if (config is null)
        {
            return null;
        }

        return new SuspensionOptions
        {
            ActiveWaitTimeout = config.ActiveWaitTimeout,
            EmitEvents = config.EmitEvents,
            SaveCheckpointFirst = config.SaveCheckpointFirst
        };
    }

    private static EdgeCondition? CompileCondition(ConditionConfig? config)
    {
        if (config is null)
        {
            return null;
        }

        return config.Type switch
        {
            ConditionKindConfig.Always => new EdgeCondition { Type = ConditionType.Always },
            ConditionKindConfig.Default => new EdgeCondition { Type = ConditionType.Default },
            ConditionKindConfig.FieldEquals => FieldCondition(ConditionType.FieldEquals, config),
            ConditionKindConfig.FieldNotEquals => FieldCondition(ConditionType.FieldNotEquals, config),
            ConditionKindConfig.FieldGreaterThan => FieldCondition(ConditionType.FieldGreaterThan, config),
            ConditionKindConfig.FieldGreaterThanOrEqual => FieldCondition(ConditionType.FieldGreaterThanOrEqual, config),
            ConditionKindConfig.FieldLessThan => FieldCondition(ConditionType.FieldLessThan, config),
            ConditionKindConfig.FieldLessThanOrEqual => FieldCondition(ConditionType.FieldLessThanOrEqual, config),
            ConditionKindConfig.FieldContains => FieldCondition(ConditionType.FieldContains, config),
            ConditionKindConfig.FieldContainsAny => FieldCondition(ConditionType.FieldContainsAny, config),
            ConditionKindConfig.FieldContainsAll => FieldCondition(ConditionType.FieldContainsAll, config),
            ConditionKindConfig.FieldStartsWith => FieldCondition(ConditionType.FieldStartsWith, config) with
            {
                RegexOptions = config.IgnoreCase ? "IgnoreCase" : null
            },
            ConditionKindConfig.FieldEndsWith => FieldCondition(ConditionType.FieldEndsWith, config) with
            {
                RegexOptions = config.IgnoreCase ? "IgnoreCase" : null
            },
            ConditionKindConfig.FieldMatchesRegex => FieldCondition(ConditionType.FieldMatchesRegex, config) with
            {
                RegexOptions = config.IgnoreCase ? "IgnoreCase" : null
            },
            ConditionKindConfig.FieldExists => FieldCondition(ConditionType.FieldExists, config),
            ConditionKindConfig.FieldNotExists => FieldCondition(ConditionType.FieldNotExists, config),
            ConditionKindConfig.FieldEmpty => FieldCondition(ConditionType.FieldIsEmpty, config),
            ConditionKindConfig.FieldNotEmpty => FieldCondition(ConditionType.FieldIsNotEmpty, config),
            ConditionKindConfig.UpstreamOneSuccess => new EdgeCondition { Type = ConditionType.UpstreamOneSuccess },
            ConditionKindConfig.UpstreamAllDone => new EdgeCondition { Type = ConditionType.UpstreamAllDone },
            ConditionKindConfig.UpstreamAllDoneOneSuccess => new EdgeCondition { Type = ConditionType.UpstreamAllDoneOneSuccess },
            ConditionKindConfig.All => new EdgeCondition
            {
                Type = ConditionType.And,
                Conditions = config.All?.Select(CompileCondition).Where(c => c != null).Cast<EdgeCondition>().ToList()
            },
            ConditionKindConfig.Any => new EdgeCondition
            {
                Type = ConditionType.Or,
                Conditions = config.Any?.Select(CompileCondition).Where(c => c != null).Cast<EdgeCondition>().ToList()
            },
            ConditionKindConfig.Not => new EdgeCondition
            {
                Type = ConditionType.Not,
                Conditions = config.Not is null ? Array.Empty<EdgeCondition>() : new[] { CompileCondition(config.Not)! }
            },
            _ => throw new ArgumentOutOfRangeException(nameof(config), config.Type, "Unsupported condition type.")
        };
    }

    private static MapErrorMode? CompileMapErrorMode(MapErrorModeConfig? config)
    {
        return config switch
        {
            null => null,
            MapErrorModeConfig.FailFast => MapErrorMode.FailFast,
            MapErrorModeConfig.ContinueWithNulls => MapErrorMode.ContinueWithNulls,
            MapErrorModeConfig.ContinueOmitFailures => MapErrorMode.ContinueOmitFailures,
            _ => MapErrorMode.FailFast
        };
    }

    private static PartitionDefinition? CompilePartitionDefinition(PartitionDefinitionConfig? config)
    {
        if (config is null)
        {
            return null;
        }

        if (config.Definition is null)
        {
            throw new InvalidOperationException("Partition definition config requires a Definition payload.");
        }

        var json = config.Definition.Value.GetRawText();
        PartitionDefinition? definition = config.Type switch
        {
            PartitionKindConfig.Static => JsonSerializer.Deserialize(
                json,
                GraphConfigJsonSerializerContext.Default.StaticPartitionDefinition),
            PartitionKindConfig.Time => JsonSerializer.Deserialize(
                json,
                GraphConfigJsonSerializerContext.Default.TimePartitionDefinition),
            PartitionKindConfig.Multi => JsonSerializer.Deserialize(
                json,
                GraphConfigJsonSerializerContext.Default.MultiPartitionDefinition),
            _ => throw new ArgumentOutOfRangeException(nameof(config), config.Type, "Unsupported partition definition type.")
        };

        return definition ?? throw new InvalidOperationException("Partition definition payload could not be deserialized.");
    }

    private PartitionDependencyMapping? CompilePartitionDependencies(PartitionDependencyConfig? config)
    {
        if (config is null)
        {
            return null;
        }

        if (config.Custom is not null)
        {
            var mapping = _options.ResolvePartitionDependencyMapping(config.Custom.Name, config.Custom.Arguments);
            return mapping.CustomDescriptor is not null
                ? mapping
                : mapping with { CustomDescriptor = config.Custom };
        }

        var kind = config.Type ?? ReadPartitionDependencyKind(config.Mapping);
        return kind switch
        {
            PartitionDependencyMappingKindConfig.WeeklyFromDaily => PartitionDependencyMapping.WeeklyFromDaily(),
            PartitionDependencyMappingKindConfig.MonthlyFromDaily => PartitionDependencyMapping.MonthlyFromDaily(),
            PartitionDependencyMappingKindConfig.QuarterlyFromMonthly => PartitionDependencyMapping.QuarterlyFromMonthly(),
            PartitionDependencyMappingKindConfig.YearlyFromMonthly => PartitionDependencyMapping.YearlyFromMonthly(),
            null => throw new InvalidOperationException("Partition dependency config requires Type or Mapping.type."),
            _ => throw new ArgumentOutOfRangeException(nameof(config), kind, "Unsupported partition dependency mapping type.")
        };
    }

    private static PartitionDependencyMappingKindConfig? ReadPartitionDependencyKind(JsonElement? mapping)
    {
        if (mapping is null || mapping.Value.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        if (!mapping.Value.TryGetProperty("type", out var typeElement) || typeElement.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        return Enum.TryParse<PartitionDependencyMappingKindConfig>(typeElement.GetString(), ignoreCase: true, out var kind)
            ? kind
            : null;
    }

    private static CacheOptions? CompileCacheOptions(CacheOptionsConfig? config)
    {
        if (config is null || !config.Enabled)
        {
            return null;
        }

        return new CacheOptions
        {
            Strategy = ParseEnum(config.Strategy, CacheKeyStrategy.InputsAndCode),
            Ttl = config.Ttl,
            Invalidation = ParseEnum(config.Invalidation, CacheInvalidation.OnCodeChange)
        };
    }

    private IReadOnlyDictionary<string, InputSchema>? CompileInputSchemas(
        IReadOnlyDictionary<string, InputSchemaConfig>? schemas)
    {
        if (schemas is null || schemas.Count == 0)
        {
            return null;
        }

        return schemas.ToDictionary(
            kvp => kvp.Key,
            kvp => CompileInputSchema(kvp.Key, kvp.Value),
            StringComparer.Ordinal);
    }

    private InputSchema CompileInputSchema(string inputName, InputSchemaConfig config)
    {
        var type = ResolveType(config.TypeName)
            ?? throw new InvalidOperationException($"Input schema type '{config.TypeName}' for '{inputName}' could not be resolved.");

        return new InputSchema
        {
            Type = type,
            Required = config.Required,
            DefaultValue = config.DefaultValue is null
                ? null
                : CompileDefaultValue(inputName, type, config.DefaultValue.Value),
            Validator = CompileInputValidator(inputName, config.Constraints)
        };
    }

    private static object? CompileDefaultValue(string inputName, Type type, JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (type == typeof(string))
        {
            return value.GetString();
        }

        if (type == typeof(bool) && value.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            return value.GetBoolean();
        }

        if (type == typeof(byte) && value.TryGetByte(out var byteValue))
        {
            return byteValue;
        }

        if (type == typeof(sbyte) && value.TryGetSByte(out var sbyteValue))
        {
            return sbyteValue;
        }

        if (type == typeof(short) && value.TryGetInt16(out var shortValue))
        {
            return shortValue;
        }

        if (type == typeof(ushort) && value.TryGetUInt16(out var ushortValue))
        {
            return ushortValue;
        }

        if (type == typeof(int) && value.TryGetInt32(out var intValue))
        {
            return intValue;
        }

        if (type == typeof(uint) && value.TryGetUInt32(out var uintValue))
        {
            return uintValue;
        }

        if (type == typeof(long) && value.TryGetInt64(out var longValue))
        {
            return longValue;
        }

        if (type == typeof(ulong) && value.TryGetUInt64(out var ulongValue))
        {
            return ulongValue;
        }

        if (type == typeof(float) && value.TryGetSingle(out var floatValue))
        {
            return floatValue;
        }

        if (type == typeof(double) && value.TryGetDouble(out var doubleValue))
        {
            return doubleValue;
        }

        if (type == typeof(decimal) && value.TryGetDecimal(out var decimalValue))
        {
            return decimalValue;
        }

        if (type == typeof(Guid) && value.ValueKind == JsonValueKind.String && value.TryGetGuid(out var guidValue))
        {
            return guidValue;
        }

        if (type == typeof(DateTime) && value.ValueKind == JsonValueKind.String && value.TryGetDateTime(out var dateTimeValue))
        {
            return dateTimeValue;
        }

        if (type == typeof(DateTimeOffset) && value.ValueKind == JsonValueKind.String && value.TryGetDateTimeOffset(out var dateTimeOffsetValue))
        {
            return dateTimeOffsetValue;
        }

        if (type == typeof(TimeSpan) &&
            value.ValueKind == JsonValueKind.String &&
            TimeSpan.TryParse(value.GetString(), CultureInfo.InvariantCulture, out var timeSpanValue))
        {
            return timeSpanValue;
        }

        throw new InvalidOperationException(
            $"Input schema default value for '{inputName}' could not be converted to '{type.FullName}'.");
    }

    private IInputValidator? CompileInputValidator(string inputName, JsonElement? constraints)
    {
        if (constraints is null || constraints.Value.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (constraints.Value.ValueKind != JsonValueKind.Object ||
            !constraints.Value.TryGetProperty("type", out var typeElement) ||
            typeElement.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"Input schema constraints for '{inputName}' must be an object with a string 'type'.");
        }

        var type = typeElement.GetString();
        return type?.ToLowerInvariant() switch
        {
            "url" => InputValidators.Url(),
            "email" => InputValidators.Email(),
            "regex" => InputValidators.Regex(RequiredString(constraints.Value, "pattern", inputName)),
            "range" => InputValidators.Range(RequiredInt(constraints.Value, "min", inputName), RequiredInt(constraints.Value, "max", inputName)),
            "stringlength" => InputValidators.StringLength(RequiredInt(constraints.Value, "minLength", inputName), RequiredInt(constraints.Value, "maxLength", inputName)),
            "collectioncount" => InputValidators.CollectionCount(RequiredInt(constraints.Value, "minCount", inputName), RequiredInt(constraints.Value, "maxCount", inputName)),
            "enum" => CompileEnumValidator(inputName, RequiredString(constraints.Value, "enumType", inputName)),
            "custom" => CompileCustomInputValidator(inputName, constraints.Value),
            _ => throw new InvalidOperationException($"Input schema constraint type '{type}' for '{inputName}' is not supported.")
        };
    }

    private IInputValidator CompileCustomInputValidator(string inputName, JsonElement constraints)
    {
        var name = RequiredString(constraints, "name", inputName);
        var arguments = constraints.TryGetProperty("arguments", out var argumentsElement)
            ? argumentsElement.Clone()
            : (JsonElement?)null;

        var validator = _options.ResolveInputValidator(name, arguments);
        return validator is IDescribedInputValidator
            ? validator
            : InputValidators.Custom(name, arguments, validator);
    }

    private IInputValidator CompileEnumValidator(string inputName, string enumTypeName)
    {
        var enumType = ResolveType(enumTypeName)
            ?? throw new InvalidOperationException($"Input schema enum type '{enumTypeName}' for '{inputName}' could not be resolved.");

        if (!enumType.IsEnum)
        {
            throw new InvalidOperationException($"Input schema enum type '{enumTypeName}' for '{inputName}' is not an enum.");
        }

        return InputValidators.Enum(enumType);
    }

    private static string RequiredString(JsonElement element, string property, string inputName)
    {
        if (!element.TryGetProperty(property, out var value) || value.ValueKind != JsonValueKind.String)
        {
            throw new InvalidOperationException($"Input schema constraints for '{inputName}' require string property '{property}'.");
        }

        return value.GetString()!;
    }

    private static int RequiredInt(JsonElement element, string property, string inputName)
    {
        if (!element.TryGetProperty(property, out var value) || !value.TryGetInt32(out var number))
        {
            throw new InvalidOperationException($"Input schema constraints for '{inputName}' require integer property '{property}'.");
        }

        return number;
    }

    private static TEnum ParseEnum<TEnum>(string? value, TEnum defaultValue)
        where TEnum : struct
    {
        return string.IsNullOrWhiteSpace(value)
            ? defaultValue
            : Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed)
                ? parsed
                : throw new InvalidOperationException($"Unsupported {typeof(TEnum).Name} value '{value}'.");
    }

    private Type? ResolveType(string typeName)
    {
        var registeredType = _options.ResolveType(typeName);
        if (registeredType is not null)
        {
            return registeredType;
        }

        var simpleTypeName = StripAssemblyQualification(typeName);
        return simpleTypeName switch
        {
            "bool" or "boolean" or "System.Boolean" => typeof(bool),
            "byte" or "System.Byte" => typeof(byte),
            "sbyte" or "System.SByte" => typeof(sbyte),
            "short" or "System.Int16" => typeof(short),
            "ushort" or "System.UInt16" => typeof(ushort),
            "int" or "integer" or "System.Int32" => typeof(int),
            "uint" or "System.UInt32" => typeof(uint),
            "long" or "System.Int64" => typeof(long),
            "ulong" or "System.UInt64" => typeof(ulong),
            "float" or "single" or "System.Single" => typeof(float),
            "double" or "number" or "System.Double" => typeof(double),
            "decimal" or "System.Decimal" => typeof(decimal),
            "string" or "System.String" => typeof(string),
            "object" or "System.Object" => typeof(object),
            "Guid" or "System.Guid" => typeof(Guid),
            "DateTime" or "System.DateTime" => typeof(DateTime),
            "DateTimeOffset" or "System.DateTimeOffset" => typeof(DateTimeOffset),
            "TimeSpan" or "System.TimeSpan" => typeof(TimeSpan),
            "Exception" or "System.Exception" => typeof(Exception),
            "InvalidOperationException" or "System.InvalidOperationException" => typeof(InvalidOperationException),
            "OperationCanceledException" or "System.OperationCanceledException" => typeof(OperationCanceledException),
            "TaskCanceledException" or "System.Threading.Tasks.TaskCanceledException" => typeof(TaskCanceledException),
            "TimeoutException" or "System.TimeoutException" => typeof(TimeoutException),
            "ArgumentException" or "System.ArgumentException" => typeof(ArgumentException),
            "ArgumentNullException" or "System.ArgumentNullException" => typeof(ArgumentNullException),
            "NotSupportedException" or "System.NotSupportedException" => typeof(NotSupportedException),
            _ => _options.ResolveType(simpleTypeName)
        };
    }

    private static string StripAssemblyQualification(string typeName)
    {
        var commaIndex = typeName.IndexOf(',');
        return commaIndex < 0
            ? typeName
            : typeName[..commaIndex].Trim();
    }

    private static EdgeCondition FieldCondition(ConditionType type, ConditionConfig config)
    {
        return new EdgeCondition
        {
            Type = type,
            Field = config.Field,
            Value = GetConditionValue(config)
        };
    }

    private static object? GetConditionValue(ConditionConfig config)
    {
        if (config.Values is { Count: > 0 })
        {
            return config.Values.Select(GraphJsonValue.ToObject).ToArray();
        }

        if (config.Pattern is not null)
        {
            return config.Pattern;
        }

        return config.Value is null ? null : GraphJsonValue.ToObject(config.Value.Value);
    }

    private static ScheduleConstraint? CompileSchedule(ScheduleConstraintConfig? config)
    {
        if (config is null)
        {
            return null;
        }

        return new ScheduleConstraint
        {
            CronExpression = config.CronExpression,
            TimeZone = config.TimeZoneId is null ? null : TimeZoneInfo.FindSystemTimeZoneById(config.TimeZoneId),
            Tolerance = config.Tolerance
        };
    }

    private static EdgeRetryPolicy? CompileEdgeRetryPolicy(EdgeRetryPolicyConfig? config)
    {
        if (config is null)
        {
            return null;
        }

        return new EdgeRetryPolicy
        {
            RetryInterval = config.RetryInterval,
            MaxWaitTime = config.MaxWaitTime,
            MaxRetries = config.MaxRetries,
            ExhaustedBehavior = config.ExhaustedBehavior == EdgeRetryExhaustedBehaviorConfig.SkipNode
                ? EdgeRetryExhaustedBehavior.SkipNode
                : EdgeRetryExhaustedBehavior.FailGraph
        };
    }

    private static CloningPolicy CompileCloningPolicy(CloningPolicyConfig? config)
    {
        return config switch
        {
            CloningPolicyConfig.AlwaysClone => CloningPolicy.AlwaysClone,
            CloningPolicyConfig.NeverClone => CloningPolicy.NeverClone,
            CloningPolicyConfig.LazyClone or null => CloningPolicy.LazyClone,
            _ => CloningPolicy.LazyClone
        };
    }

    private static IterationOptions? CompileIterationOptions(IterationOptionsConfig? config)
    {
        if (config is null)
        {
            return null;
        }

        return new IterationOptions
        {
            MaxIterations = config.MaxIterations,
            UseChangeAwareIteration = config.EnableChangeDetection,
            EnableAutoConvergence = config.StopOnConvergence
        };
    }
}
