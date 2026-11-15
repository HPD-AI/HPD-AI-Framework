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
