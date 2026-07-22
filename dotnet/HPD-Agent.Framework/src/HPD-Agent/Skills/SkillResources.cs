using System.Text;
using HPD.Agent;
using HPD.Agent.Middleware;

/// <summary>Base type for a read-only model-visible skill resource.</summary>
public abstract class SkillResource : SkillCapability
{
    /// <summary>Initializes a resource.</summary>
    protected SkillResource(string name, string description) : base(name, description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
    }

    /// <summary>Gets the resource result type used to describe its AI function.</summary>
    public virtual Type ResultType => typeof(string);

    /// <summary>Reads the resource without mutating its origin.</summary>
    public abstract ValueTask<object?> ReadAsync(
        SkillResourceContext context,
        CancellationToken cancellationToken);
}

/// <summary>Context supplied to a resource provider.</summary>
public sealed record SkillResourceContext(
    string SkillName,
    FunctionExecutionContext FunctionContext,
    IServiceProvider? Services,
    IContentStore? ContentStore);

/// <summary>Classifies a non-sensitive resource read failure.</summary>
public enum SkillResourceErrorCategory
{
    /// <summary>The referenced content does not exist.</summary>
    NotFound,
    /// <summary>Policy denied access to the content.</summary>
    AccessDenied,
    /// <summary>The content representation is unsupported.</summary>
    UnsupportedContent,
    /// <summary>The backing source is unavailable.</summary>
    SourceUnavailable,
    /// <summary>The content exceeds a configured limit.</summary>
    ContentTooLarge,
    /// <summary>The requested immutable version is invalid.</summary>
    InvalidVersion
}

/// <summary>A non-sensitive result returned when dynamic resource content is unavailable.</summary>
public sealed record SkillResourceUnavailableResult(
    string ResourceName,
    SkillResourceErrorCategory Category,
    string Message);

/// <summary>A resource whose value is stored directly in the skill definition.</summary>
public sealed class InlineSkillResource : SkillResource
{
    private readonly object? _content;

    /// <summary>Initializes an inline resource.</summary>
    public InlineSkillResource(string name, string description, object? content)
        : base(name, description) => _content = content;

    /// <inheritdoc />
    public override Type ResultType => _content?.GetType() ?? typeof(object);

    /// <inheritdoc />
    public override ValueTask<object?> ReadAsync(SkillResourceContext context, CancellationToken cancellationToken)
        => ValueTask.FromResult(_content);
}

/// <summary>A resource resolved by a direct AOT-safe delegate on each read.</summary>
public sealed class DelegateSkillResource : SkillResource
{
    private readonly Func<SkillResourceContext, CancellationToken, ValueTask<object?>> _provider;
    private readonly Type _resultType;

    /// <summary>Initializes a delegate-backed resource.</summary>
    public DelegateSkillResource(
        string name,
        string description,
        Func<SkillResourceContext, CancellationToken, ValueTask<object?>> provider,
        Type? resultType = null) : base(name, description)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _resultType = resultType ?? typeof(object);
    }

    /// <inheritdoc />
    public override Type ResultType => _resultType;

    /// <inheritdoc />
    public override ValueTask<object?> ReadAsync(SkillResourceContext context, CancellationToken cancellationToken)
        => _provider(context, cancellationToken);
}

/// <summary>An embedded resource opened through a generated or explicitly supplied accessor.</summary>
public sealed class EmbeddedSkillResource : SkillResource
{
    private readonly Func<Stream> _open;

    /// <summary>Initializes an embedded resource without runtime assembly scanning.</summary>
    public EmbeddedSkillResource(string name, string description, Func<Stream> open)
        : base(name, description) => _open = open ?? throw new ArgumentNullException(nameof(open));

    /// <inheritdoc />
    public override async ValueTask<object?> ReadAsync(SkillResourceContext context, CancellationToken cancellationToken)
    {
        await using var stream = _open() ?? throw new InvalidOperationException($"Embedded skill resource '{Name}' was not found.");
        using var reader = new StreamReader(stream, Encoding.UTF8, true, 1024, leaveOpen: false);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>Identifies an exact content-store resource snapshot.</summary>
public sealed record ContentStoreSkillContentReference(ContentAddress Address);

/// <summary>A resource read from the invocation content store.</summary>
public sealed class ContentStoreSkillResource : SkillResource
{
    private readonly IContentStore? _boundStore;

    /// <summary>Initializes a content-store resource.</summary>
    public ContentStoreSkillResource(
        string name,
        string description,
        ContentStoreSkillContentReference reference,
        IContentStore? contentStore = null) : base(name, description)
    {
        Reference = reference ?? throw new ArgumentNullException(nameof(reference));
        _boundStore = contentStore;
    }

    /// <summary>Gets the exact stored content reference.</summary>
    public ContentStoreSkillContentReference Reference { get; }

    /// <inheritdoc />
    public override async ValueTask<object?> ReadAsync(SkillResourceContext context, CancellationToken cancellationToken)
    {
        var store = _boundStore ?? context.ContentStore ?? throw new InvalidOperationException(
            $"Skill resource '{Name}' requires an IContentStore, but none is configured.");
        await using var result = await store.OpenReadAsync(Reference.Address, cancellationToken).ConfigureAwait(false);
        if (result is null)
        {
            return new SkillResourceUnavailableResult(
                Name,
                SkillResourceErrorCategory.NotFound,
                "The requested skill resource is no longer available. Refresh or reinstall the skill package.");
        }
        using var reader = new StreamReader(result.Content, Encoding.UTF8, true, 1024, leaveOpen: false);
        return await reader.ReadToEndAsync(cancellationToken).ConfigureAwait(false);
    }
}

/// <summary>A development resource constrained beneath an approved root.</summary>
public sealed class FileSkillResource : SkillResource
{
    private readonly string _resolvedPath;

    /// <summary>Initializes a root-constrained file resource.</summary>
    public FileSkillResource(string name, string description, string root, string relativePath)
        : base(name, description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);
        if (Path.IsPathRooted(relativePath))
            throw new ArgumentException("Skill resource paths must be relative.", nameof(relativePath));

        var canonicalRoot = Path.GetFullPath(root);
        var candidate = Path.GetFullPath(Path.Combine(canonicalRoot, relativePath));
        var prefix = canonicalRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? canonicalRoot
            : canonicalRoot + Path.DirectorySeparatorChar;
        if (!candidate.StartsWith(prefix, StringComparison.Ordinal))
            throw new ArgumentException("Skill resource path escapes its approved root.", nameof(relativePath));
        _resolvedPath = candidate;
    }

    /// <inheritdoc />
    public override async ValueTask<object?> ReadAsync(SkillResourceContext context, CancellationToken cancellationToken)
        => await File.ReadAllTextAsync(_resolvedPath, cancellationToken).ConfigureAwait(false);
}
