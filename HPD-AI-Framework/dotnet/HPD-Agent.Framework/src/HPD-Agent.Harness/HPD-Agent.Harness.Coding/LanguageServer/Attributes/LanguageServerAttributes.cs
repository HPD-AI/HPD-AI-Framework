namespace HPDOS.ToolHarnesses.Middleware;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class HpdLanguageServerAttribute(string id) : Attribute
{
    public string Id => id;
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class LanguageServerExtensionsAttribute(params string[] extensions) : Attribute
{
    public IReadOnlyList<string> Extensions => extensions;
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class LanguageServerLanguageIdsAttribute(params string[] extensionLanguageIdPairs) : Attribute
{
    public IReadOnlyList<string> ExtensionLanguageIdPairs => extensionLanguageIdPairs;
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class LanguageServerRootMarkersAttribute(params string[] markers) : Attribute
{
    public IReadOnlyList<string> Markers => markers;
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class LanguageServerExcludeRootMarkersAttribute(params string[] markers) : Attribute
{
    public IReadOnlyList<string> Markers => markers;
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class LanguageServerExecutableAttribute(string executable) : Attribute
{
    public string Executable => executable;
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class LanguageServerArgumentsAttribute(params string[] arguments) : Attribute
{
    public IReadOnlyList<string> Arguments => arguments;
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class LanguageServerExperimentalAttribute : Attribute
{
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class LanguageServerDisabledByDefaultAttribute : Attribute
{
}
