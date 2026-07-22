using HPD.Agent;
using HPD.Agent.Middleware;

/// <summary>Describes a parameterless external script capability.</summary>
public sealed class SkillScript : SkillCapability
{
    /// <summary>Initializes a script capability.</summary>
    public SkillScript(string name, string description) : base(name, description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
    }

    /// <summary>Gets the external script reference.</summary>
    public required SkillScriptReference Reference { get; init; }

    /// <summary>Gets whether invocation requires permission.</summary>
    public bool RequiresPermission { get; init; } = true;

    /// <summary>Gets the maximum execution duration.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(2);

    /// <summary>Gets the maximum encoded output size.</summary>
    public long MaximumOutputBytes { get; init; } = 1_048_576;

    /// <summary>Gets the package content store binding used by installed scripts.</summary>
    internal IContentStore? ContentStore { get; init; }
}

/// <summary>Base type for storage-neutral external script references.</summary>
public abstract record SkillScriptReference(string Runtime)
{
    /// <summary>Gets the runner runtime identifier.</summary>
    public string Runtime { get; init; } = string.IsNullOrWhiteSpace(Runtime)
        ? throw new ArgumentException("A script runtime is required.", nameof(Runtime))
        : Runtime;
}

/// <summary>References an exact script snapshot in an IContentStore.</summary>
public sealed record ContentStoreScriptReference(ContentAddress Address, string Runtime)
    : SkillScriptReference(Runtime);

/// <summary>References a script embedded in a generated package assembly.</summary>
public sealed record EmbeddedScriptReference(string ResourceName, string Runtime)
    : SkillScriptReference(Runtime);

/// <summary>References a script beneath an approved package root.</summary>
public sealed record FileScriptReference(string RelativePath, string Runtime)
    : SkillScriptReference(Runtime);

/// <summary>References a script resolved by a trusted remote runner.</summary>
public sealed record UriScriptReference(Uri Uri, string Runtime)
    : SkillScriptReference(Runtime);

/// <summary>Executes supported skill scripts behind a host-defined isolation boundary.</summary>
public interface ISkillScriptRunner
{
    /// <summary>Returns whether this runner can execute the supplied script.</summary>
    bool CanRun(SkillScript script);

    /// <summary>Executes a script using established agent context rather than model arguments.</summary>
    ValueTask<object?> RunAsync(SkillScriptExecutionContext context, CancellationToken cancellationToken);
}

/// <summary>Classifies a bounded external-script execution failure.</summary>
public enum SkillScriptErrorCategory
{
    /// <summary>No compatible runner was registered.</summary>
    RunnerUnavailable,
    /// <summary>Execution permission was denied.</summary>
    PermissionDenied,
    /// <summary>The configured execution deadline elapsed.</summary>
    TimedOut,
    /// <summary>The caller cancelled execution.</summary>
    Cancelled,
    /// <summary>The referenced script content is invalid or unavailable.</summary>
    InvalidContent,
    /// <summary>The runner failed while executing the script.</summary>
    ExecutionFailed,
    /// <summary>The result exceeds the configured byte limit.</summary>
    OutputTooLarge,
    /// <summary>The runner returned a result shape unsupported by Native AOT serialization.</summary>
    UnsupportedResult
}

/// <summary>A non-sensitive categorized script execution failure.</summary>
public sealed class SkillScriptExecutionException : Exception
{
    /// <summary>Initializes a categorized script failure.</summary>
    public SkillScriptExecutionException(
        SkillScriptErrorCategory category,
        string message,
        Exception? innerException = null) : base(message, innerException) => Category = category;

    /// <summary>Gets the stable failure category.</summary>
    public SkillScriptErrorCategory Category { get; }
}

/// <summary>Context supplied to a script runner.</summary>
public sealed record SkillScriptExecutionContext(
    string SkillName,
    SkillScript Script,
    FunctionExecutionContext FunctionContext,
    IServiceProvider? Services,
    IContentStore? ContentStore);
