namespace HPD.TUI.Extensions;

public interface IExtension
{
    string Name { get; }

    Version Version { get; }

    void Initialize(ExtensionContext context);
}

public sealed class ExtensionContext
{
    public ExtensionContext(TuiExtensionRegistry registry)
    {
        Registry = registry;
    }

    public TuiExtensionRegistry Registry { get; }
}
