namespace HPD.TUI.Models;

public sealed class FilterState<T>
{
    private string _query = string.Empty;

    public string Query
    {
        get => _query;
        set => _query = value ?? string.Empty;
    }

    public Func<CollectionItem<T>, string, bool>? Predicate { get; set; }

    public Func<CollectionItem<T>, string, int>? Score { get; set; }

    public bool Matches(CollectionItem<T> item)
    {
        ArgumentNullException.ThrowIfNull(item);

        if (string.IsNullOrWhiteSpace(_query))
        {
            return true;
        }

        if (Predicate is { } predicate)
        {
            return predicate(item, _query);
        }

        return ContainsQuery(item.Title)
            || ContainsQuery(item.Description)
            || ContainsQuery(item.Category)
            || ContainsQuery(item.Footer);
    }

    private bool ContainsQuery(string? text)
    {
        return !string.IsNullOrEmpty(text)
            && text.Contains(_query, StringComparison.OrdinalIgnoreCase);
    }
}
