using System.Text.Json;
using HPD.Graph.Abstractions.Artifacts;
using HPD.Graph.Abstractions.Caching;
using HPD.Graph.Abstractions.Config;
using HPD.Graph.Abstractions.Execution;
using HPD.Graph.Abstractions.Graph;
using HPD.Graph.Abstractions.Serialization;
using HPD.Graph.Abstractions.Validation;
using HPD.Graph.Core.Validation;
using RuntimeGraph = HPD.Graph.Abstractions.Graph.Graph;

namespace HPD.Graph.Core.Config;

/// <summary>
/// Exports supported runtime graph instances into serializable graph definitions.
/// </summary>
public sealed class GraphConfigExporter
{
    public GraphConfig Export(RuntimeGraph graph)
    {
        ArgumentNullException.ThrowIfNull(graph);

        return new GraphConfig
        {
            GraphId = graph.Id,
            GraphVersion = graph.Version,
            Name = graph.Name,
            EntryNodeId = graph.EntryNodeId,
            ExitNodeId = graph.ExitNodeId,
            MaxIterations = graph.MaxIterations,
            ExecutionTimeout = graph.ExecutionTimeout,
            CloningPolicy = ExportCloningPolicy(graph.CloningPolicy),
            IterationOptions = ExportIterationOptions(graph.IterationOptions),
            Nodes = graph.Nodes
                .Where(node => node.Id != graph.EntryNodeId && node.Id != graph.ExitNodeId)
                .ToDictionary(node => node.Id, ExportNode, StringComparer.Ordinal),
            Edges = graph.Edges.Select(ExportEdge).ToList(),
            Metadata = graph.Metadata
        };
    }

    private static NodeConfig ExportNode(Node node)
    {
        return new NodeConfig
        {
            Id = node.Id,
            Name = node.Name,
            Type = ExportNodeType(node.Type),
            HandlerName = node.HandlerName,
            Config = ExportNodeConfig(node.Config),
            Timeout = node.Timeout,
            RetryPolicy = ExportRetryPolicy(node.RetryPolicy),
            ErrorPolicy = ExportErrorPolicy(node.ErrorPolicy),
            SuspensionOptions = ExportSuspensionOptions(node.SuspensionOptions),
            EnableCheckpointing = node.EnableCheckpointing,
            MaxExecutions = node.MaxExecutions,
            MaxParallelExecutions = node.MaxParallelExecutions,
            OutputPortCount = node.OutputPortCount,
            SubGraphRef = node.SubGraphRef,
            SubGraph = node.SubGraph is null ? null : ExportGraph(node.SubGraph),
            MapProcessorGraph = node.MapProcessorGraph is null ? null : ExportGraph(node.MapProcessorGraph),
            MapProcessorGraphRef = node.MapProcessorGraphRef,
            MaxParallelMapTasks = node.MaxParallelMapTasks,
            MapInputChannel = node.MapInputChannel,
            MapOutputChannel = node.MapOutputChannel,
            MapErrorMode = ExportMapErrorMode(node.MapErrorMode),
            MapItemType = node.MapItemType,
            MapResultType = node.MapResultType,
            MapProcessorGraphs = node.MapProcessorGraphs?.ToDictionary(
                kvp => kvp.Key,
                kvp => ExportGraph(kvp.Value),
                StringComparer.Ordinal),
            MapRouterName = node.MapRouterName,
            MapDefaultGraph = node.MapDefaultGraph is null ? null : ExportGraph(node.MapDefaultGraph),
            Artifacts = ExportArtifacts(node),
            Partitions = ExportPartitionDefinition(node.Partitions),
            PartitionDependencies = ExportPartitionDependencies(node.PartitionDependencies),
            Cache = ExportCacheOptions(node.Cache),
            ArtifactNamespace = node.ArtifactNamespace,
            InputSchemas = ExportInputSchemas(node.InputSchemas),
            Metadata = node.Metadata
        };
    }

    private static GraphConfig ExportGraph(RuntimeGraph graph) => new GraphConfigExporter().Export(graph);

    private static JsonElement? ExportNodeConfig(JsonElement? config)
    {
        return config?.Clone();
    }

    private static EdgeConfig ExportEdge(Edge edge)
    {
        return new EdgeConfig
        {
            From = edge.From,
            To = edge.To,
            FromPort = edge.FromPort,
            ToPort = edge.ToPort,
            Priority = edge.Priority,
            Condition = ExportCondition(edge.Condition),
            Delay = edge.Delay,
            Schedule = ExportSchedule(edge.Schedule),
            RetryPolicy = ExportEdgeRetryPolicy(edge.RetryPolicy),
            CloningPolicy = edge.CloningPolicy is null ? null : ExportCloningPolicy(edge.CloningPolicy.Value),
            Metadata = edge.Metadata
        };
    }

    private static NodeKindConfig ExportNodeType(NodeType type) => type switch
    {
        NodeType.Start => NodeKindConfig.Start,
        NodeType.End => NodeKindConfig.End,
        NodeType.Handler => NodeKindConfig.Handler,
        NodeType.Router => NodeKindConfig.Router,
        NodeType.SubGraph => NodeKindConfig.SubGraph,
        NodeType.Map => NodeKindConfig.Map,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unsupported node type.")
    };

    private static RetryPolicyConfig? ExportRetryPolicy(RetryPolicy? policy)
    {
        if (policy is null)
        {
            return null;
        }

        return new RetryPolicyConfig
        {
            MaxAttempts = policy.MaxAttempts,
            InitialDelay = policy.InitialDelay,
            Strategy = policy.Strategy switch
            {
                BackoffStrategy.Constant => BackoffStrategyConfig.Constant,
                BackoffStrategy.Exponential => BackoffStrategyConfig.Exponential,
                BackoffStrategy.Linear => BackoffStrategyConfig.Linear,
                BackoffStrategy.JitteredExponential => BackoffStrategyConfig.JitteredExponential,
                _ => BackoffStrategyConfig.Exponential
            },
            MaxDelay = policy.MaxDelay,
            RetryableExceptionTypeNames = policy.RetryableExceptions?.Select(type => type.AssemblyQualifiedName ?? type.FullName ?? type.Name).ToList()
        };
    }

    private static ArtifactDependencyConfig? ExportArtifacts(Node node)
    {
        if (node.ProducesArtifact is null && node.RequiresArtifacts is null)
        {
            return null;
        }

        return new ArtifactDependencyConfig
        {
            ProducesArtifact = node.ProducesArtifact?.ToString(),
            RequiresArtifacts = node.RequiresArtifacts?.Select(artifact => artifact.ToString()).ToList()
        };
    }

    private static PartitionDefinitionConfig? ExportPartitionDefinition(PartitionDefinition? partition)
    {
        return partition switch
        {
            null => null,
            StaticPartitionDefinition staticPartition => new PartitionDefinitionConfig
            {
                Type = PartitionKindConfig.Static,
                Definition = JsonSerializer.SerializeToElement(
                    staticPartition,
                    GraphConfigJsonSerializerContext.Default.StaticPartitionDefinition)
            },
            TimePartitionDefinition timePartition => new PartitionDefinitionConfig
            {
                Type = PartitionKindConfig.Time,
                Definition = JsonSerializer.SerializeToElement(
                    timePartition,
                    GraphConfigJsonSerializerContext.Default.TimePartitionDefinition)
            },
            MultiPartitionDefinition multiPartition => new PartitionDefinitionConfig
            {
                Type = PartitionKindConfig.Multi,
                Definition = JsonSerializer.SerializeToElement(
                    multiPartition,
                    GraphConfigJsonSerializerContext.Default.MultiPartitionDefinition)
            },
            _ => throw new NotSupportedException($"Partition definition '{partition.GetType().FullName}' cannot be exported to GraphConfig.")
        };
    }

    private static PartitionDependencyConfig? ExportPartitionDependencies(PartitionDependencyMapping? mapping)
    {
        if (mapping is null)
        {
            return null;
        }

        return mapping.Kind switch
        {
            PartitionDependencyMappingKind.WeeklyFromDaily => new PartitionDependencyConfig
            {
                Type = PartitionDependencyMappingKindConfig.WeeklyFromDaily
            },
            PartitionDependencyMappingKind.MonthlyFromDaily => new PartitionDependencyConfig
            {
                Type = PartitionDependencyMappingKindConfig.MonthlyFromDaily
            },
            PartitionDependencyMappingKind.QuarterlyFromMonthly => new PartitionDependencyConfig
            {
                Type = PartitionDependencyMappingKindConfig.QuarterlyFromMonthly
            },
            PartitionDependencyMappingKind.YearlyFromMonthly => new PartitionDependencyConfig
            {
                Type = PartitionDependencyMappingKindConfig.YearlyFromMonthly
            },
            null when mapping.CustomDescriptor is not null => new PartitionDependencyConfig
            {
                Custom = mapping.CustomDescriptor
            },
            null => throw new NotSupportedException("Custom runtime partition dependency mappings cannot be exported to GraphConfig."),
            _ => throw new NotSupportedException($"Partition dependency mapping '{mapping.Kind}' cannot be exported to GraphConfig.")
        };
    }

    private static CacheOptionsConfig? ExportCacheOptions(CacheOptions? cache)
    {
        if (cache is null)
        {
            return null;
        }

        return new CacheOptionsConfig
        {
            Enabled = true,
            Strategy = cache.Strategy.ToString(),
            Ttl = cache.Ttl,
            Invalidation = cache.Invalidation.ToString()
        };
    }

    private static IReadOnlyDictionary<string, InputSchemaConfig>? ExportInputSchemas(
        IReadOnlyDictionary<string, InputSchema>? schemas)
    {
        if (schemas is null || schemas.Count == 0)
        {
            return null;
        }

        return schemas.ToDictionary(
            kvp => kvp.Key,
            kvp =>
            {
                return new InputSchemaConfig
                {
                    TypeName = kvp.Value.Type.AssemblyQualifiedName ?? kvp.Value.Type.FullName ?? kvp.Value.Type.Name,
                    Required = kvp.Value.Required,
                    DefaultValue = kvp.Value.DefaultValue is null
                        ? null
                        : GraphJsonValue.ToJsonElement(kvp.Value.DefaultValue, $"input schema '{kvp.Key}' default value"),
                    Constraints = ExportInputValidator(kvp.Value.Validator)
                };
            },
            StringComparer.Ordinal);
    }

    private static JsonElement? ExportInputValidator(IInputValidator? validator)
    {
        return validator switch
        {
            null => null,
            UrlValidator => Constraint("""{"type":"url"}"""),
            EmailValidator => Constraint("""{"type":"email"}"""),
            RegexValidator regex => Constraint(
                $$"""{"type":"regex","pattern":{{JsonSerializer.Serialize(regex.Pattern, GraphConfigJsonSerializerContext.Default.String)}}}"""),
            RangeValidator range => Constraint($$"""{"type":"range","min":{{range.Min}},"max":{{range.Max}}}"""),
            StringLengthValidator length => Constraint(
                $$"""{"type":"stringLength","minLength":{{length.MinLength}},"maxLength":{{length.MaxLength}}}"""),
            CollectionCountValidator count => Constraint(
                $$"""{"type":"collectionCount","minCount":{{count.MinCount}},"maxCount":{{count.MaxCount}}}"""),
            IDescribedInputValidator described => ExportDescribedInputValidator(described),
            _ when TryExportEnumValidator(validator, out var element) => element,
            _ => throw new NotSupportedException($"Input validator '{validator.GetType().FullName}' cannot be exported to GraphConfig.")
        };
    }

    private static JsonElement ExportDescribedInputValidator(IDescribedInputValidator validator)
    {
        var arguments = validator.DescriptorArguments is { } argumentElement
            ? $",\"arguments\":{argumentElement.GetRawText()}"
            : string.Empty;

        return Constraint(
            $"{{\"type\":\"custom\",\"name\":{JsonSerializer.Serialize(validator.DescriptorName, GraphConfigJsonSerializerContext.Default.String)}{arguments}}}");
    }

    private static bool TryExportEnumValidator(IInputValidator validator, out JsonElement element)
    {
        if (validator is IRuntimeEnumValidator runtimeEnumValidator)
        {
            element = EnumConstraint(runtimeEnumValidator.EnumType);
            return true;
        }

        element = default;
        return false;
    }

    private static JsonElement EnumConstraint(Type enumType)
    {
        var enumTypeName = enumType.AssemblyQualifiedName ?? enumType.FullName ?? enumType.Name;
        return Constraint(
            $$"""{"type":"enum","enumType":{{JsonSerializer.Serialize(enumTypeName, GraphConfigJsonSerializerContext.Default.String)}}}""");
    }

    private static JsonElement Constraint(string json)
        => JsonDocument.Parse(json).RootElement.Clone();

    private static MapErrorModeConfig? ExportMapErrorMode(MapErrorMode? mode)
    {
        return mode switch
        {
            null => null,
            MapErrorMode.FailFast => MapErrorModeConfig.FailFast,
            MapErrorMode.ContinueWithNulls => MapErrorModeConfig.ContinueWithNulls,
            MapErrorMode.ContinueOmitFailures => MapErrorModeConfig.ContinueOmitFailures,
            _ => MapErrorModeConfig.FailFast
        };
    }

    private static ErrorPropagationPolicyConfig? ExportErrorPolicy(ErrorPropagationPolicy? policy)
    {
        if (policy is null)
        {
            return null;
        }

        if (policy.ShouldPropagate is not null)
        {
            throw new NotSupportedException("Runtime error propagation predicates cannot be exported to GraphConfig.");
        }

        return new ErrorPropagationPolicyConfig
        {
            Mode = policy.Mode switch
            {
                PropagationMode.StopGraph => PropagationModeConfig.StopGraph,
                PropagationMode.SkipDependents => PropagationModeConfig.SkipDependents,
                PropagationMode.ExecuteFallback => PropagationModeConfig.ExecuteFallback,
                PropagationMode.Isolate => PropagationModeConfig.Isolate,
                _ => PropagationModeConfig.StopGraph
            },
            AffectedNodes = policy.AffectedNodes,
            FallbackNodeId = policy.FallbackNodeId
        };
    }

    private static SuspensionOptionsConfig? ExportSuspensionOptions(SuspensionOptions? options)
    {
        if (options is null)
        {
            return null;
        }

        return new SuspensionOptionsConfig
        {
            ActiveWaitTimeout = options.ActiveWaitTimeout,
            EmitEvents = options.EmitEvents,
            SaveCheckpointFirst = options.SaveCheckpointFirst
        };
    }

    private static ConditionConfig? ExportCondition(EdgeCondition? condition)
    {
        if (condition is null)
        {
            return null;
        }

        return condition.Type switch
        {
            ConditionType.Always => new ConditionConfig { Type = ConditionKindConfig.Always },
            ConditionType.Default => new ConditionConfig { Type = ConditionKindConfig.Default },
            ConditionType.FieldEquals => FieldCondition(ConditionKindConfig.FieldEquals, condition),
            ConditionType.FieldNotEquals => FieldCondition(ConditionKindConfig.FieldNotEquals, condition),
            ConditionType.FieldGreaterThan => FieldCondition(ConditionKindConfig.FieldGreaterThan, condition),
            ConditionType.FieldGreaterThanOrEqual => FieldCondition(ConditionKindConfig.FieldGreaterThanOrEqual, condition),
            ConditionType.FieldLessThan => FieldCondition(ConditionKindConfig.FieldLessThan, condition),
            ConditionType.FieldLessThanOrEqual => FieldCondition(ConditionKindConfig.FieldLessThanOrEqual, condition),
            ConditionType.FieldContains => FieldCondition(ConditionKindConfig.FieldContains, condition),
            ConditionType.FieldContainsAny => FieldCondition(ConditionKindConfig.FieldContainsAny, condition),
            ConditionType.FieldContainsAll => FieldCondition(ConditionKindConfig.FieldContainsAll, condition),
            ConditionType.FieldStartsWith => FieldCondition(ConditionKindConfig.FieldStartsWith, condition) with
            {
                IgnoreCase = string.Equals(condition.RegexOptions, "IgnoreCase", StringComparison.OrdinalIgnoreCase)
            },
            ConditionType.FieldEndsWith => FieldCondition(ConditionKindConfig.FieldEndsWith, condition) with
            {
                IgnoreCase = string.Equals(condition.RegexOptions, "IgnoreCase", StringComparison.OrdinalIgnoreCase)
            },
            ConditionType.FieldMatchesRegex => FieldCondition(ConditionKindConfig.FieldMatchesRegex, condition) with
            {
                IgnoreCase = string.Equals(condition.RegexOptions, "IgnoreCase", StringComparison.OrdinalIgnoreCase)
            },
            ConditionType.FieldExists => FieldCondition(ConditionKindConfig.FieldExists, condition),
            ConditionType.FieldNotExists => FieldCondition(ConditionKindConfig.FieldNotExists, condition),
            ConditionType.FieldIsEmpty => FieldCondition(ConditionKindConfig.FieldEmpty, condition),
            ConditionType.FieldIsNotEmpty => FieldCondition(ConditionKindConfig.FieldNotEmpty, condition),
            ConditionType.UpstreamOneSuccess => new ConditionConfig { Type = ConditionKindConfig.UpstreamOneSuccess },
            ConditionType.UpstreamAllDone => new ConditionConfig { Type = ConditionKindConfig.UpstreamAllDone },
            ConditionType.UpstreamAllDoneOneSuccess => new ConditionConfig { Type = ConditionKindConfig.UpstreamAllDoneOneSuccess },
            ConditionType.And => new ConditionConfig
            {
                Type = ConditionKindConfig.All,
                All = condition.Conditions?.Select(ExportCondition).Where(c => c != null).Cast<ConditionConfig>().ToList()
            },
            ConditionType.Or => new ConditionConfig
            {
                Type = ConditionKindConfig.Any,
                Any = condition.Conditions?.Select(ExportCondition).Where(c => c != null).Cast<ConditionConfig>().ToList()
            },
            ConditionType.Not => new ConditionConfig
            {
                Type = ConditionKindConfig.Not,
                Not = condition.Conditions?.Select(ExportCondition).FirstOrDefault(c => c != null)
            },
            _ => throw new NotSupportedException($"Condition '{condition.Type}' cannot be exported.")
        };
    }

    private static ConditionConfig FieldCondition(ConditionKindConfig type, EdgeCondition condition)
    {
        return new ConditionConfig
        {
            Type = type,
            Field = condition.Field,
            Value = condition.Value is null
                ? null
                : GraphJsonValue.ToJsonElement(condition.Value, $"edge condition '{condition.Field ?? condition.Type.ToString()}'")
        };
    }

    private static ScheduleConstraintConfig? ExportSchedule(ScheduleConstraint? schedule)
    {
        if (schedule is null)
        {
            return null;
        }

        if (schedule.AdditionalCondition is not null)
        {
            throw new NotSupportedException("Runtime schedule predicates cannot be exported to GraphConfig.");
        }

        return new ScheduleConstraintConfig
        {
            CronExpression = schedule.CronExpression,
            TimeZoneId = schedule.TimeZone?.Id,
            Tolerance = schedule.Tolerance
        };
    }

    private static EdgeRetryPolicyConfig? ExportEdgeRetryPolicy(EdgeRetryPolicy? policy)
    {
        if (policy is null)
        {
            return null;
        }

        if (policy.RetryCondition is not null)
        {
            throw new NotSupportedException("Runtime edge retry predicates cannot be exported to GraphConfig.");
        }

        return new EdgeRetryPolicyConfig
        {
            RetryInterval = policy.RetryInterval,
            MaxWaitTime = policy.MaxWaitTime,
            MaxRetries = policy.MaxRetries,
            ExhaustedBehavior = policy.ExhaustedBehavior == EdgeRetryExhaustedBehavior.SkipNode
                ? EdgeRetryExhaustedBehaviorConfig.SkipNode
                : EdgeRetryExhaustedBehaviorConfig.FailGraph
        };
    }

    private static CloningPolicyConfig ExportCloningPolicy(CloningPolicy policy)
    {
        return policy switch
        {
            CloningPolicy.AlwaysClone => CloningPolicyConfig.AlwaysClone,
            CloningPolicy.NeverClone => CloningPolicyConfig.NeverClone,
            CloningPolicy.LazyClone => CloningPolicyConfig.LazyClone,
            _ => CloningPolicyConfig.LazyClone
        };
    }

    private static IterationOptionsConfig? ExportIterationOptions(IterationOptions? options)
    {
        if (options is null)
        {
            return null;
        }

        return new IterationOptionsConfig
        {
            MaxIterations = options.MaxIterations,
            StopOnConvergence = options.EnableAutoConvergence
        };
    }
}
