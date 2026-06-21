using HPD.TUI.Core;

namespace HPD.TUI.Models;

public class CollectionItem<T>
{
    public CollectionItem(
        string key,
        T value,
        string title,
        string? description = null,
        string? category = null,
        string? footer = null,
        bool disabled = false,
        Style? style = null,
        IReadOnlyDictionary<string, object?>? metadata = null)
    {
        Key = string.IsNullOrWhiteSpace(key) ? throw new ArgumentException("Key is required.", nameof(key)) : key;
        Value = value;
        Title = title ?? throw new ArgumentNullException(nameof(title));
        Description = description;
        Category = category;
        Footer = footer;
        Disabled = disabled;
        Style = style;
        Metadata = metadata ?? EmptyMetadata;
    }

    private static IReadOnlyDictionary<string, object?> EmptyMetadata { get; } = new Dictionary<string, object?>();

    public string Key { get; }

    public T Value { get; }

    public string Title { get; }

    public string? Description { get; }

    public string? Category { get; }

    public string? Footer { get; }

    public bool Disabled { get; }

    public Style? Style { get; }

    public IReadOnlyDictionary<string, object?> Metadata { get; }
}
