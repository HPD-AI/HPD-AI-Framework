using Microsoft.Extensions.AI;

namespace HPD.Agent.Tests.Workspace;

public sealed class WorkspaceSessionRepositoryTests
{
    [Fact]
    public async Task SaveSession_CreatesSessionSpaceAndMetadataDocument()
    {
        var workspace = new InMemoryWorkspaceStore();
        var repository = new WorkspaceSessionRepository(workspace);
        var session = new HPD.Agent.Session("session-1");
        session.AddMetadata("topic", "workspace-store");

        await repository.SaveSessionAsync(session);

        var space = await workspace.FindSpaceAsync(
            WorkspacePrincipalRef.System,
            new WorkspaceSpaceQuery
            {
                Kind = WorkspaceSessionRepository.SessionKind,
                ExternalId = "session-1"
            });
        Assert.NotNull(space);

        var metadataDocuments = await workspace.ListContentAsync(
            WorkspacePrincipalRef.System,
            space.Id,
            new WorkspaceContentAttachmentQuery { Role = WorkspaceSessionRepository.SessionMetadataRole });
        var metadataDocument = Assert.Single(metadataDocuments);
        Assert.Equal("session.json", metadataDocument.Name);

        var loaded = await repository.LoadSessionAsync("session-1");
        Assert.NotNull(loaded);
        Assert.Equal("session-1", loaded.Id);
        Assert.True(loaded.Metadata.ContainsKey("topic"));
    }

    [Fact]
    public async Task SaveSession_ReplacesMetadataDocumentForSameSessionRole()
    {
        var workspace = new InMemoryWorkspaceStore();
        var repository = new WorkspaceSessionRepository(workspace);
        var session = new HPD.Agent.Session("session-1");

        await repository.SaveSessionAsync(session);
        session.AddMetadata("updated", true);
        await repository.SaveSessionAsync(session);

        var space = await workspace.FindSpaceAsync(
            WorkspacePrincipalRef.System,
            new WorkspaceSpaceQuery
            {
                Kind = WorkspaceSessionRepository.SessionKind,
                ExternalId = "session-1"
            });
        Assert.NotNull(space);

        var metadataDocuments = await workspace.ListContentAsync(
            WorkspacePrincipalRef.System,
            space.Id,
            new WorkspaceContentAttachmentQuery { Role = WorkspaceSessionRepository.SessionMetadataRole });
        Assert.Single(metadataDocuments);

        var loaded = await repository.LoadSessionAsync("session-1");
        Assert.NotNull(loaded);
        Assert.True(loaded.Metadata.ContainsKey("updated"));
    }

    [Fact]
    public async Task LoadSession_NormalizesMetadataJsonElementsToClrValues()
    {
        var repository = new WorkspaceSessionRepository(new InMemoryWorkspaceStore());
        var session = new HPD.Agent.Session("session-1");
        session.AddMetadata("platformKey", "slack:C123:111.000");
        session.AddMetadata("platformKeyAliases", new[] { "slack:C123:thread" });
        session.AddMetadata("channelContext", new Dictionary<string, string>
        {
            ["teamId"] = "team-1",
            ["channelId"] = "channel-1"
        });

        await repository.SaveSessionAsync(session);

        var loaded = await repository.LoadSessionAsync(session.Id);

        Assert.NotNull(loaded);
        Assert.IsType<string>(loaded.Metadata["platformKey"]);
        Assert.Equal("slack:C123:111.000", loaded.Metadata["platformKey"]);

        var aliases = Assert.IsAssignableFrom<IEnumerable<string>>(loaded.Metadata["platformKeyAliases"]);
        Assert.Equal(["slack:C123:thread"], aliases);

        var channelContext = Assert.IsAssignableFrom<IReadOnlyDictionary<string, object>>(loaded.Metadata["channelContext"]);
        Assert.Equal("team-1", channelContext["teamId"]);
        Assert.Equal("channel-1", channelContext["channelId"]);
    }

    [Fact]
    public async Task SaveBranchDocument_CreatesBranchChildSpaceAndProjectsBranch()
    {
        var workspace = new InMemoryWorkspaceStore();
        var repository = new WorkspaceSessionRepository(workspace);
        var session = new HPD.Agent.Session("session-1");
        var branch = session.CreateBranch("main");
        branch.Name = "main";

        await repository.SaveSessionAsync(session);
        await repository.SaveBranchDocumentAsync(
            BranchEventDocumentBuilder.FromBranchSnapshot(session.Id, branch));

        var sessionSpace = await workspace.FindSpaceAsync(
            WorkspacePrincipalRef.System,
            new WorkspaceSpaceQuery
            {
                Kind = WorkspaceSessionRepository.SessionKind,
                ExternalId = "session-1"
            });
        Assert.NotNull(sessionSpace);

        var branchSpaces = await workspace.ListChildSpacesAsync(
            WorkspacePrincipalRef.System,
            sessionSpace.Id,
            new WorkspaceSpaceQuery { Kind = WorkspaceSessionRepository.BranchKind });
        var branchSpace = Assert.Single(branchSpaces);
        Assert.Equal("main", branchSpace.ExternalId);

        var streamDocuments = await workspace.ListContentAsync(
            WorkspacePrincipalRef.System,
            branchSpace.Id,
            new WorkspaceContentAttachmentQuery { Role = WorkspaceSessionRepository.BranchEventStreamRole });
        Assert.Single(streamDocuments);

        var loaded = await repository.LoadBranchAsync(session.Id, "main");
        Assert.NotNull(loaded);
        Assert.Equal("main", loaded.Id);
        Assert.NotNull(loaded.Session);
        Assert.Equal(session.Id, loaded.Session.Id);
    }

    [Fact]
    public async Task AppendBranchEvent_AssignsSequencesAndRejectsStaleExpectedSequence()
    {
        var repository = new WorkspaceSessionRepository(new InMemoryWorkspaceStore());
        var session = new HPD.Agent.Session("session-1");
        await repository.SaveSessionAsync(session);

        var firstMessage = new ChatMessage(ChatRole.User, "hello") { MessageId = "msg-1" };

        await repository.AppendBranchEventAsync(
            session.Id,
            "main",
            BranchEventFactory.MessageStarted(session.Id, "main", firstMessage),
            expectedSequenceNumber: 0);

        var staleAppend = repository.AppendBranchEventAsync(
            session.Id,
            "main",
            BranchEventFactory.MessageCompleted(session.Id, "main", "msg-1"),
            expectedSequenceNumber: 0);
        await Assert.ThrowsAsync<WorkspaceConflictException>(() => staleAppend);

        await repository.AppendBranchEventAsync(
            session.Id,
            "main",
            BranchEventFactory.MessageCompleted(session.Id, "main", "msg-1"),
            expectedSequenceNumber: 1);

        var events = new List<AgentEvent>();
        await foreach (var evt in repository.ReadBranchEventsAsync(
            session.Id,
            "main",
            HPD.Events.ReplayReadOptions.All))
        {
            events.Add(evt);
        }

        Assert.Equal([1, 2], events.Select(evt => evt.SequenceNumber));
        Assert.All(events, evt =>
        {
            Assert.Equal(session.Id, evt.SessionId);
            Assert.Equal("main", evt.BranchId);
        });
    }

    [Fact]
    public async Task ListIds_ReturnsSessionAndBranchExternalIds()
    {
        var repository = new WorkspaceSessionRepository(new InMemoryWorkspaceStore());
        var session = new HPD.Agent.Session("session-1");

        await repository.SaveSessionAsync(session);
        await repository.AppendBranchEventAsync(
            session.Id,
            "main",
            BranchEventFactory.BranchCreated(session.CreateBranch("main")));
        await repository.AppendBranchEventAsync(
            session.Id,
            "alternate",
            BranchEventFactory.BranchCreated(session.CreateBranch("alternate")));

        var sessionIds = await repository.ListSessionIdsAsync();
        var branchIds = await repository.ListBranchIdsAsync(session.Id);

        Assert.Equal(["session-1"], sessionIds);
        Assert.Equal(["alternate", "main"], branchIds);
    }

    [Fact]
    public async Task DeleteBranch_RemovesBranchChildSpaceWithoutDeletingSession()
    {
        var repository = new WorkspaceSessionRepository(new InMemoryWorkspaceStore());
        var session = new HPD.Agent.Session("session-1");

        await repository.SaveSessionAsync(session);
        await repository.AppendBranchEventAsync(
            session.Id,
            "main",
            BranchEventFactory.BranchCreated(session.CreateBranch("main")));

        await repository.DeleteBranchAsync(session.Id, "main");

        Assert.NotNull(await repository.LoadSessionAsync(session.Id));
        Assert.Null(await repository.LoadBranchAsync(session.Id, "main"));
        Assert.Empty(await repository.ListBranchIdsAsync(session.Id));
    }

    [Fact]
    public async Task DeleteSession_RemovesSessionAndBranchSpaces()
    {
        var repository = new WorkspaceSessionRepository(new InMemoryWorkspaceStore());
        var session = new HPD.Agent.Session("session-1");

        await repository.SaveSessionAsync(session);
        await repository.AppendBranchEventAsync(
            session.Id,
            "main",
            BranchEventFactory.BranchCreated(session.CreateBranch("main")));

        await repository.DeleteSessionAsync(session.Id);

        Assert.Null(await repository.LoadSessionAsync(session.Id));
        Assert.Empty(await repository.ListSessionIdsAsync());
        Assert.Empty(await repository.ListBranchIdsAsync(session.Id));
    }

}
