#pragma warning disable MEAI001
using System.Collections.Immutable;
using Microsoft.Extensions.AI;
using HPD.Agent.Serialization;

namespace HPD.Agent;

/// <summary>
/// Host-owned state used to construct and run an agent.
/// These values are intentionally excluded from the portable <see cref="AgentConfig"/>.
/// </summary>
internal sealed class AgentRuntimeOptions
{
    public string? SourceDirectory { get; set; }

    public string? AgentId { get; set; }

    public ISessionStore? SessionStore { get; set; }

    public AgentEventComposition? EventComposition { get; set; }

    public IAgentStore? AgentStore { get; set; }

    public AgentStoreOptions? AgentStoreOptions { get; set; }

    public SessionStoreOptions? SessionStoreOptions { get; set; }

    public ImmutableHashSet<string> ExplicitlyRegisteredToolHarnesses { get; set; } =
        ImmutableHashSet<string>.Empty;

    public IList<AITool>? ServerConfiguredTools { get; set; } = new List<AITool>();

    public Action<ChatOptions>? ConfigureOptions { get; set; }

    public AgentClientMiddlewareConfig? ClientMiddleware { get; set; }

    public AgentPolicyExtensionRegistry PolicyExtensions { get; } = new();
}

/// <summary>Named executable policy implementations supplied by the host.</summary>
internal sealed class AgentPolicyExtensionRegistry
{
    private readonly Dictionary<string, Func<Exception, int, CancellationToken, Task<TimeSpan?>>> _functionRetryStrategies =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<Exception, int, CancellationToken, Task<TimeSpan?>>> _modelRetryStrategies =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<FunctionResultContent, bool>> _errorDetectors =
        new(StringComparer.Ordinal);
    private readonly Dictionary<string, Func<string, CancellationToken, Task<IEnumerable<PIIMatch>>>> _externalPIIDetectors =
        new(StringComparer.Ordinal);
    private AgentPolicyExtensionRegistry? _parent;

    public void SetParent(AgentPolicyExtensionRegistry parent)
    {
        ArgumentNullException.ThrowIfNull(parent);
        if (ReferenceEquals(parent, this))
            throw new ArgumentException("A policy extension registry cannot be its own parent.", nameof(parent));
        _parent ??= parent;
    }

    public void AddFunctionRetryStrategy(
        string name,
        Func<Exception, int, CancellationToken, Task<TimeSpan?>> strategy)
        => Add(_functionRetryStrategies, name, strategy);

    public void AddModelRetryStrategy(
        string name,
        Func<Exception, int, CancellationToken, Task<TimeSpan?>> strategy)
        => Add(_modelRetryStrategies, name, strategy);

    public void AddErrorDetector(string name, Func<FunctionResultContent, bool> detector)
        => Add(_errorDetectors, name, detector);

    public void AddExternalPIIDetector(
        string name,
        Func<string, CancellationToken, Task<IEnumerable<PIIMatch>>> detector)
        => Add(_externalPIIDetectors, name, detector);

    public bool TryGetFunctionRetryStrategy(
        string name,
        out Func<Exception, int, CancellationToken, Task<TimeSpan?>>? strategy)
        => _functionRetryStrategies.TryGetValue(name, out strategy) ||
           _parent?.TryGetFunctionRetryStrategy(name, out strategy) == true;

    public bool TryGetModelRetryStrategy(
        string name,
        out Func<Exception, int, CancellationToken, Task<TimeSpan?>>? strategy)
        => _modelRetryStrategies.TryGetValue(name, out strategy) ||
           _parent?.TryGetModelRetryStrategy(name, out strategy) == true;

    public bool TryGetErrorDetector(string name, out Func<FunctionResultContent, bool>? detector)
        => _errorDetectors.TryGetValue(name, out detector) ||
           _parent?.TryGetErrorDetector(name, out detector) == true;

    public bool TryGetExternalPIIDetector(
        string name,
        out Func<string, CancellationToken, Task<IEnumerable<PIIMatch>>>? detector)
        => _externalPIIDetectors.TryGetValue(name, out detector) ||
           _parent?.TryGetExternalPIIDetector(name, out detector) == true;

    private static void Add<T>(
        Dictionary<string, T> registry,
        string name,
        T implementation)
        where T : class
    {
        ArgumentNullException.ThrowIfNull(implementation);
        name = ValidateName(name);
        if (!registry.TryAdd(name, implementation))
            throw new InvalidOperationException($"Policy extension '{name}' is already registered.");
    }

    internal static string ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (name.Length > 128)
            throw new ArgumentException("Policy extension names must be at most 128 characters.", nameof(name));
        if (name.Any(char.IsControl))
            throw new ArgumentException("Policy extension names cannot contain control characters.", nameof(name));
        return name;
    }
}

/// <summary>
/// Runtime-only wrappers for provider-created client families.
/// </summary>
internal sealed class AgentClientMiddlewareConfig
{
    public List<Func<IChatClient, IServiceProvider?, IChatClient>>? Chat { get; set; }

    public List<Func<ITextToSpeechClient, IServiceProvider?, ITextToSpeechClient>>? TextToSpeech { get; set; }

    public List<Func<ISpeechToTextClient, IServiceProvider?, ISpeechToTextClient>>? SpeechToText { get; set; }

    public List<Func<IRealtimeClient, IServiceProvider?, IRealtimeClient>>? Realtime { get; set; }

    public List<Func<IImageGenerator, IServiceProvider?, IImageGenerator>>? ImageGeneration { get; set; }

    public List<Func<IEmbeddingGenerator, IServiceProvider?, IEmbeddingGenerator>>? Embeddings { get; set; }

    public List<Func<IHostedFileClient, IServiceProvider?, IHostedFileClient>>? HostedFiles { get; set; }
}
