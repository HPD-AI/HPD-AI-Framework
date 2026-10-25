// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using System.Collections.Immutable;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>Materializes function-local activation nodes and their visibility edges.</summary>
internal static class FunctionCollapseProjection
{
    internal static List<AIFunction> Apply(IEnumerable<AIFunction> source, bool enabled)
    {
        var functions = source.ToList();
        if (!enabled)
            return functions;

        var existingIds = functions
            .Select(ReadCapabilityMetadata)
            .Where(static metadata => metadata is not null)
            .Select(static metadata => metadata!.Id)
            .ToHashSet();
        var targets = functions
            .Select(function => (Function: function, Collapse: ReadCollapseMetadata(function)))
            .Where(static entry => entry.Collapse is not null)
            .ToArray();
        if (targets.Length == 0)
            return functions;

        var targetToActivation = new Dictionary<CapabilityId, FunctionCollapseMetadata>();
        foreach (var (function, collapse) in targets)
        {
            var targetMetadata = ReadCapabilityMetadata(function)
                ?? throw new InvalidOperationException($"Collapsible function '{function.Name}' lacks capability metadata.");
            if (!string.Equals(targetMetadata.Id.Value, collapse!.TargetCapabilityId.Value, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    $"Collapsible function '{function.Name}' capability identity does not match its declaration.");

            targetToActivation[targetMetadata.Id] = collapse;
            SetCapabilityMetadata(function, targetMetadata with
            {
                ParentContainerIds = [collapse.ActivationCapabilityId]
            });
            SetLegacyProperty(function, "ParentContainer", collapse.ActivationName);

            if (existingIds.Contains(collapse.ActivationCapabilityId))
                continue;

            var activation = CreateActivation(function, targetMetadata, collapse);
            functions.Add(activation);
            existingIds.Add(collapse.ActivationCapabilityId);
        }

        foreach (var function in functions)
        {
            var metadata = ReadCapabilityMetadata(function);
            if (metadata is null ||
                metadata.Kind == HPDCapabilityKind.FunctionActivation ||
                metadata.Reveals.IsDefaultOrEmpty)
                continue;

            var rewritten = metadata.Reveals;
            foreach (var (targetId, collapse) in targetToActivation)
            {
                if (!rewritten.Contains(targetId))
                    continue;
                rewritten = rewritten.Replace(targetId, collapse.ActivationCapabilityId);
            }
            if (rewritten != metadata.Reveals)
                SetCapabilityMetadata(function, metadata with { Reveals = rewritten });

            RetargetLegacyMembership(function, targetToActivation);
        }

        return functions;
    }

    private static AIFunction CreateActivation(
        AIFunction target,
        HPDCapabilityMetadata targetMetadata,
        FunctionCollapseMetadata collapse)
    {
        var instructions =
            $"{target.Name} is now available with its complete argument schema.";
        if (!string.IsNullOrWhiteSpace(collapse.FunctionResult))
            instructions += $"\n\n{collapse.FunctionResult}";

        var properties = new Dictionary<string, object?>
        {
            [HPDCapabilityMetadata.AdditionalPropertiesKey] = new HPDCapabilityMetadata
            {
                Id = collapse.ActivationCapabilityId,
                Kind = HPDCapabilityKind.FunctionActivation,
                DeclarationMemberName = targetMetadata.DeclarationMemberName,
                OwnerHarnessIdentity = targetMetadata.OwnerHarnessIdentity,
                DeclaringHarnessIdentity = targetMetadata.DeclaringHarnessIdentity,
                ImplementationIdentity = targetMetadata.ImplementationIdentity,
                ParentContainerIds = targetMetadata.ParentContainerIds,
                Reveals = [targetMetadata.Id]
            },
            ["IsContainer"] = true,
            ["ReferencedFunctions"] = new[] { target.Name },
            ["ChildFunctions"] = new[] { target.Name },
            ["FunctionCount"] = 1,
            ["FunctionResult"] = collapse.FunctionResult,
            ["ToolHarnessName"] =
                target.AdditionalProperties?.TryGetValue("ToolHarnessName", out var harnessName) == true
                    ? harnessName
                    : null,
            ["ToolHarnessIdentity"] = collapse.OwnerHarnessIdentity,
            ["SourceType"] = "Function"
        };
        if (!string.IsNullOrWhiteSpace(collapse.SystemPrompt))
            properties["SystemPrompt"] = collapse.SystemPrompt;

        var description = collapse.Description.TrimEnd();
        return HPDAIFunctionFactory.Create(
            (_, _, _) => Task.FromResult<object?>(instructions),
            new HPDAIFunctionFactoryOptions
            {
                Name = collapse.ActivationName,
                Description = description,
                SchemaProvider = CreateEmptySchema,
                SerializerOptions = target.JsonSerializerOptions,
                ResultType = typeof(string),
                AdditionalProperties = properties
            });
    }

    private static void RetargetLegacyMembership(
        AIFunction function,
        IReadOnlyDictionary<CapabilityId, FunctionCollapseMetadata> targetToActivation)
    {
        foreach (var key in new[] { "ReferencedFunctions", "ChildFunctions" })
        {
            if (function.AdditionalProperties?.TryGetValue(key, out var raw) != true ||
                raw is not IEnumerable<string> names)
                continue;

            var rewritten = names.ToArray();
            foreach (var collapse in targetToActivation.Values)
            {
                for (var index = 0; index < rewritten.Length; index++)
                {
                    if (string.Equals(rewritten[index], collapse.TargetName, StringComparison.Ordinal) ||
                        string.Equals(
                            ContainerFunctionProjection.Unqualify(rewritten[index]),
                            collapse.TargetName,
                            StringComparison.Ordinal))
                        rewritten[index] = collapse.ActivationName;
                }
            }
            SetLegacyProperty(function, key, rewritten);
        }
    }

    private static FunctionCollapseMetadata? ReadCollapseMetadata(AIFunction function) =>
        function.AdditionalProperties?.TryGetValue(
            FunctionCollapseMetadata.AdditionalPropertiesKey,
            out var value) == true
            ? value as FunctionCollapseMetadata
            : null;

    private static HPDCapabilityMetadata? ReadCapabilityMetadata(AIFunction function) =>
        function.AdditionalProperties?.TryGetValue(
            HPDCapabilityMetadata.AdditionalPropertiesKey,
            out var value) == true
            ? value as HPDCapabilityMetadata
            : null;

    private static void SetCapabilityMetadata(AIFunction function, HPDCapabilityMetadata metadata)
    {
        if (function.AdditionalProperties is not IDictionary<string, object?> properties)
            throw new InvalidOperationException($"Function '{function.Name}' metadata is immutable.");
        properties[HPDCapabilityMetadata.AdditionalPropertiesKey] = metadata;
    }

    private static void SetLegacyProperty(AIFunction function, string key, object? value)
    {
        if (function.AdditionalProperties is IDictionary<string, object?> properties)
            properties[key] = value;
    }

    private static JsonElement CreateEmptySchema()
    {
        using var document = JsonDocument.Parse(
            "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}");
        return document.RootElement.Clone();
    }
}
