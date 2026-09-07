using System.Text.Json;
using System.Text.Json.Nodes;

namespace HPD.Agent.Goals;

internal static class GoalActionComposition
{
    internal static VerifiedAIFunctionActionComposition Restrict(
        HPDAIFunctionFactory.HPDAIFunction function, GoalToolAccess access, bool allowModelCreation)
    {
        if (!Enum.IsDefined(access) || access == GoalToolAccess.Hidden)
            throw new InvalidOperationException("goal_function_hidden");
        var original = function.OperationContract ?? throw new InvalidOperationException("goal_action_contract_missing");
        var allowed = original.Actions.Where(pair => access == GoalToolAccess.ReadOnly
            ? pair.Key == "get" : allowModelCreation || pair.Key != "create")
            .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
        var schema = JsonNode.Parse(function.JsonSchema.GetRawText())!.AsObject();
        var variants = schema["properties"]?[original.ActionArgumentName]?["oneOf"]?.AsArray()
            ?? throw new InvalidOperationException("goal_action_schema_invalid");
        for (var i = variants.Count - 1; i >= 0; i--)
        {
            var action = variants[i]?["properties"]?[original.Discriminator]?["const"]?.GetValue<string>();
            if (action is null) throw new InvalidOperationException("goal_action_discriminator_invalid");
            if (!allowed.ContainsKey(action)) variants.RemoveAt(i);
        }
        using var document = JsonDocument.Parse(schema.ToJsonString());
        var contract = original with { Actions = allowed };
        return new(document.RootElement, contract, raw =>
        {
            if (!raw.TryGetProperty(original.ActionArgumentName, out var operation) ||
                !operation.TryGetProperty(original.Discriminator, out var discriminator) ||
                discriminator.ValueKind != JsonValueKind.String || !allowed.ContainsKey(discriminator.GetString()!))
                throw new InvalidOperationException("goal_action_not_permitted");
            return (function.ArgumentBinder ?? throw new InvalidOperationException("goal_action_binder_missing"))(raw);
        });
    }
}
