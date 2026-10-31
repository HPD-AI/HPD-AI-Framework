using System.Collections.Concurrent;
using HPD.Graph.Abstractions.Execution;
using HPD.Graph.Abstractions.Graph;
using HPD.Graph.Abstractions.Validation;
using HPD.MultiAgent.Config;

namespace HPD.MultiAgent;

internal static class WorkflowOutputContract
{
    private static readonly ConcurrentDictionary<string, IInputValidator> UnionValidators = new();
    internal static ErrorPropagationPolicy ErrorPolicy(AgentNodeOptions options)
    {
        if (options.ErrorMode != ErrorMode.Fallback && (options.FallbackAgentId is not null || options.ExhaustionMode is not null))
            throw new InvalidOperationException("Fallback settings require Fallback mode.");
        var mode = Map(options.ErrorMode);
        return new ErrorPropagationPolicy
        {
            Mode = mode, FallbackNodeId = options.FallbackAgentId, AffectedNodes = options.AffectedNodes?.ToArray(),
            ExhaustionMode = options.ExhaustionMode is { } exhausted ? Map(exhausted) : null
        };
    }
    private static PropagationMode Map(ErrorMode mode) => mode switch
    {
        ErrorMode.Stop => PropagationMode.StopGraph,
        ErrorMode.Isolate => PropagationMode.Isolate,
        ErrorMode.SkipDependents => PropagationMode.SkipDependents,
        ErrorMode.Fallback => PropagationMode.ExecuteFallback,
        _ => throw new InvalidOperationException("Unknown workflow error policy.")
    };

    internal static NodeOutputContract Create(AgentNodeOptions options)
    {
        var values = new List<OutputValueContract>();
        if (options.OutputMode == AgentOutputMode.String)
            values.Add(Value(options.OutputKey ?? "answer", typeof(string)));
        else if (options.OutputMode == AgentOutputMode.Handoff)
        {
            values.Add(Value("handoff_target", typeof(string)));
            values.Add(Value("response", typeof(string), required: false));
        }
        else
        {
            var types = options.OutputMode == AgentOutputMode.Union ? options.UnionTypes! : [options.StructuredType!];
            if (types.Any(t => !t.IsClass || t.ContainsGenericParameters))
                throw new InvalidOperationException("Structured output types must be closed reference types.");
            foreach (var group in types.SelectMany(t => t.GetProperties().Where(p => p.CanRead && p.GetIndexParameters().Length == 0 && p.GetMethod?.IsStatic == false))
                .Where(p => p.Name.ToLowerInvariant() is not "result" and not "matched_type").GroupBy(p => p.Name.ToLowerInvariant()))
            {
                var propertyTypes = group.Select(p => p.PropertyType).Distinct().ToArray();
                values.Add(Value(group.Key, propertyTypes.Length == 1 ? Nullable.GetUnderlyingType(propertyTypes[0]) ?? propertyTypes[0] : typeof(object), required: false, nullable: true));
            }
            var result = Value("result", options.OutputMode == AgentOutputMode.Union ? typeof(object) : types[0]);
            if (options.OutputMode == AgentOutputMode.Union)
            {
                var identity = "hpd.union:" + string.Join("|", types.Select(t => t.AssemblyQualifiedName).Order());
                var validator = UnionValidators.GetOrAdd(identity, _ => new UnionValidator(types.ToArray()));
                result = result with { SchemaIdentity = identity, SchemaVersion = "1", SchemaValidator = validator };
                values.Add(Value("matched_type", typeof(string)));
            }
            values.Add(result);
        }
        return new NodeOutputContract { Values = values.ToArray() };
    }
    private static OutputValueContract Value(string key, Type type, bool required = true, bool nullable = false) => new()
    {
        Key = key, TypeName = type.AssemblyQualifiedName!, RuntimeType = type, Required = required, Nullable = nullable
    };
    private sealed class UnionValidator(Type[] alternatives) : IInputValidator
    {
        public ValidationResult Validate(string inputName, object? value) => value is not null && alternatives.Contains(value.GetType())
            ? ValidationResult.Success() : ValidationResult.Failure("Value is not a declared union alternative.");
    }
}
