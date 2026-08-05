namespace HPD.Base;

internal static class BaseQueryFieldResolver
{
    /// <summary>Executes the to stored names operation.</summary>
    public static RecordQuery ToStoredNames(CollectionDefinition collection, RecordQuery query)
    {
        var names = (collection.Fields ?? [])
            .ToDictionary(static field => field.Id, static field => field.Name, StringComparer.Ordinal);

        return query with
        {
            Filter = Resolve(query.Filter, names),
            Sort = query.Sort?.Select(sort => sort with { Field = Name(sort.Field, names) }).ToArray(),
            Select = query.Select?.Select(field => Name(field, names)).ToArray(),
        };
    }

    private static FilterExpression? Resolve(
        FilterExpression? filter,
        IReadOnlyDictionary<string, string> names)
    {
        if (filter is null)
        {
            return null;
        }

        return filter with
        {
            Field = filter.Field is null ? null : Name(filter.Field, names),
            Children = filter.Children?.Select(child => Resolve(child, names)!).ToArray(),
        };
    }

    private static string Name(string id, IReadOnlyDictionary<string, string> names) =>
        names.TryGetValue(id, out var storedName) ? storedName : id;
}
