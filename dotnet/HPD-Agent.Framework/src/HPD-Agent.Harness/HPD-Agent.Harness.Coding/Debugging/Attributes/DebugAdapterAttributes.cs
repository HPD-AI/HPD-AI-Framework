using HPD.Agent.ToolHarness.Coding.Debugging;

namespace HPD.Agent.ToolHarness.Coding.Debugging.Attributes;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class HpdDebugAdapterAttribute(string id) : Attribute
{
    public string Id { get; } = id;
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DebugAdapterLanguagesAttribute(params string[] languages) : Attribute
{
    public IReadOnlyList<string> Languages { get; } = languages;
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DebugAdapterFileExtensionsAttribute(params string[] extensions) : Attribute
{
    public IReadOnlyList<string> Extensions { get; } = extensions;
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DebugAdapterRootMarkersAttribute(params string[] markers) : Attribute
{
    public IReadOnlyList<string> Markers { get; } = markers;
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DebugAdapterTargetKindsAttribute(DebugTargetKind targetKinds) : Attribute
{
    public DebugTargetKind TargetKinds { get; } = targetKinds;
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DebugAdapterProgramKindsAttribute(DebugAdapterProgramKind programKinds) : Attribute
{
    public DebugAdapterProgramKind ProgramKinds { get; } = programKinds;
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DebugAdapterFactoryAttribute(Type factoryType) : Attribute
{
    public Type FactoryType { get; } = factoryType;
}

[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = true)]
public sealed class DebugAdapterCommandHintAttribute(string command) : Attribute
{
    public string Command { get; } = command;
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DebugAdapterArgumentHintsAttribute(params string[] arguments) : Attribute
{
    public IReadOnlyList<string> Arguments { get; } = arguments;
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DebugAdapterInstallGuidanceAttribute(string guidanceId) : Attribute
{
    public string GuidanceId { get; } = guidanceId;
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DebugAdapterPriorityAttribute(int priority) : Attribute
{
    public int Priority { get; } = priority;
}

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DebugAdapterExperimentalAttribute : Attribute;

[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class DebugAdapterDisabledByDefaultAttribute : Attribute;
