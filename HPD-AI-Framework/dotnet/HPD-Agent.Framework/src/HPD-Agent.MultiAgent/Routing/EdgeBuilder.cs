using System.Text.RegularExpressions;
using HPDAgent.Graph.Abstractions;
using HPDAgent.Graph.Abstractions.Graph;

namespace HPD.MultiAgent.Routing;

/// <summary>
/// Fluent builder for defining edges between nodes.
/// </summary>
public class EdgeBuilder
{
    private readonly MultiAgent _workflowBuilder;
    private readonly string[] _sourceNodes;

    internal EdgeBuilder(MultiAgent workflowBuilder, string[] sourceNodes)
    {
        _workflowBuilder = workflowBuilder;
        _sourceNodes = sourceNodes;
    }

    /// <summary>
    /// Define target nodes for these edges.
    /// </summary>
    public EdgeTargetBuilder To(params string[] targetNodes)
    {
        return new EdgeTargetBuilder(_workflowBuilder, _sourceNodes, targetNodes);
    }

    /// <summary>
    /// Route by type when using union output.
    /// </summary>
    public TypeRouteBuilder RouteByType()
    {
        if (_sourceNodes.Length != 1)
        {
            throw new InvalidOperationException("RouteByType() can only be used with a single source node");
        }

        return new TypeRouteBuilder(_workflowBuilder, _sourceNodes[0]);
    }
}

/// <summary>
/// Builder for edge targets with optional conditions.
/// </summary>
public class EdgeTargetBuilder
{
    private readonly MultiAgent _workflowBuilder;
    private readonly string[] _sourceNodes;
    private readonly string[] _targetNodes;
    private EdgeCondition? _condition;

    internal EdgeTargetBuilder(
        MultiAgent workflowBuilder,
        string[] sourceNodes,
        string[] targetNodes)
    {
        _workflowBuilder = workflowBuilder;
        _sourceNodes = sourceNodes;
        _targetNodes = targetNodes;

        // Register edges immediately for unconditional case
        RegisterEdges();
    }

    /// <summary>
    /// Add a custom predicate condition for traversing these edges.
    /// The predicate receives the source node's outputs and returns true to traverse the edge.
    /// Note: predicate edges are not serializable to JSON config.
    /// </summary>
    public EdgeTargetBuilder When(Func<EdgeConditionContext, bool> predicate)
    {
        if (predicate == null) throw new ArgumentNullException(nameof(predicate));

        // Remove the unconditional edges registered in the constructor,
        // then register predicate-based edges via the workflow builder.
        foreach (var source in _sourceNodes)
        {
            foreach (var target in _targetNodes)
            {
                _workflowBuilder.AddPredicateEdge(source, target, predicate);
            }
        }

        return this;
    }

    /// <summary>
    /// Add a field equals condition.
    /// </summary>
    public EdgeTargetBuilder WhenEquals(string field, object value)
    {
        _condition = new EdgeCondition
        {
            Type = ConditionType.FieldEquals,
            Field = field,
            Value = value
        };
        UpdateEdges();
        return this;
    }

    /// <summary>
    /// Add a field not equals condition.
    /// </summary>
    public EdgeTargetBuilder WhenNotEquals(string field, object value)
    {
        _condition = new EdgeCondition
        {
            Type = ConditionType.FieldNotEquals,
            Field = field,
            Value = value
        };
        UpdateEdges();
        return this;
    }

    /// <summary>
    /// Add a field exists condition.
    /// </summary>
    public EdgeTargetBuilder WhenExists(string field)
    {
        _condition = new EdgeCondition
        {
            Type = ConditionType.FieldExists,
            Field = field
        };
        UpdateEdges();
        return this;
    }

    /// <summary>
    /// Add a field not exists condition.
    /// </summary>
    public EdgeTargetBuilder WhenNotExists(string field)
    {
        _condition = new EdgeCondition
        {
            Type = ConditionType.FieldNotExists,
            Field = field
        };
        UpdateEdges();
        return this;
    }

    /// <summary>
    /// Add a field greater than condition.
    /// </summary>
    public EdgeTargetBuilder WhenGreaterThan(string field, object value)
    {
        _condition = new EdgeCondition
        {
            Type = ConditionType.FieldGreaterThan,
            Field = field,
            Value = value
        };
        UpdateEdges();
        return this;
    }

    /// <summary>
    /// Add a field less than condition.
    /// </summary>
    public EdgeTargetBuilder WhenLessThan(string field, object value)
    {
        _condition = new EdgeCondition
        {
            Type = ConditionType.FieldLessThan,
            Field = field,
            Value = value
        };
        UpdateEdges();
        return this;
    }

    /// <summary>
    /// Add a field contains condition (for strings or collections).
    /// </summary>
    public EdgeTargetBuilder WhenContains(string field, object value)
    {
        _condition = new EdgeCondition
        {
            Type = ConditionType.FieldContains,
            Field = field,
            Value = value
        };
        UpdateEdges();
        return this;
    }

    /// <summary>
    /// Add a declarative compound or leaf condition built via <see cref="Condition"/> factory methods.
    /// Unlike <see cref="When(Func{EdgeConditionContext, bool})"/>, this overload is fully serializable.
    /// </summary>
    public EdgeTargetBuilder When(EdgeCondition condition)
    {
        _condition = condition ?? throw new ArgumentNullException(nameof(condition));
        UpdateEdges();
        return this;
    }

    // ========================================
    // Advanced String Conditions
    // ========================================

    /// <summary>
    /// Traverse if <paramref name="field"/> starts with <paramref name="prefix"/>.
    /// </summary>
    public EdgeTargetBuilder WhenStartsWith(string field, string prefix)
    {
        _condition = new EdgeCondition
        {
            Type = ConditionType.FieldStartsWith,
            Field = field,
            Value = prefix
        };
        UpdateEdges();
        return this;
    }

    /// <summary>
    /// Traverse if <paramref name="field"/> ends with <paramref name="suffix"/>.
    /// </summary>
    public EdgeTargetBuilder WhenEndsWith(string field, string suffix)
    {
        _condition = new EdgeCondition
        {
            Type = ConditionType.FieldEndsWith,
            Field = field,
            Value = suffix
        };
        UpdateEdges();
        return this;
    }

    /// <summary>
    /// Traverse if <paramref name="field"/> matches the regular expression <paramref name="pattern"/>.
    /// </summary>
    public EdgeTargetBuilder WhenMatchesRegex(string field, string pattern, RegexOptions options = RegexOptions.None)
    {
        var optionsStr = options == RegexOptions.None
            ? null
            : string.Join(",", options.ToString().Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries));

        _condition = new EdgeCondition
        {
            Type = ConditionType.FieldMatchesRegex,
            Field = field,
            Value = pattern,
            RegexOptions = optionsStr
        };
        UpdateEdges();
        return this;
    }

    /// <summary>
    /// Traverse if <paramref name="field"/> is null, empty, or whitespace-only.
    /// </summary>
    public EdgeTargetBuilder WhenEmpty(string field)
    {
        _condition = new EdgeCondition
        {
            Type = ConditionType.FieldIsEmpty,
            Field = field
        };
        UpdateEdges();
        return this;
    }

    /// <summary>
    /// Traverse if <paramref name="field"/> is NOT null, empty, or whitespace-only.
    /// </summary>
    public EdgeTargetBuilder WhenNotEmpty(string field)
    {
        _condition = new EdgeCondition
        {
            Type = ConditionType.FieldIsNotEmpty,
            Field = field
        };
        UpdateEdges();
        return this;
    }

    // ========================================
    // Collection Conditions
    // ========================================

    /// <summary>
    /// Traverse if the <paramref name="field"/> array contains at least one of <paramref name="values"/>.
    /// </summary>
    public EdgeTargetBuilder WhenContainsAny(string field, params object[] values)
    {
        _condition = new EdgeCondition
        {
            Type = ConditionType.FieldContainsAny,
            Field = field,
            Value = values
        };
        UpdateEdges();
        return this;
    }

    /// <summary>
    /// Traverse if the <paramref name="field"/> array contains ALL of <paramref name="values"/>.
    /// </summary>
    public EdgeTargetBuilder WhenContainsAll(string field, params object[] values)
    {
        _condition = new EdgeCondition
        {
            Type = ConditionType.FieldContainsAll,
            Field = field,
            Value = values
        };
        UpdateEdges();
        return this;
    }

    /// <summary>
    /// Mark this as a default edge (fallback if no other conditions match).
    /// </summary>
    public EdgeTargetBuilder AsDefault()
    {
        _condition = new EdgeCondition
        {
            Type = ConditionType.Default
        };
        UpdateEdges();
        return this;
    }

    /// <summary>
    /// Continue defining more edges.
    /// </summary>
    public EdgeBuilder From(params string[] sourceNodes)
    {
        return _workflowBuilder.From(sourceNodes);
    }

    /// <summary>
    /// Build the workflow.
    /// </summary>
    public Task<AgentWorkflowInstance> BuildAsync(CancellationToken cancellationToken = default)
    {
        return _workflowBuilder.BuildAsync(cancellationToken);
    }

    private void RegisterEdges()
    {
        foreach (var source in _sourceNodes)
        {
            foreach (var target in _targetNodes)
            {
                _workflowBuilder.AddEdgeInternal(source, target, _condition);
            }
        }
    }

    private void UpdateEdges()
    {
        // Remove previously registered edges and add with new condition
        foreach (var source in _sourceNodes)
        {
            foreach (var target in _targetNodes)
            {
                _workflowBuilder.UpdateEdgeCondition(source, target, _condition);
            }
        }
    }
}

/// <summary>
/// Context for evaluating edge conditions.
/// </summary>
public class EdgeConditionContext
{
    private readonly Dictionary<string, object> _outputs;

    internal EdgeConditionContext(Dictionary<string, object> outputs)
    {
        _outputs = outputs;
    }

    /// <summary>
    /// Get a value from the source node's outputs.
    /// </summary>
    public T Get<T>(string key)
    {
        if (_outputs.TryGetValue(key, out var value) && value is T typedValue)
        {
            return typedValue;
        }
        return default!;
    }

    /// <summary>
    /// Check if a key exists in the outputs.
    /// </summary>
    public bool HasKey(string key)
    {
        return _outputs.ContainsKey(key);
    }
}

/// <summary>
/// Builder for type-based routing with union outputs.
/// </summary>
public class TypeRouteBuilder
{
    private readonly MultiAgent _workflowBuilder;
    private readonly string _sourceNode;
    private readonly List<(Type Type, string Target)> _routes = new();

    internal TypeRouteBuilder(MultiAgent workflowBuilder, string sourceNode)
    {
        _workflowBuilder = workflowBuilder;
        _sourceNode = sourceNode;
    }

    /// <summary>
    /// Route to target when the matched type is T.
    /// </summary>
    public TypeRouteBuilder When<T>(string targetNode)
    {
        _routes.Add((typeof(T), targetNode));

        // Register edge with FieldEquals condition on matched_type
        _workflowBuilder.AddEdgeInternal(_sourceNode, targetNode, new EdgeCondition
        {
            Type = ConditionType.FieldEquals,
            Field = "matched_type",
            Value = typeof(T).Name
        });

        return this;
    }

    /// <summary>
    /// Add a default route for unmatched types.
    /// </summary>
    public TypeRouteBuilder Default(string targetNode)
    {
        _workflowBuilder.AddEdgeInternal(_sourceNode, targetNode, new EdgeCondition
        {
            Type = ConditionType.Default
        });

        return this;
    }

    /// <summary>
    /// Continue defining more edges.
    /// </summary>
    public EdgeBuilder From(params string[] sourceNodes)
    {
        return _workflowBuilder.From(sourceNodes);
    }

    /// <summary>
    /// Build the workflow.
    /// </summary>
    public Task<AgentWorkflowInstance> BuildAsync(CancellationToken cancellationToken = default)
    {
        return _workflowBuilder.BuildAsync(cancellationToken);
    }
}
