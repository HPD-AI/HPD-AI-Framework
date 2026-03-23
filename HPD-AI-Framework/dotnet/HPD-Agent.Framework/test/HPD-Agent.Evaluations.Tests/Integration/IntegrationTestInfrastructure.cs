// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

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

    public IProviderFeatures? GetProvider(string providerKey) =>
        providerKey == "test" ? new StubProviderFeatures(_client ?? new StubChatClient()) : null;

    public IReadOnlyCollection<string> GetRegisteredProviders() => ["test"];
    public void Register(IProviderFeatures provider) { }
    public bool IsRegistered(string providerKey) => providerKey == "test";
    public void Clear() { }
}

internal sealed class StubProviderFeatures(IChatClient client) : IProviderFeatures
{
    public string ProviderKey => "test";
    public string DisplayName => "Test";
    public IChatClient CreateChatClient(ProviderConfig config, IServiceProvider? services) => client;
    public HPD.Agent.ErrorHandling.IProviderErrorHandler CreateErrorHandler() => new StubErrorHandler();
    public ProviderMetadata GetMetadata() => new() { ProviderKey = "test", DisplayName = "Test", SupportsStreaming = true, SupportsFunctionCalling = true };
    public ProviderValidationResult ValidateConfiguration(ProviderConfig config) => ProviderValidationResult.Success();
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
    private readonly Dictionary<(string sessionId, string branchId), Branch> _branches = new();

    public void AddBranch(string sessionId, Branch branch) =>
        _branches[(sessionId, branch.Id)] = branch;

    public Task<Branch?> LoadBranchAsync(string sessionId, string branchId, CancellationToken ct = default) =>
        Task.FromResult(_branches.GetValueOrDefault((sessionId, branchId)));

    public Task SaveBranchAsync(string sessionId, Branch branch, CancellationToken ct = default)
    {
        _branches[(sessionId, branch.Id)] = branch;
        return Task.CompletedTask;
    }

    public Task<Session?> LoadSessionAsync(string sessionId, CancellationToken ct = default) => Task.FromResult<Session?>(null);
    public Task SaveSessionAsync(Session session, CancellationToken ct = default) => Task.CompletedTask;
    public Task<List<string>> ListSessionIdsAsync(CancellationToken ct = default) => Task.FromResult(new List<string>());
    public Task DeleteSessionAsync(string sessionId, CancellationToken ct = default) => Task.CompletedTask;
    public Task<List<string>> ListBranchIdsAsync(string sessionId, CancellationToken ct = default) => Task.FromResult(new List<string>());
    public Task DeleteBranchAsync(string sessionId, string branchId, CancellationToken ct = default) => Task.CompletedTask;
    public Task<UncommittedTurn?> LoadUncommittedTurnAsync(string sessionId, CancellationToken ct = default) => Task.FromResult<UncommittedTurn?>(null);
    public Task SaveUncommittedTurnAsync(UncommittedTurn turn, CancellationToken ct = default) => Task.CompletedTask;
    public Task DeleteUncommittedTurnAsync(string sessionId, CancellationToken ct = default) => Task.CompletedTask;
    public IContentStore? GetContentStore(string sessionId) => null;
    public Task<int> DeleteInactiveSessionsAsync(TimeSpan threshold, bool dryRun = false, CancellationToken ct = default) => Task.FromResult(0);
}

// ── BranchBuilder ─────────────────────────────────────────────────────────

/// <summary>
/// Fluent builder for Branch instances used in integration tests.
/// </summary>
internal sealed class BranchBuilder
{
    private readonly string _sessionId;
    private readonly string _branchId;
    private readonly List<ChatMessage> _messages = new();

    public BranchBuilder(string sessionId = "sess-1", string branchId = "branch-1")
    {
        _sessionId = sessionId;
        _branchId = branchId;
    }

    public BranchBuilder AddUserMessage(string text)
    {
        _messages.Add(new ChatMessage(ChatRole.User, text));
        return this;
    }

    public BranchBuilder AddAssistantMessage(string text)
    {
        _messages.Add(new ChatMessage(ChatRole.Assistant, text));
        return this;
    }

    public BranchBuilder AddToolCall(string callId, string toolName, string result)
    {
        var callMsg = new ChatMessage(ChatRole.Assistant,
            [new FunctionCallContent(callId, toolName, new Dictionary<string, object?>())]);
        var resultMsg = new ChatMessage(ChatRole.Tool,
            [new FunctionResultContent(callId, result)]);
        _messages.Add(callMsg);
        _messages.Add(resultMsg);
        return this;
    }

    public Branch Build()
    {
        // Use internal Branch(sessionId, branchId) constructor (accessible via InternalsVisibleTo)
        var branch = new Branch(_sessionId, _branchId);
        branch.Messages.AddRange(_messages);
        return branch;
    }
}
