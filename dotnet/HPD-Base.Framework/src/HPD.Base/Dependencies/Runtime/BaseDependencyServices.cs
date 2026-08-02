
namespace HPD.Base;

/// <summary>A trusted value used to resolve an opaque dependency reference.</summary>
public readonly record struct BaseDependencyParameter(string Name, string? Value);

/// <summary>Creates opaque references from registered templates and trusted values.</summary>
public interface IBaseDependencyReferenceFactory
{
    /// <summary>Executes the create operation.</summary>
    BaseDependencyReference Create(string templateId, params BaseDependencyParameter[] parameters);
    /// <summary>Executes the create set operation.</summary>
    BaseDependencySet CreateSet(params BaseDependencyReference[] references);
}

/// <summary>Supplies additional dependency inputs for a committed mutation.</summary>
public interface IBaseMutationDependencyRule
{
    /// <summary>Executes the resolve async operation.</summary>
    ValueTask<IReadOnlyList<BaseDependencyInput>> ResolveAsync(
        BaseRecordMutationEvent mutation,
        CancellationToken cancellationToken = default);
}

/// <summary>Describes a trusted dependency before its values are protected.</summary>
public sealed record BaseDependencyInput
{
    /// <summary>Gets or sets the template ID.</summary>
    public required string TemplateId { get; init; }
    /// <summary>Gets or sets the parameters.</summary>
    public required BaseDependencyParameter[] Parameters { get; init; }
}

/// <summary>Maps committed record mutations to safe dependency invalidations.</summary>
public interface IBaseDependencyInvalidationMapper
{
    /// <summary>Executes the map async operation.</summary>
    ValueTask<BaseDependencyInvalidation> MapAsync(
        BaseRecordMutationEvent mutation,
        CancellationToken cancellationToken = default);
}

/// <summary>Provides the registered public dependency-template catalog.</summary>
public interface IBaseDependencyTemplateProvider
{
    /// <summary>Gets the templates.</summary>
    IReadOnlyList<BaseDependencyTemplate> Templates { get; }
}
