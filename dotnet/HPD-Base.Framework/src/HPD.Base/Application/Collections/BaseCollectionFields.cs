namespace HPD.Base;

/// <summary>
/// Builds the typed field set for a manual collection contract.
/// </summary>
/// <typeparam name="TRecord">The persisted record type.</typeparam>
public sealed class BaseCollectionFields<TRecord>
{
    private readonly Dictionary<string, object> _items = new(StringComparer.Ordinal);
    private readonly HashSet<string> _applicationNames = new(StringComparer.Ordinal);
    private readonly HashSet<string> _wireNames = new(StringComparer.Ordinal);
    private bool _sealed;

    internal IReadOnlyDictionary<string, object> Items => _items;

    /// <summary>
    /// Declares one typed field.
    /// </summary>
    /// <typeparam name="TValue">The field value type.</typeparam>
    /// <param name="id">The stable logical field identifier.</param>
    /// <param name="applicationName">The exact application-facing property identity.</param>
    /// <param name="wireName">The exact serializer-owned wire identity.</param>
    /// <param name="nullable">Whether the persisted field accepts null.</param>
    /// <param name="operators">The query operations supported by the field.</param>
    /// <returns>The typed field contract.</returns>
    public BaseField<TRecord, TValue> Add<TValue>(
        string id,
        string applicationName,
        string wireName,
        bool nullable = false,
        BaseFieldOperator operators = BaseFieldOperator.Equal)
    {
        ObjectDisposedException.ThrowIf(_sealed, this);
        BaseApplicationId.Validate(id, nameof(id));
        ArgumentException.ThrowIfNullOrWhiteSpace(applicationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(wireName);

        var field = new BaseField<TRecord, TValue>(id, applicationName, wireName, nullable, operators);
        if (!_items.TryAdd(id, field))
        {
            throw new InvalidOperationException(
                $"Field '{id}' is already declared.");
        }

        if (!_applicationNames.Add(applicationName))
        {
            _items.Remove(id);
            throw new InvalidOperationException(
                $"Field application name '{applicationName}' is already declared.");
        }

        if (!_wireNames.Add(wireName))
        {
            _items.Remove(id);
            _applicationNames.Remove(applicationName);
            throw new InvalidOperationException(
                $"Field wire name '{wireName}' is already declared.");
        }

        return field;
    }

    internal void Seal() => _sealed = true;
}
