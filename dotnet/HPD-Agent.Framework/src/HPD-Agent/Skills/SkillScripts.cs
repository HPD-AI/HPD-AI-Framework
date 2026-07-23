using HPD.Agent;
using HPD.Agent.Middleware;
using System.Text.Json;

/// <summary>Describes an external script capability with an explicit model input contract.</summary>
public sealed class SkillScript : SkillCapability
{
    /// <summary>Initializes a script capability.</summary>
    public SkillScript(string name, string description) : base(name, description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
    }

    /// <summary>Gets the external script reference.</summary>
    public required SkillScriptReference Reference { get; init; }

    /// <summary>Gets the required schema, validation, and binding contract for model arguments.</summary>
    public required SkillScriptInputContract InputContract { get; init; }

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

    /// <summary>Executes a script using validated arguments and established agent context.</summary>
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
    SkillScriptArguments Arguments,
    FunctionExecutionContext FunctionContext,
    IServiceProvider? Services,
    IContentStore? ContentStore);

/// <summary>Provides effective canonical JSON and an optional generated CLR value to a script runner.</summary>
public sealed class SkillScriptArguments
{
    private readonly object _value;

    /// <summary>Initializes script arguments from effective JSON and an explicitly described bound value.</summary>
    /// <param name="json">The detached effective canonical JSON.</param>
    /// <param name="value">The generated CLR value or validated dynamic JSON value.</param>
    /// <param name="boundType">The generated CLR type, or <see langword="null"/> for dynamic JSON.</param>
    /// <param name="contractFingerprint">The canonical input-contract fingerprint.</param>
    public SkillScriptArguments(
        JsonElement json,
        object value,
        Type? boundType,
        string contractFingerprint)
    {
        Json = json.Clone();
        _value = value;
        BoundType = boundType;
        ContractFingerprint = contractFingerprint;
    }

    /// <summary>Gets the effective canonical JSON argument object supplied to every runtime.</summary>
    public JsonElement Json { get; }

    /// <summary>Gets the generated CLR type, or <see langword="null"/> for a data-driven contract.</summary>
    public Type? BoundType { get; }

    /// <summary>Gets the stable input-contract fingerprint.</summary>
    public string ContractFingerprint { get; }

    /// <summary>Attempts to retrieve the generated CLR input value.</summary>
    public bool TryGet<T>(out T? value)
    {
        if (_value is T typed)
        {
            value = typed;
            return true;
        }
        value = default;
        return false;
    }

    /// <summary>Gets the generated CLR input value or throws when this contract does not produce it.</summary>
    public T GetRequired<T>() => TryGet<T>(out var value)
        ? value!
        : throw new InvalidOperationException($"This script invocation does not contain a bound '{typeof(T).FullName}' value.");

    /// <summary>Writes the effective canonical JSON to an existing UTF-8 JSON writer.</summary>
    /// <param name="writer">The destination JSON writer.</param>
    public void WriteJson(Utf8JsonWriter writer)
    {
        ArgumentNullException.ThrowIfNull(writer);
        Json.WriteTo(writer);
    }

    /// <summary>Writes the effective canonical JSON as UTF-8 to a destination stream.</summary>
    /// <param name="destination">The writable destination stream.</param>
    /// <param name="cancellationToken">Cancels asynchronous flushing.</param>
    public async ValueTask WriteJsonAsync(
        Stream destination,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(destination);
        if (!destination.CanWrite)
            throw new ArgumentException("The destination stream must be writable.", nameof(destination));
        using var writer = new Utf8JsonWriter(destination);
        WriteJson(writer);
        await writer.FlushAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Owns the schema, binding, and effective JSON semantics for one skill script.</summary>
public abstract class SkillScriptInputContract
{
    /// <summary>Gets the generated CLR input type, or <see langword="null"/> for a data-driven contract.</summary>
    public abstract Type? BoundType { get; }

    /// <summary>Gets the immutable canonical JSON Schema.</summary>
    public abstract JsonElement JsonSchema { get; }

    /// <summary>Gets the stable canonical schema fingerprint.</summary>
    public abstract string CanonicalSchemaFingerprint { get; }

    internal abstract AIFunctionBindingResult Bind(JsonElement arguments);
}

/// <summary>Creates explicit input contracts for skill scripts.</summary>
public static class SkillScriptInput
{
    private static readonly SkillScriptInputContract s_empty =
        new ContractAdapter(CanonicalJsonInputContract.Create(
            JsonDocument.Parse("""{"type":"object","properties":{},"required":[],"additionalProperties":false}""").RootElement));

    /// <summary>Gets the explicit closed empty-object contract for scripts with no model arguments.</summary>
    public static SkillScriptInputContract Empty => s_empty;

    /// <summary>Adapts a reusable generated input contract for script execution.</summary>
    /// <typeparam name="T">The generated CLR input type.</typeparam>
    /// <param name="contract">The reusable generated input contract.</param>
    /// <returns>A script input-contract adapter.</returns>
    public static SkillScriptInputContract Generated<T>(IAIInputContract<T> contract) =>
        new ContractAdapter(contract ?? throw new ArgumentNullException(nameof(contract)));

    /// <summary>Compiles a bounded HPD canonical JSON Schema into a data-driven script input contract.</summary>
    /// <param name="schema">The closed canonical schema to compile immediately.</param>
    /// <returns>A data-driven script input contract.</returns>
    public static SkillScriptInputContract FromCanonicalSchema(JsonElement schema) =>
        new ContractAdapter(CanonicalJsonInputContract.Create(schema));

    private sealed class ContractAdapter(IAIInputContract contract) : SkillScriptInputContract
    {
        public override Type? BoundType => contract.BoundType;
        public override JsonElement JsonSchema => contract.JsonSchema;
        public override string CanonicalSchemaFingerprint => contract.CanonicalSchemaFingerprint;
        internal override AIFunctionBindingResult Bind(JsonElement arguments) => contract.Bind(arguments);
    }
}
