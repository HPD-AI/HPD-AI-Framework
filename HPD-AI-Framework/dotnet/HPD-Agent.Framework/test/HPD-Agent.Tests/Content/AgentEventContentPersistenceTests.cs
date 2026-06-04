using System.Text.Json.Serialization;
using HPD.Agent;
using HPD.Agent.Serialization;

namespace HPD.Agent.Tests.Content;

public class AgentEventContentPersistenceTests
{
    static AgentEventContentPersistenceTests()
    {
        AgentEventSerializer.RegisterEventType(
            typeof(PersistableContentTestEvent),
            "PERSISTABLE_CONTENT_TEST",
            AgentEventContentPersistenceTestJsonContext.Default.PersistableContentTestEvent);
    }

    [Fact]
    public async Task PersistAsync_WhenEventRequestsContentPersistence_WritesWorkspaceAttachment()
    {
        var workspace = new InMemoryWorkspaceStore();
        var evt = new PersistableContentTestEvent("hello")
        {
            EventId = "event-1",
            SessionId = "session-1",
            BranchId = "branch-1",
            TraceId = "trace-1",
            SpanId = "span-1",
            Metadata = new AgentMetadata
            {
                AgentName = "TestAgent",
                AgentId = "agent-1"
            }
        };

        var info = await AgentEventContentPersistence.PersistAsync(
            workspace,
            evt,
            "default-scope");

        Assert.NotNull(info);
        Assert.Equal("event-1.json", info.Name);
        Assert.Equal("application/json", info.ContentType);
        Assert.Equal(ContentSource.Agent.ToString(), info.Metadata?["origin"]);
        Assert.Equal("PERSISTABLE_CONTENT_TEST", info.Metadata?["event.type"]);
        Assert.Equal("event-1", info.Metadata?["event.id"]);
        Assert.Equal("session-1", info.Metadata?["session"]);
        Assert.Equal("branch-1", info.Metadata?["branch"]);
        Assert.Equal("trace-1", info.Metadata?["trace"]);
        Assert.Equal("span-1", info.Metadata?["span"]);
        Assert.Equal("TestAgent", info.Metadata?["agent.name"]);
        Assert.Equal("agent-1", info.Metadata?["agent.id"]);
        Assert.Equal("test", info.Metadata?["kind"]);

        var branchSpace = await ResolveBranchSpaceAsync(workspace);
        var attachments = await workspace.ListContentAsync(
            WorkspacePrincipalRef.System,
            branchSpace.Id,
            new WorkspaceContentAttachmentQuery { Role = WorkspaceContentRoles.Memory });
        var attachment = Assert.Single(attachments);
        Assert.Equal(info.Id, attachment.ContentId);
        Assert.Equal("/memory/events", attachment.PathHint);

        await using var stream = await workspace.OpenContentAsync(
            WorkspacePrincipalRef.System,
            info.Id,
            info.Version);
        Assert.NotNull(stream);
        using var reader = new StreamReader(stream);
        var json = await reader.ReadToEndAsync();

        Assert.Contains("\"type\":\"PERSISTABLE_CONTENT_TEST\"", json);
        Assert.Contains("\"value\":\"hello\"", json);
    }

    [Fact]
    public async Task PersistAsync_WhenEventDoesNotRequestContentPersistence_DoesNothing()
    {
        var workspace = new InMemoryWorkspaceStore();

        var info = await AgentEventContentPersistence.PersistAsync(
            workspace,
            new TextDeltaEvent("hello", "message-1"),
            "default-scope");

        Assert.Null(info);
        Assert.Empty(await workspace.ListSpacesAsync(WorkspacePrincipalRef.System));
    }

    private static async Task<WorkspaceSpaceInfo> ResolveBranchSpaceAsync(IWorkspaceStore workspace)
    {
        var sessionSpace = await workspace.FindSpaceAsync(
            WorkspacePrincipalRef.System,
            new WorkspaceSpaceQuery
            {
                Kind = WorkspaceSessionRepository.SessionKind,
                ExternalId = "session-1"
            });
        Assert.NotNull(sessionSpace);

        var branchSpace = await workspace.FindSpaceAsync(
            WorkspacePrincipalRef.System,
            new WorkspaceSpaceQuery
            {
                Kind = WorkspaceSessionRepository.BranchKind,
                ExternalId = "branch-1",
                ParentSpaceId = sessionSpace.Id
            });
        Assert.NotNull(branchSpace);
        return branchSpace;
    }
}

internal sealed record PersistableContentTestEvent(string Value) : AgentEvent
{
    public override ContentPersistenceRequest? GetContentPersistenceRequest() => new()
    {
        Role = WorkspaceContentRoles.Memory,
        PathHint = "/memory/events",
        Name = "event-1.json",
        Description = "Persisted test event",
        Origin = ContentSource.Agent,
        Tags = new Dictionary<string, string>
        {
            ["kind"] = "test"
        }
    };
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(PersistableContentTestEvent))]
internal partial class AgentEventContentPersistenceTestJsonContext : JsonSerializerContext;
