// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: FSL-1.1-ALv2

using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using HPD.Agent.Evaluations.Evaluators;
using HPD.Agent.Providers;
using System.Runtime.CompilerServices;

namespace HPD.Agent.Evaluations.Tests.Integration;

// ── StubProviderRegistry ──────────────────────────────────────────────────

/// <summary>
/// Minimal IProviderRegistry for integration tests — returns a stub "test" provider.
/// </summary>
internal sealed class StubProviderRegistry : IProviderRegistry
{
    private readonly IChatClient? _client;

    public StubProviderRegistry(IChatClient? client = null) => _client = client;

    public IProvider? GetProvider(string providerKey) =>
        providerKey == "test" ? new StubChatClientProvider(_client ?? new StubChatClient()) : null;

    public TProvider? GetProvider<TProvider>(string providerKey)
        where TProvider : class, IProvider
        => GetProvider(providerKey) as TProvider;

    public IReadOnlyCollection<string> GetRegisteredProviders() => ["test"];
    public void Register(IProvider provider) { }
    public bool IsRegistered(string providerKey) => providerKey == "test";
    public void Clear() { }
}

internal sealed class StubChatClientProvider(IChatClient client) : IChatClientProvider
{
    public string ProviderKey => "test";
    public string DisplayName => "Test";
    public IChatClient CreateChatClient(ClientProviderConfig config, IServiceProvider? services) => client;
    public HPD.Agent.ErrorHandling.IProviderErrorHandler CreateErrorHandler() => new StubErrorHandler();
    public ProviderMetadata GetMetadata() => new()
    {
        ProviderKey = "test",
        DisplayName = "Test",
        Families = new Dictionary<ProviderClientFamily, ProviderFamilyDescriptor>
        {
            [ProviderClientFamily.Chat] = new()
            {
                Family = ProviderClientFamily.Chat,
                Capabilities = new Dictionary<string, object?>
                {
                    ["SupportsStreaming"] = true,
                    ["SupportsFunctionCalling"] = true
                }
            }
        }
    };
    public ProviderValidationResult ValidateConfiguration(ClientProviderConfig config, ProviderClientFamily family) => ProviderValidationResult.Success();
}

internal sealed class StubErrorHandler : HPD.Agent.ErrorHandling.IProviderErrorHandler
{
    public HPD.Agent.ErrorHandling.ProviderErrorDetails? ParseError(Exception exception) => null;
    public TimeSpan? GetRetryDelay(HPD.Agent.ErrorHandling.ProviderErrorDetails d, int a, TimeSpan i, double m, TimeSpan x) => null;
    public bool RequiresSpecialHandling(HPD.Agent.ErrorHandling.ProviderErrorDetails d) => false;
}

// ── StubChatClient ────────────────────────────────────────────────────────

/// <summary>
/// Minimal IChatClient that returns a canned text response. Used when a chat
/// client is required to build an Agent but the test doesn't make LLM calls.
/// </summary>
internal sealed class StubChatClient : IChatClient
{
    private readonly Queue<string> _responses = new();

    public ChatClientMetadata Metadata => new("StubChatClient");

    public void EnqueueText(string text) => _responses.Enqueue(text);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken ct = default)
    {
        var text = _responses.TryDequeue(out var t) ? t : "stub response";
        return Task.FromResult(new ChatResponse([new ChatMessage(ChatRole.Assistant, text)]));
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Delay(1, ct);
        var text = _responses.TryDequeue(out var t) ? t : "stub response";
        yield return new ChatResponseUpdate { Contents = [new TextContent(text)], FinishReason = ChatFinishReason.Stop };
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;
    public void Dispose() { }
}

// ── StubDeterministicEvaluator ────────────────────────────────────────────

/// <summary>
/// Minimal deterministic evaluator whose result (pass/fail) is controlled by the test.
/// </summary>
internal sealed class StubDeterministicEvaluator : HpdDeterministicEvaluatorBase
{
    private readonly string _metricName;
    private readonly bool _pass;
    private readonly int _callCount;
    public int CallCount => _callCount;

    // Separate backing field because base class seals EvaluateAsync
    private int _calls;
    public int Calls => _calls;

    public StubDeterministicEvaluator(string metricName, bool pass = true)
    {
        _metricName = metricName;
        _pass = pass;
        _callCount = 0;
    }

    public override IReadOnlyCollection<string> EvaluationMetricNames => [_metricName];

    protected override ValueTask<EvaluationResult> EvaluateDeterministicAsync(
        IEnumerable<ChatMessage> messages,
        ChatResponse modelResponse,
        IEnumerable<EvaluationContext>? additionalContext,
        CancellationToken cancellationToken)
    {
        System.Threading.Interlocked.Increment(ref _calls);
        var metric = new BooleanMetric(_metricName) { Value = _pass };
        var result = new EvaluationResult(metric);
        return ValueTask.FromResult(result);
    }
}

// ── FakeSessionStore ──────────────────────────────────────────────────────

/// <summary>
/// In-memory ISessionStore for RetroactiveScorer tests.
/// </summary>
internal sealed class FakeSessionStore : ISessionStore
{
    private readonly Dictionary<(string sessionId, string threadId), Thread> _threads = new();

    public void AddThread(string sessionId, Thread thread) =>
        _threads[(sessionId, thread.Id)] = thread;

    public Task<Thread?> LoadThreadAsync(string sessionId, string threadId, CancellationToken ct = default) =>
        Task.FromResult(_threads.GetValueOrDefault((sessionId, threadId)));
    public Task<Session?> LoadSessionAsync(string sessionId, CancellationToken ct = default) => Task.FromResult<Session?>(null);
    public Task SaveSessionAsync(Session session, CancellationToken ct = default) => Task.CompletedTask;
    public Task<List<string>> ListSessionIdsAsync(CancellationToken ct = default) => Task.FromResult(new List<string>());
    public Task DeleteSessionAsync(string sessionId, CancellationToken ct = default) => Task.CompletedTask;
    public Task<ThreadEventDocument?> LoadThreadDocumentAsync(string sessionId, string threadId, CancellationToken ct = default) => Task.FromResult<ThreadEventDocument?>(null);
    public Task AppendThreadEventAsync(string sessionId, string threadId, AgentEvent evt, long? expectedSequenceNumber = null, CancellationToken ct = default) => Task.CompletedTask;
    public Task<List<string>> ListThreadIdsAsync(string sessionId, CancellationToken ct = default) => Task.FromResult(new List<string>());
    public Task DeleteThreadAsync(string sessionId, string threadId, CancellationToken ct = default) => Task.CompletedTask;
    public Task<int> DeleteInactiveSessionsAsync(TimeSpan threshold, bool dryRun = false, CancellationToken ct = default) => Task.FromResult(0);
}

// ── ThreadBuilder ─────────────────────────────────────────────────────────

/// <summary>
/// Fluent builder for Thread instances used in integration tests.
/// </summary>
internal sealed class ThreadBuilder
{
    private readonly string _sessionId;
    private readonly string _threadId;
    private readonly List<ChatMessage> _messages = new();

    public ThreadBuilder(string sessionId = "sess-1", string threadId = "thread-1")
    {
        _sessionId = sessionId;
        _threadId = threadId;
    }

    public ThreadBuilder AddUserMessage(string text)
    {
        _messages.Add(new ChatMessage(ChatRole.User, text));
        return this;
    }

    public ThreadBuilder AddAssistantMessage(string text)
    {
        _messages.Add(new ChatMessage(ChatRole.Assistant, text));
        return this;
    }

    public ThreadBuilder AddToolCall(string callId, string toolName, string result)
    {
        var callMsg = new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent(callId, toolName, new Dictionary<string, object?>())]);
        var resultMsg = new ChatMessage(ChatRole.Tool,
            [new FunctionResultContent(callId, result)]);
        _messages.Add(callMsg);
        _messages.Add(resultMsg);
        return this;
    }

    public Thread Build()
    {
        // Use internal Thread(sessionId, threadId) constructor (accessible via InternalsVisibleTo)
        var thread = new Thread(_sessionId, _threadId);
        thread.Messages.AddRange(_messages);
        return thread;
    }
}
