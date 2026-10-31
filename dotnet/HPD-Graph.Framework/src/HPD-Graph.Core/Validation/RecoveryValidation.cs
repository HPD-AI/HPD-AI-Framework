using HPD.Graph.Abstractions.Execution;
using HPD.Graph.Abstractions.Graph;
using RuntimeGraph = HPD.Graph.Abstractions.Graph.Graph;

namespace HPD.Graph.Core.Validation;

/// <summary>Construction and runtime checks shared by raw Graph and MultiAgent recovery.</summary>
public static class RecoveryValidation
{
    public static void Validate(RuntimeGraph graph)
    {
        foreach (var node in graph.Nodes)
        {
            node.RetryPolicy?.Validate();
            if (node.OutputContract is { } contract)
            {
                if (contract.Values.GroupBy(v => (v.Port, v.Key)).Any(g => g.Count() > 1))
                    throw Invalid(node, "Duplicate output contract keys.");
                foreach (var value in contract.Values)
                    if (value.Port < 0 || value.Port >= node.OutputPortCount || string.IsNullOrWhiteSpace(value.Key)
                        || value.RuntimeType is null || value.RuntimeType.ContainsGenericParameters
                        || value.SchemaIdentity is not null && (string.IsNullOrWhiteSpace(value.SchemaVersion) || value.SchemaValidator is null))
                        throw Invalid(node, "Output contracts require resolved value types and registered versioned schema validators.");
            }
            if (node.RecoveryOnly && (node.Type != NodeType.Handler || graph.Edges.Any(e => e.From == node.Id || e.To == node.Id)
                || node.ErrorPolicy is not null))
                throw Invalid(node, "Recovery-only nodes must be handlers without ordinary edges or error policies.");
            if (node.ErrorPolicy is not { Mode: PropagationMode.ExecuteFallback } policy)
            {
                if (node.ErrorPolicy?.FallbackNodeId is not null || node.ErrorPolicy?.ExhaustionMode is not null)
                    throw Invalid(node, "Fallback settings require ExecuteFallback.");
                continue;
            }
            if (policy.ExhaustionMode is null or PropagationMode.ExecuteFallback || !Enum.IsDefined(policy.ExhaustionMode.Value))
                throw Invalid(node, "Fallback requires explicit StopGraph, Isolate, or SkipDependents exhaustion.");
            var fallback = graph.Nodes.SingleOrDefault(n => n.Id == policy.FallbackNodeId);
            if (fallback is null || fallback.Id == node.Id || !fallback.RecoveryOnly)
                throw Invalid(node, "Fallback must reference a distinct recovery-only handler; chained recovery is unsupported.");
            if (node.OutputContract is null || fallback.OutputContract is null)
                throw Invalid(node, "Fallback substitution requires explicit primary and recovery output contracts.");
            RequireCompatible(node.OutputContract, fallback.OutputContract);
        }
    }

    public static void RequireCompatible(NodeOutputContract primary, NodeOutputContract fallback)
    {
        foreach (var expected in primary.Values)
        {
            var actual = fallback.Values.SingleOrDefault(v => v.Port == expected.Port && v.Key == expected.Key);
            if (actual is null) { if (expected.Required) throw new InvalidOperationException("Fallback omits a required primary output."); continue; }
            if (expected.Required && !actual.Required || !expected.Nullable && actual.Nullable
                || expected.RuntimeType is null || actual.RuntimeType is null || !expected.RuntimeType.IsAssignableFrom(actual.RuntimeType)
                || expected.SchemaIdentity != actual.SchemaIdentity || expected.SchemaVersion != actual.SchemaVersion)
                throw new InvalidOperationException($"Incompatible fallback output contract for port {expected.Port}, key '{expected.Key}'.");
        }
        if (!primary.AllowAdditionalOutputs && (fallback.AllowAdditionalOutputs || fallback.Values.Any(a => !primary.Values.Any(p => p.Port == a.Port && p.Key == a.Key))))
            throw new InvalidOperationException("Fallback may emit outputs forbidden by the primary contract.");
    }

    public static NodeExecutionResult.Success ValidateAndProject(NodeOutputContract contract, NodeExecutionResult.Success success)
    {
        var projected = new Dictionary<int, Dictionary<string, object>>();
        foreach (var value in contract.Values)
        {
            var present = success.PortOutputs.TryGetValue(value.Port, out var port) && port.TryGetValue(value.Key, out _);
            if (!present) { if (value.Required) throw new InvalidOperationException($"Missing required recovery output '{value.Key}'."); continue; }
            var actual = port![value.Key];
            if (actual is null ? !value.Nullable : value.RuntimeType?.IsInstanceOfType(actual) != true)
                throw new InvalidOperationException($"Invalid recovery output '{value.Key}'.");
            if (value.SchemaValidator?.Validate(value.Key, actual).IsValid == false)
                throw new InvalidOperationException($"Recovery output '{value.Key}' violates its registered schema.");
            if (!projected.TryGetValue(value.Port, out var target)) projected[value.Port] = target = new();
            target[value.Key] = actual!;
        }
        foreach (var (port, values) in success.PortOutputs)
            foreach (var (key, value) in values)
                if (!contract.Values.Any(v => v.Port == port && v.Key == key))
                {
                    if (!contract.AllowAdditionalOutputs) throw new InvalidOperationException($"Unexpected recovery output '{key}'.");
                    if (!projected.TryGetValue(port, out var target)) projected[port] = target = new();
                    target[key] = value;
                }
        return success with { PortOutputs = projected };
    }
    private static InvalidOperationException Invalid(Node node, string message) => new($"Node '{node.Id}': {message}");
}
