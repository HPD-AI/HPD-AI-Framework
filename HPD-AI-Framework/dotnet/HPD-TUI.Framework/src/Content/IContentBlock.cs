using HPD.TUI.Core;

namespace HPD.TUI.Content;

public interface IContentBlock : IComponent
{
    ContentBlockKind Kind { get; }
}

public enum ContentBlockKind
{
    Text,
    Markup,
    Markdown,
    Code,
    KeyValue,
    List,
    Separator
}
