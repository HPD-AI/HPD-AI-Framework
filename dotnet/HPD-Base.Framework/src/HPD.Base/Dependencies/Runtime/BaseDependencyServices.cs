
namespace HPD.Base;

/// <summary>A trusted value used to resolve an opaque dependency reference.</summary>
public readonly record struct BaseDependencyParameter(string Name, string? Value);

/// <summary>Creates opaque references from registered templates and trusted values.</summary>
public interface IBaseDependencyReferenceFactory
{
    BaseDependencyReference Create(string templateId, params BaseDependencyParameter[] parameters);
    BaseDependencySet CreateSet(params BaseDependencyReference[] references);
}

/// <summary>Supplies additional dependency inputs for a committed mutation.</summary>
public interface IBaseMutationDependencyRule
{
    ValueTask<IReadOnlyList<BaseDependencyInput>> ResolveAsync(
        BaseRecordMutationEvent mutation,
        CancellationToken cancellationToken = default);
}

/// <summary>Describes a trusted dependency before its values are protected.</summary>
public sealed record BaseDependencyInput
{
    public required string TemplateId { get; init; }
    public required BaseDependencyParameter[] Parameters { get; init; }
}

/// <summary>Maps committed record mutations to safe dependency invalidations.</summary>
public interface IBaseDependencyInvalidationMapper
{
    ValueTask<BaseDependencyInvalidation> MapAsync(
        BaseRecordMutationEvent mutation,
        CancellationToken cancellationToken = default);
}

/// <summary>Provides the registered public dependency-template catalog.</summary>
public interface IBaseDependencyTemplateProvider
{
    IReadOnlyList<BaseDependencyTemplate> Templates { get; }
}
