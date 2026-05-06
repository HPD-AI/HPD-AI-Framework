using System.Text.Json;
using HPDAgent.Graph.Abstractions.Config;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Graph;
using RuntimeGraph = HPDAgent.Graph.Abstractions.Graph.Graph;

namespace HPDAgent.Graph.Core.Config;

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
            MaxExecutions = node.MaxExecutions,
            MaxParallelExecutions = node.MaxParallelExecutions,
            OutputPortCount = node.OutputPortCount,
            SubGraphRef = node.SubGraphRef,
            SubGraph = node.SubGraph is null ? null : new GraphConfigExporter().Export(node.SubGraph),
            ArtifactNamespace = node.ArtifactNamespace,
            Metadata = node.Metadata
        };
    }

    private static JsonElement? ExportNodeConfig(IReadOnlyDictionary<string, object> config)
    {
        if (config.Count == 0)
        {
            return null;
        }

        if (config.Count == 1 && config.TryGetValue("$value", out var rawValue) && rawValue is JsonElement rawElement)
        {
            return rawElement.Clone();
        }

        var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var pair in config)
        {
            if (pair.Value is JsonElement element)
            {
                values[pair.Key] = element.Clone();
                continue;
            }

            values[pair.Key] = JsonSerializer.SerializeToElement(pair.Value);
        }

        return JsonSerializer.SerializeToElement(values);
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
            ConditionType.FieldStartsWith => FieldCondition(ConditionKindConfig.FieldStartsWith, condition),
            ConditionType.FieldEndsWith => FieldCondition(ConditionKindConfig.FieldEndsWith, condition),
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
            Value = condition.Value is null ? null : JsonSerializer.SerializeToElement(condition.Value)
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
            EnableChangeDetection = options.UseChangeAwareIteration,
            StopOnConvergence = options.EnableAutoConvergence
        };
    }
}
