using HPD.Base.Application.Collections;
using HPD.Base.Dependencies;
using HPD.Base.Records;

namespace HPD.Base.Application.Dependencies;

/// <summary>Resolves opaque dependencies without repeating template plumbing.</summary>
public sealed class BaseSessionDependencies(
    IBaseDependencyReferenceFactory factory,
    string? tenantId)
{
    public BaseDependencyReference Collection<T>(BaseCollection<T> collection) =>
        Collection(tenantId, collection);

    public BaseDependencyReference Collection<T>(
        string? tenant,
        BaseCollection<T> collection)
    {
        ArgumentNullException.ThrowIfNull(collection);
        return factory.Create(
            BaseDependencyIds.Collection,
            new BaseDependencyParameter("tenant", tenant),
            new BaseDependencyParameter("collection", collection.Id));
    }

    public BaseDependencyReference Record<T>(
        BaseCollection<T> collection,
        RecordId recordId) =>
        Record(tenantId, collection, recordId);

    public BaseDependencyReference Record<T>(
        string? tenant,
        BaseCollection<T> collection,
        RecordId recordId)
    {
        ArgumentNullException.ThrowIfNull(collection);
        return factory.Create(
            BaseDependencyIds.Record,
            new BaseDependencyParameter("tenant", tenant),
            new BaseDependencyParameter("collection", collection.Id),
            new BaseDependencyParameter("record", recordId.Value));
    }

    public BaseDependencyReference Resolve(
        BaseDependencyTemplateHandle template,
        params ReadOnlySpan<string?> values)
    {
        ArgumentNullException.ThrowIfNull(template);
        return template.Resolve(factory, values);
    }

    public BaseDependencySet Set(params ReadOnlySpan<BaseDependencyReference> references) =>
        factory.CreateSet(references.ToArray());
}

/// <summary>
/// Owns the validated parameter identity, order, visibility, and arity of one
/// dependency template.
/// </summary>
public sealed class BaseDependencyTemplateHandle
{
    private readonly string[] _parameterNames;

    internal BaseDependencyTemplateHandle(BaseDependencyTemplate template)
    {
        Template = template with { ParameterNames = template.ParameterNames.ToArray() };
        _parameterNames = Template.ParameterNames;
    }

    public BaseDependencyTemplate Template { get; }
    public string Id => Template.Id;
    public IReadOnlyList<string> ParameterNames => _parameterNames;

    internal BaseDependencyReference Resolve(
        IBaseDependencyReferenceFactory factory,
        ReadOnlySpan<string?> values)
    {
        if (values.Length != _parameterNames.Length)
        {
            throw new ArgumentException(
                $"Dependency template '{Id}' requires {_parameterNames.Length} values.",
                nameof(values));
        }

        var parameters = new BaseDependencyParameter[values.Length];
        for (var index = 0; index < values.Length; index++)
        {
            parameters[index] = new BaseDependencyParameter(
                _parameterNames[index],
                values[index]);
        }

        return factory.Create(Id, parameters);
    }
}
