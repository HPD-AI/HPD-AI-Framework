// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using HPD.Agent.Evaluations.Evaluators;
using HPD.Agent.Evaluations.Tracing;

namespace HPD.Agent.Evaluations.Tests.Infrastructure;

/// <summary>
/// Fluent builder for constructing TurnEvaluationContext instances in tests.
/// Provides sensible defaults so tests only specify what they care about.
/// </summary>
internal sealed class TestContextBuilder
{
    private string _agentName = "TestAgent";
    private string _sessionId = "sess-test";
    private string _branchId = "branch-test";
    private string _conversationId = "conv-test";
    private int _turnIndex = 0;
    private string _userInput = "What is the capital of France?";
    private string _outputText = "Paris.";
    private string? _reasoningText;
    private string? _groundTruth;
    private List<ToolCallRecord> _toolCalls = [];
    private int _iterationCount = 1;
    private TimeSpan _duration = TimeSpan.FromSeconds(2);
    private AgentStopKind _stopKind = AgentStopKind.Completed;
    private Dictionary<string, object> _attributes = [];
    private Dictionary<string, double> _metrics = [];

    public TestContextBuilder WithAgentName(string name) { _agentName = name; return this; }
    public TestContextBuilder WithSessionId(string id) { _sessionId = id; return this; }
    public TestContextBuilder WithBranchId(string id) { _branchId = id; return this; }
    public TestContextBuilder WithTurnIndex(int index) { _turnIndex = index; return this; }
    public TestContextBuilder WithUserInput(string input) { _userInput = input; return this; }
    public TestContextBuilder WithOutputText(string text) { _outputText = text; return this; }
    public TestContextBuilder WithReasoningText(string? text) { _reasoningText = text; return this; }
    public TestContextBuilder WithGroundTruth(string? gt) { _groundTruth = gt; return this; }
    public TestContextBuilder WithIterationCount(int count) { _iterationCount = count; return this; }
    public TestContextBuilder WithDuration(TimeSpan d) { _duration = d; return this; }
    public TestContextBuilder WithStopKind(AgentStopKind kind) { _stopKind = kind; return this; }

    public TestContextBuilder WithToolCall(string name, string callId = "call-1",
        string argsJson = "{}", string result = "ok", bool permissionDenied = false,
        TimeSpan? duration = null, string? toolkitName = null)
    {
        _toolCalls.Add(new ToolCallRecord(
            CallId: callId,
            Name: name,
            ToolkitName: toolkitName,
            ArgumentsJson: argsJson,
            Result: result,
            Duration: duration ?? TimeSpan.FromMilliseconds(50),
            WasPermissionDenied: permissionDenied));
        return this;
    }

    public TestContextBuilder WithAttribute(string key, object value) { _attributes[key] = value; return this; }
    public TestContextBuilder WithMetric(string key, double value) { _metrics[key] = value; return this; }

    public TurnEvaluationContext Build()
    {
        var response = new ChatResponse([new ChatMessage(ChatRole.Assistant, _outputText)]);

        var trace = new TurnTrace
        {
            MessageTurnId = "msg-" + _turnIndex,
            AgentName = _agentName,
            StartedAt = DateTimeOffset.UtcNow - _duration,
            Duration = _duration,
            Iterations = []
        };

        return new TurnEvaluationContext
        {
            AgentName = _agentName,
            SessionId = _sessionId,
            BranchId = _branchId,
            ConversationId = _conversationId,
            TurnIndex = _turnIndex,
            UserInput = _userInput,
            ConversationHistory = [],
            OutputText = _outputText,
            FinalResponse = response,
            ReasoningText = _reasoningText,
            ToolCalls = _toolCalls,
            Trace = trace,
            IterationCount = _iterationCount,
            Duration = _duration,
            StopKind = _stopKind,
            GroundTruth = _groundTruth,
            Attributes = _attributes,
            Metrics = _metrics,
        };
    }

    /// <summary>
    /// Wraps the built context in a TurnEvaluationContextWrapper for use as additionalContext.
    /// </summary>
    public IEnumerable<EvaluationContext> BuildAsAdditionalContext()
        => [new TurnEvaluationContextWrapper(Build())];
}

/// <summary>
/// Static factory shortcuts for common test contexts.
/// </summary>
internal static class TestContext
{
    /// <summary>A simple completed turn with no tool calls.</summary>
    public static TurnEvaluationContext Simple(string output = "Paris.") =>
        new TestContextBuilder().WithOutputText(output).Build();

    /// <summary>A turn that called one tool.</summary>
    public static TurnEvaluationContext WithTool(string toolName, string argsJson = "{}",
        string result = "ok") =>
        new TestContextBuilder()
            .WithToolCall(toolName, argsJson: argsJson, result: result)
            .Build();

    /// <summary>A context wrapped for use as additionalContext in evaluator calls.</summary>
    public static IEnumerable<EvaluationContext> AsAdditionalContext(TurnEvaluationContext ctx) =>
        [new TurnEvaluationContextWrapper(ctx)];

    public static IEnumerable<EvaluationContext> AsAdditionalContext(TestContextBuilder builder) =>
        builder.BuildAsAdditionalContext();
}
