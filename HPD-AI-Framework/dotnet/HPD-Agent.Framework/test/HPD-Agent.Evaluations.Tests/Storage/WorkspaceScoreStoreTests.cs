// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: AGPL-3.0-only

using FluentAssertions;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using HPD.Agent.Evaluations.Storage;

namespace HPD.Agent.Evaluations.Tests.Storage;

public sealed class WorkspaceScoreStoreTests
{
    [Fact]
    public async Task WriteScoreAsync_PersistsScoreRecordInWorkspace()
    {
        var workspace = new InMemoryWorkspaceStore();
        var store = new WorkspaceScoreStore(workspace);
        var record = MakeBoolRecord("Safety", passed: true, sessionId: "session-1");

        await store.WriteScoreAsync(record);

        var persistedStore = new WorkspaceScoreStore(workspace);
        var scores = await persistedStore.GetScoresAsync(sessionId: "session-1").ToListAsync();

        var roundTripped = scores.Should().ContainSingle().Which;
        roundTripped.Id.Should().Be(record.Id);
        roundTripped.EvaluatorName.Should().Be("Safety");
        roundTripped.Result.Metrics.Should().ContainKey("Test");

        var space = await workspace.FindSpaceAsync(
            WorkspacePrincipalRef.System,
            new WorkspaceSpaceQuery
            {
                Kind = WorkspaceScoreStore.EvaluationResultsKind,
                ExternalId = WorkspaceScoreStore.EvaluationResultsExternalId
            });
        space.Should().NotBeNull();

        var attachments = await workspace.ListContentAsync(
            WorkspacePrincipalRef.System,
            space!.Id,
            new WorkspaceContentAttachmentQuery { Role = WorkspaceScoreStore.ScoreRecordRole });
        var attachment = attachments.Should().ContainSingle().Subject;
        attachment.ContentVersion.Should().NotBeNullOrWhiteSpace();
        attachment.Metadata.Should().ContainKey("document_type").WhoseValue.Should().Be("score_record");
        attachment.Metadata.Should().ContainKey("score_id").WhoseValue.Should().Be(record.Id);
        attachment.Metadata.Should().ContainKey("evaluator").WhoseValue.Should().Be("Safety");
        attachment.Metadata.Should().ContainKey("session_id").WhoseValue.Should().Be("session-1");
        attachment.Metadata.Should().ContainKey("branch_id").WhoseValue.Should().Be("main");
    }

    [Fact]
    public async Task Analytics_HydrateFromWorkspaceScoreDocuments()
    {
        var workspace = new InMemoryWorkspaceStore();
        var store = new WorkspaceScoreStore(workspace);
        await store.WriteScoreAsync(MakeNumericRecord("Quality", 0.25, agentName: "agent-a"));
        await store.WriteScoreAsync(MakeNumericRecord("Quality", 0.75, agentName: "agent-a"));
        await store.WriteScoreAsync(MakeNumericRecord("Quality", 1.0, agentName: "agent-b"));

        var persistedStore = new WorkspaceScoreStore(workspace);
        var passRate = await persistedStore.GetPassRateAsync("Quality");
        var comparison = await persistedStore.GetAgentComparisonAsync("Quality", ["agent-a", "agent-b"]);

        passRate.Should().BeApproximately(2.0 / 3.0, 0.0001);
        comparison["agent-a"].Average.Should().Be(0.5);
        comparison["agent-b"].Average.Should().Be(1.0);
    }

    [Fact]
    public async Task WriteRunAsync_PersistsRunRecordAndSupportsMsResultSurface()
    {
        var workspace = new InMemoryWorkspaceStore();
        var store = new WorkspaceScoreStore(workspace);
        var scenario = new ScenarioRunResult(
            scenarioName: "case-1",
            iterationName: "1",
            executionName: "exec",
            creationTime: new DateTime(2026, 2, 20, 12, 0, 0, DateTimeKind.Utc),
            messages: [new ChatMessage(ChatRole.User, "hello")],
            modelResponse: new ChatResponse([new ChatMessage(ChatRole.Assistant, "response")]),
            evaluationResult: new EvaluationResult(
                new BooleanMetric("Pass") { Value = true },
                new NumericMetric("Quality") { Value = 0.75 }),
            tags: ["workspace"]);

        await store.WriteResultsAsync([scenario]);

        var persistedStore = new WorkspaceScoreStore(workspace);
        var runs = await persistedStore.GetRunsAsync("exec", "case-1", "1").ToListAsync();
        var run = runs.Should().ContainSingle().Which;
        run.ModelResponse.Text.Should().Be("response");
        run.EvaluationResult.Metrics.Should().ContainKeys("Pass", "Quality");

        var exported = await persistedStore.ReadResultsAsync("exec", "case-1", "1").ToListAsync();
        exported.Should().ContainSingle().Which.Tags.Should().Contain("workspace");

        var space = await workspace.FindSpaceAsync(
            WorkspacePrincipalRef.System,
            new WorkspaceSpaceQuery
            {
                Kind = WorkspaceScoreStore.EvaluationResultsKind,
                ExternalId = WorkspaceScoreStore.EvaluationResultsExternalId
            });
        space.Should().NotBeNull();
        var attachments = await workspace.ListContentAsync(
            WorkspacePrincipalRef.System,
            space!.Id,
            new WorkspaceContentAttachmentQuery { Role = WorkspaceScoreStore.RunRecordRole });
        var attachment = attachments.Should().ContainSingle().Subject;
        attachment.ContentVersion.Should().NotBeNullOrWhiteSpace();
        attachment.Metadata.Should().ContainKey("document_type").WhoseValue.Should().Be("eval_run_record");
        attachment.Metadata.Should().ContainKey("execution_name").WhoseValue.Should().Be("exec");
        attachment.Metadata.Should().ContainKey("scenario_name").WhoseValue.Should().Be("case-1");
        attachment.Metadata.Should().ContainKey("iteration_name").WhoseValue.Should().Be("1");
    }

    [Fact]
    public async Task DeleteRunsAsync_RemovesMatchingWorkspaceRunAttachments()
    {
        var workspace = new InMemoryWorkspaceStore();
        var store = new WorkspaceScoreStore(workspace);
        await store.WriteRunAsync(MakeRunRecord("exec", "case-a", "1"));
        await store.WriteRunAsync(MakeRunRecord("exec", "case-a", "2"));
        await store.WriteRunAsync(MakeRunRecord("exec", "case-b", "1"));

        await store.DeleteRunsAsync("exec", "case-a", "1");

        var remaining = await new WorkspaceScoreStore(workspace).GetRunsAsync("exec").ToListAsync();
        remaining.Should().HaveCount(2);
        remaining.Should().NotContain(r => r.ScenarioName == "case-a" && r.IterationName == "1");
    }

    private static ScoreRecord MakeBoolRecord(
        string evaluatorName,
        bool passed,
        string sessionId = "sess-1") =>
        new()
        {
            Id = Guid.NewGuid().ToString("N"),
            EvaluatorName = evaluatorName,
            EvaluatorVersion = "1.0.0",
            Result = new EvaluationResult(new BooleanMetric("Test") { Value = passed }),
            Source = EvaluationSource.Test,
            SessionId = sessionId,
            BranchId = "main",
            TurnIndex = 0,
            AgentName = "TestAgent",
            Policy = EvalPolicy.MustAlwaysPass,
            CreatedAt = DateTimeOffset.UtcNow,
            SamplingRate = 1.0,
        };

    private static ScoreRecord MakeNumericRecord(
        string evaluatorName,
        double score,
        string agentName) =>
        new()
        {
            Id = Guid.NewGuid().ToString("N"),
            EvaluatorName = evaluatorName,
            EvaluatorVersion = "1.0.0",
            Result = new EvaluationResult(new NumericMetric("Score") { Value = score }),
            Source = EvaluationSource.Test,
            SessionId = "sess-1",
            BranchId = "main",
            TurnIndex = 0,
            AgentName = agentName,
            Policy = EvalPolicy.TrackTrend,
            CreatedAt = DateTimeOffset.UtcNow,
            SamplingRate = 1.0,
        };

    private static EvaluationRunRecord MakeRunRecord(
        string executionName,
        string scenarioName,
        string iterationName) =>
        new()
        {
            Id = Guid.NewGuid().ToString("N"),
            ExecutionName = executionName,
            ScenarioName = scenarioName,
            IterationName = iterationName,
            CreatedAt = DateTimeOffset.UtcNow,
            Messages = [new ChatMessage(ChatRole.User, "hello")],
            ModelResponse = new ChatResponse([new ChatMessage(ChatRole.Assistant, "response")]),
            EvaluationResult = new EvaluationResult(new BooleanMetric("Pass") { Value = true }),
            Tags = ["unit-test"],
            Source = EvaluationSource.Test,
            AgentName = "TestAgent",
            SessionId = executionName,
            BranchId = scenarioName,
            TurnIndex = 0,
        };
}

file static class AsyncEnumerableExtensions
{
    public static async Task<List<T>> ToListAsync<T>(this IAsyncEnumerable<T> source)
    {
        var list = new List<T>();
        await foreach (var item in source)
            list.Add(item);
        return list;
    }
}
