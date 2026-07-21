using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>Projects a function set while keeping container metadata consistent with its surviving children.</summary>
internal static class ContainerFunctionProjection
{
    public static IReadOnlyList<AIFunction> Project(
        IEnumerable<AIFunction> functions,
        Func<AIFunction, bool> include)
    {
        var source = functions.ToArray();
        var includedLeaves = source
            .Where(function => !IsContainer(function) && include(function))
            .ToArray();
        var includedNames = includedLeaves
            .Select(function => function.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new List<AIFunction>(source.Length);

        foreach (var function in source)
        {
            if (!IsContainer(function))
            {
                if (includedNames.Contains(function.Name))
                    result.Add(function);
                continue;
            }

            var projected = ProjectContainer(function, includedNames);
            if (projected is not null)
                result.Add(projected);
        }

        return result;
    }

    private static AIFunction? ProjectContainer(AIFunction function, HashSet<string> includedNames)
    {
        var properties = new Dictionary<string, object?>(function.AdditionalProperties);
        var childNames = ReadNames(properties, "ChildFunctions");
        var referencedNames = ReadNames(properties, "ReferencedFunctions");
        var membership = childNames.Length > 0 ? childNames : referencedNames;

        // A container without declared membership is not changed by leaf projection.
        if (membership.Length == 0)
            return function;

        var surviving = membership.Where(name => includedNames.Contains(Unqualify(name))).ToArray();
        if (surviving.Length == 0)
            return null;

        if (childNames.Length > 0)
            properties["ChildFunctions"] = childNames.Where(name => includedNames.Contains(Unqualify(name))).ToArray();
        if (referencedNames.Length > 0)
            properties["ReferencedFunctions"] = referencedNames.Where(name => includedNames.Contains(Unqualify(name))).ToArray();

        if (surviving.Length == membership.Length)
            return function;

        var functionList = string.Join(", ", surviving.Select(Unqualify));
        var action = IsSkill(function) ? "activated" : "expanded";
        var activation = $"{function.Name} {action}. Available functions: {functionList}";
        if (properties.TryGetValue("FunctionResult", out var value) && value is string instructions && !string.IsNullOrWhiteSpace(instructions))
            activation += $"\n\n{instructions}";

        return HPDAIFunctionFactory.Create(
            (_, _, _) => Task.FromResult<object?>(activation),
            new HPDAIFunctionFactoryOptions
            {
                Name = function.Name,
                Description = AppendAvailableFunctions(function.Description, functionList),
                SerializerOptions = function.JsonSerializerOptions,
                ResultType = typeof(string),
                SchemaProvider = () => function.JsonSchema,
                AdditionalProperties = properties
            });
    }

    private static string AppendAvailableFunctions(string description, string functionList)
    {
        var marker = " Contains ";
        var markerIndex = description.LastIndexOf(marker, StringComparison.Ordinal);
        var purpose = markerIndex >= 0 ? description[..markerIndex].TrimEnd() : description.TrimEnd();
        return $"{purpose} Contains {functionList.Split(", ").Length} functions: {functionList}";
    }

    private static bool IsContainer(AIFunction function) =>
        function.AdditionalProperties?.TryGetValue("IsContainer", out var value) == true && value is true;

    private static bool IsSkill(AIFunction function) =>
        function.AdditionalProperties?.TryGetValue("IsSkill", out var value) == true && value is true;

    private static string[] ReadNames(IReadOnlyDictionary<string, object?> properties, string key) =>
        properties.TryGetValue(key, out var value) && value is IEnumerable<string> names
            ? names.ToArray()
            : Array.Empty<string>();

    internal static string Unqualify(string name)
    {
        var separator = name.LastIndexOf('.');
        return separator < 0 ? name : name[(separator + 1)..];
    }
}
