using System.Text.Json;
using HPDAgent.Graph.Abstractions.Config;
using HPDAgent.Graph.Abstractions.Execution;
using HPDAgent.Graph.Abstractions.Graph;
using RuntimeGraph = HPDAgent.Graph.Abstractions.Graph.Graph;

namespace HPDAgent.Graph.Core.Config;

/// <summary>
/// Compiles serializable graph definitions into runtime graph instances.
/// </summary>
public sealed class GraphConfigCompiler
{
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
            MaxExecutions = config.MaxExecutions,
            MaxParallelExecutions = config.MaxParallelExecutions,
            OutputPortCount = config.OutputPortCount,
            SubGraphRef = config.SubGraphRef,
            SubGraph = config.SubGraph is null ? null : CompileSubGraph(config.SubGraph),
            ArtifactNamespace = config.ArtifactNamespace,
            Metadata = config.Metadata
        };
    }

    private static IReadOnlyDictionary<string, object> CompileNodeConfig(JsonElement? config)
    {
        if (config is null)
        {
            return new Dictionary<string, object>();
        }

        var element = config.Value;
        if (element.ValueKind != JsonValueKind.Object)
        {
            return new Dictionary<string, object>
            {
                ["$value"] = element.Clone()
            };
        }

        var values = new Dictionary<string, object>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            values[property.Name] = property.Value.Clone();
        }

        return values;
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

    private static RetryPolicy? CompileRetryPolicy(RetryPolicyConfig? config)
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
            MaxDelay = config.MaxDelay
        };
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
            ConditionKindConfig.FieldStartsWith => FieldCondition(ConditionType.FieldStartsWith, config),
            ConditionKindConfig.FieldEndsWith => FieldCondition(ConditionType.FieldEndsWith, config),
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
            return config.Values.Select(value => value.Clone()).ToArray();
        }

        if (config.Pattern is not null)
        {
            return config.Pattern;
        }

        return config.Value?.Clone();
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
