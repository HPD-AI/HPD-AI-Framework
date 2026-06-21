namespace HPD.TUI.Extensions;

public sealed class ExtensionManager
{
    private readonly List<IExtension> _extensions = [];

    public IReadOnlyList<IExtension> Extensions => _extensions;

    public TuiExtensionRegistry Registry { get; } = new();

    public void Load(IExtension extension)
    {
        ArgumentNullException.ThrowIfNull(extension);

        extension.Initialize(new ExtensionContext(Registry));
        _extensions.Add(extension);
    }
}
