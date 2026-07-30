using System.Collections.ObjectModel;
using System.Text.Json.Serialization.Metadata;
using HPD.Base.Schema;

namespace HPD.Base.Application.Collections;

/// <summary>
/// Represents an immutable, typed application contract for one BASE collection.
/// </summary>
/// <typeparam name="T">The persisted record type.</typeparam>
public sealed class BaseCollection<T>
{
    private readonly ReadOnlyDictionary<string, object> _fields;

    private BaseCollection(
        CollectionDefinition definition,
        JsonTypeInfo<T> jsonTypeInfo,
        IReadOnlyDictionary<string, object> fields)
    {
        Definition = definition;
        JsonTypeInfo = jsonTypeInfo;
        _fields = new ReadOnlyDictionary<string, object>(
            new Dictionary<string, object>(fields, StringComparer.Ordinal));
    }

    /// <summary>
    /// Gets the canonical collection identifier.
    /// </summary>
    public string Id => Definition.Id;

    /// <summary>
    /// Gets the source-generated JSON contract for the persisted type.
    /// </summary>
    public JsonTypeInfo<T> JsonTypeInfo { get; }

    /// <summary>
    /// Gets the canonical immutable collection definition.
    /// </summary>
    public CollectionDefinition Definition { get; }

    /// <summary>
    /// Gets the registered typed field with the specified stored path.
    /// </summary>
    /// <typeparam name="TValue">The expected field value type.</typeparam>
    /// <param name="path">The canonical stored field path.</param>
    /// <returns>The typed field contract.</returns>
    /// <exception cref="KeyNotFoundException">The path is not registered.</exception>
    /// <exception cref="InvalidOperationException">The registered field has a different value type.</exception>
    public BaseField<T, TValue> Field<TValue>(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!_fields.TryGetValue(path, out var field))
        {
            throw new KeyNotFoundException(
                $"Collection '{Id}' does not declare field '{path}'.");
        }

        if (field is not BaseField<T, TValue> typed)
        {
            throw new InvalidOperationException(
                $"Collection '{Id}' field '{path}' is not declared as '{typeof(TValue).FullName}'.");
        }

        return typed;
    }

    /// <summary>
    /// Creates a validated manual collection contract.
    /// </summary>
    /// <param name="definition">The canonical collection definition.</param>
    /// <param name="jsonTypeInfo">Source-generated JSON metadata for <typeparamref name="T"/>.</param>
    /// <param name="configure">The typed field declaration callback.</param>
    /// <returns>An immutable typed collection contract.</returns>
    public static BaseCollection<T> Create(
        CollectionDefinition definition,
        JsonTypeInfo<T> jsonTypeInfo,
        Action<BaseCollectionFields<T>> configure)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(jsonTypeInfo);
        ArgumentNullException.ThrowIfNull(configure);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Id);

        if (jsonTypeInfo.Type != typeof(T))
        {
            throw new ArgumentException(
                $"JSON metadata must describe '{typeof(T).FullName}'.",
                nameof(jsonTypeInfo));
        }

        var fields = new BaseCollectionFields<T>();
        configure(fields);
        fields.Seal();

        return new BaseCollection<T>(definition, jsonTypeInfo, fields.Items);
    }
}
