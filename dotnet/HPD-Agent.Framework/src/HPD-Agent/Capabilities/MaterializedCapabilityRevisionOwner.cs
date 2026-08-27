using System.Collections.Immutable;
using Microsoft.Extensions.AI;

namespace HPD.Agent;

/// <summary>Owns a materialized in-process capability revision with no subordinate resources.</summary>
internal sealed class MaterializedCapabilityRevisionOwner : ICapabilitySourceRevisionOwner
{
    /// <summary>Creates a validated materialized revision.</summary>
    internal MaterializedCapabilityRevisionOwner(
        CapabilitySourceId sourceId,
        CapabilitySourceRevision revision,
        IEnumerable<AIFunction> functions)
    {
        SourceId = sourceId;
        Revision = revision;
        var materialized = functions.OrderBy(static function => function.Name, StringComparer.Ordinal)
            .ToImmutableArray();
        Snapshot = new CapabilitySourceSnapshot
        {
            Functions = materialized,
            Descriptors = materialized.ToImmutableDictionary(
                static function => GetMetadata(function).Id,
                function =>
                {
                    var metadata = GetMetadata(function);
                    return new CapabilityDescriptor
                    {
                        Id = metadata.Id,
                        SourceId = sourceId,
                        SourceRevision = revision,
                        ModelName = function.Name,
                        Kind = metadata.Kind
                    };
                })
        };
    }

    /// <inheritdoc />
    public CapabilitySourceId SourceId { get; }
    /// <inheritdoc />
    public CapabilitySourceRevision Revision { get; }
    /// <inheritdoc />
    public CapabilitySourceSnapshot Snapshot { get; }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => ValueTask.CompletedTask;

    private static HPDCapabilityMetadata GetMetadata(AIFunction function)
    {
        if (function.AdditionalProperties?.TryGetValue(
                HPDCapabilityMetadata.AdditionalPropertiesKey,
                out var value) != true || value is not HPDCapabilityMetadata metadata)
            throw new InvalidOperationException($"Function '{function.Name}' has no typed capability metadata.");
        return metadata;
    }
}
