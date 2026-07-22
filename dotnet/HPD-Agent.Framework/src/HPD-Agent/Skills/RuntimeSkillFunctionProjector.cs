using System.ComponentModel;
using System.Collections.Immutable;
using System.Text.Json;
using HPD.Agent.Middleware;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

[EditorBrowsable(EditorBrowsableState.Never)]
internal static class RuntimeSkillFunctionProjector
{
    public static IReadOnlyList<AIFunction> Project(
        string ownerToolHarnessName,
        IReadOnlyList<Skill> skills,
        List<AIFunction> materializedFunctions,
        HPDToolSerializationOptions? serialization)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ownerToolHarnessName);
        ArgumentNullException.ThrowIfNull(skills);
        var additions = new List<AIFunction>();

        foreach (var skill in skills)
        {
            var skillId = CapabilityId.Create($"runtime:{ownerToolHarnessName}:{skill.Id}");
            var children = new List<CapabilityId>();
            foreach (var capability in skill.Capabilities)
            {
                if (capability is ISkillFunctionReference functionReference)
                {
                    var function = materializedFunctions.FirstOrDefault(candidate =>
                        GetMetadata(candidate)?.DeclarationMemberName == functionReference.MemberName &&
                        GetMetadata(candidate)?.Id.Value.StartsWith(
                            $"generated:{functionReference.ToolHarnessType.Name}.",
                            StringComparison.Ordinal) == true)
                        ?? throw new InvalidOperationException(
                            $"Runtime skill '{skill.Id}' references unavailable function " +
                            $"'{functionReference.ToolHarnessType.Name}.{functionReference.MemberName}'.");
                    var metadata = GetMetadata(function)!;
                    if (function.AdditionalProperties is not IDictionary<string, object?> properties)
                        throw new InvalidOperationException(
                            $"Function '{function.Name}' exposes immutable metadata that cannot be normalized.");
                    properties[HPDCapabilityMetadata.AdditionalPropertiesKey] = metadata with
                    {
                        ParentContainerIds = metadata.ParentContainerIds.Add(skillId)
                    };
                    children.Add(metadata.Id);
                }
            }

            var projectedChildren = SkillCapabilityFunctionProjector
                .CreateChildren(skill, skillId, serialization)
                .ToArray();
            additions.AddRange(projectedChildren);
            children.AddRange(projectedChildren.Select(child => GetMetadata(child)!.Id));
            additions.Add(CreateActivation(skill, skillId, children, serialization));
        }

        return additions;
    }

    private static AIFunction CreateActivation(
        Skill skill,
        CapabilityId id,
        IEnumerable<CapabilityId> children,
        HPDToolSerializationOptions? serialization)
        => HPDAIFunctionFactory.Create(
            async (_, functionContext, cancellationToken) =>
            {
                await functionContext.PublishAsync(
                    new SkillActivationStartedEvent(id, skill.Name),
                    cancellationToken).ConfigureAwait(false);
                try
                {
                    var instructions = await skill.Instructions(
                        new SkillInstructionContext(
                            functionContext,
                            functionContext.Services,
                            functionContext.ContentStore),
                        cancellationToken).ConfigureAwait(false);
                    if (skill.Reinforcement is not null)
                    {
                        var reinforcement = await skill.Reinforcement(
                            new SkillInstructionContext(
                                functionContext,
                                functionContext.Services,
                                functionContext.ContentStore),
                            cancellationToken).ConfigureAwait(false);
                        functionContext.ResultMetadata.Set("HPD.SkillReinforcement", reinforcement);
                    }
                    await functionContext.PublishAsync(
                        new SkillActivatedEvent(id, skill.Name, children.Count(), skill.Lifetime),
                        cancellationToken).ConfigureAwait(false);
                    return instructions;
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    await functionContext.PublishAsync(
                        new SkillActivationFailedEvent(id, skill.Name, exception.GetType().Name),
                        CancellationToken.None).ConfigureAwait(false);
                    throw;
                }
            },
            new HPDAIFunctionFactoryOptions
            {
                Name = skill.Name,
                Description = skill.Description,
                SchemaProvider = static () =>
                {
                    using var document = JsonDocument.Parse("{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}");
                    return document.RootElement.Clone();
                },
                SerializerOptions = serialization?.SerializerOptions,
                ResultType = typeof(string),
                AdditionalProperties = new Dictionary<string, object>
                {
                    [HPDCapabilityMetadata.AdditionalPropertiesKey] = new HPDCapabilityMetadata
                    {
                        Id = id,
                        Kind = HPDCapabilityKind.SkillActivation,
                        Reveals = children.ToImmutableArray()
                    },
                    [SkillRuntimeMetadata.SkillDefinitionKey] = skill
                }
            });

    private static HPDCapabilityMetadata? GetMetadata(AIFunction function)
        => function.AdditionalProperties?.TryGetValue(
            HPDCapabilityMetadata.AdditionalPropertiesKey,
            out var value) == true ? value as HPDCapabilityMetadata : null;
}

/// <summary>Stable internal metadata keys used by generated skill registration.</summary>
[EditorBrowsable(EditorBrowsableState.Never)]
public static class SkillRuntimeMetadata
{
    /// <summary>Identifies the immutable skill definition attached to an activation function.</summary>
    public const string SkillDefinitionKey = "HPD.SkillDefinition";
}
