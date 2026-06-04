using System.Text;
using HPD.Agent;

namespace HPD.Agent.Tests.Workspace;

public abstract class IWorkspaceStoreContractTests
{
    protected abstract IWorkspaceStore CreateStore();

    private static WorkspacePrincipalRef Principal => WorkspacePrincipalRef.System;
    private static WorkspacePrincipalRef UserOne => new("user", "user-1");
    private static WorkspacePrincipalRef UserTwo => new("user", "user-2");

    [Fact]
    public async Task CreateChildSpace_BranchIsChildOfSessionSpace()
    {
        var store = CreateStore();
        var session = await store.CreateSpaceAsync(
            Principal,
            new CreateWorkspaceSpaceRequest
            {
                Kind = "session",
                ExternalId = "session-1",
                Name = "Session 1"
            });

        var branch = await store.CreateChildSpaceAsync(
            Principal,
            session.Id,
            new CreateWorkspaceSpaceRequest
            {
                Kind = "branch",
                ExternalId = "main",
                Name = "main"
            });

        Assert.Equal(session.Id, branch.ParentSpaceId);

        var children = await store.ListChildSpacesAsync(
            Principal,
            session.Id,
            new WorkspaceSpaceQuery { Kind = "branch" });

        var child = Assert.Single(children);
        Assert.Equal(branch.Id, child.Id);
        Assert.Equal("main", child.ExternalId);
    }

    [Fact]
    public async Task AttachContent_SameContentCanAppearInMultipleSpacesWithDifferentRoles()
    {
        var store = CreateStore();
        var session = await CreateSpaceAsync(store, "session", "session-1");
        var project = await CreateSpaceAsync(store, "project", "project-1");
        var upload = await WriteAttachmentTextAsync(
            store,
            session.Id,
            "shared source",
            "text/plain",
            "source.txt",
            "upload",
            WorkspacePermissions.Read);
        var sourceDoc = await store.AttachContentAsync(
            Principal,
            project.Id,
            upload.ContentId,
            new AttachWorkspaceContentRequest
            {
                Role = "source_doc",
                Name = "contract.txt",
                ContentVersion = upload.ContentVersion,
                Permission = "read_write"
            });

        Assert.Equal(upload.ContentId, sourceDoc.ContentId);
        Assert.NotEqual(upload.Id, sourceDoc.Id);

        var sessionContent = await store.ListContentAsync(
            Principal,
            session.Id,
            new WorkspaceContentAttachmentQuery { Role = "upload" });
        var projectContent = await store.ListContentAsync(
            Principal,
            project.Id,
            new WorkspaceContentAttachmentQuery { Role = "source_doc" });

        Assert.Single(sessionContent);
        Assert.Single(projectContent);
        Assert.Equal("source.txt", sessionContent[0].Name);
        Assert.Equal("contract.txt", projectContent[0].Name);
    }

    [Fact]
    public async Task AttachContent_ContentVersionRemainsReadableAfterContentUpdate()
    {
        var store = CreateStore();
        var session = await CreateSpaceAsync(store, "session", "session-1");
        var attachment = await WriteAttachmentTextAsync(
            store,
            session.Id,
            "v1",
            "text/plain",
            "draft.txt",
            "artifact",
            WorkspacePermissions.ReadWrite);
        var snapshot = await store.AttachContentAsync(
            Principal,
            session.Id,
            attachment.ContentId,
            new AttachWorkspaceContentRequest
            {
                Role = "snapshot",
                Name = "draft-v1.txt",
                ContentVersion = attachment.ContentVersion,
                Permission = "read"
            });

        var updated = await WriteAttachmentTextAsync(
            store,
            session.Id,
            "v2",
            "text/plain",
            "draft.txt",
            "artifact",
            WorkspacePermissions.ReadWrite,
            existingAttachmentId: attachment.Id,
            ifMatchContentVersion: attachment.ContentVersion,
            ifMatchAttachmentVersion: attachment.Version);

        Assert.NotEqual(attachment.ContentVersion, updated.ContentVersion);
        Assert.Equal(attachment.ContentVersion, snapshot.ContentVersion);

        await using var attachedVersion = await store.OpenContentAsync(
            Principal,
            snapshot.ContentId,
            snapshot.ContentVersion);
        Assert.NotNull(attachedVersion);
        Assert.Equal("v1", await new StreamReader(attachedVersion!).ReadToEndAsync());

        await using var latest = await store.OpenContentAsync(Principal, attachment.ContentId);
        Assert.NotNull(latest);
        Assert.Equal("v2", await new StreamReader(latest!).ReadToEndAsync());
    }

    [Fact]
    public async Task FrameworkDocuments_AreContentAttachmentsByRole()
    {
        var store = CreateStore();
        var agent = await CreateSpaceAsync(store, "agent", "agent-1");
        var session = await CreateSpaceAsync(store, "session", "session-1");

        var definition = await WriteAttachmentTextAsync(
            store,
            agent.Id,
            """{"id":"agent-1"}""",
            "application/json",
            "definition.json",
            "agent_definition",
            WorkspacePermissions.ReadWrite);
        var metadata = await WriteAttachmentTextAsync(
            store,
            session.Id,
            """{"id":"session-1"}""",
            "application/json",
            "session.json",
            "session_metadata",
            WorkspacePermissions.ReadWrite);

        var agentDocuments = await store.ListContentAsync(
            Principal,
            agent.Id,
            new WorkspaceContentAttachmentQuery { Role = "agent_definition" });
        var sessionDocuments = await store.ListContentAsync(
            Principal,
            session.Id,
            new WorkspaceContentAttachmentQuery { Role = "session_metadata" });

        Assert.Single(agentDocuments);
        Assert.Single(sessionDocuments);
        Assert.Equal(definition.ContentId, agentDocuments[0].ContentId);
        Assert.Equal(metadata.ContentId, sessionDocuments[0].ContentId);
    }

    [Fact]
    public async Task WriteContent_WithStaleVersion_ThrowsConflict()
    {
        var store = CreateStore();
        var space = await CreateSpaceAsync(store, "project", "project-1");
        var original = await WriteAttachmentTextAsync(store, space.Id, "v1", "text/plain", "draft.txt", "draft");
        var updated = await WriteAttachmentTextAsync(
            store,
            space.Id,
            "v2",
            "text/plain",
            "draft.txt",
            "draft",
            existingAttachmentId: original.Id,
            ifMatchContentVersion: original.ContentVersion,
            ifMatchAttachmentVersion: original.Version);

        var ex = await Assert.ThrowsAsync<WorkspaceConflictException>(() => WriteAttachmentTextAsync(
            store,
            space.Id,
            "v3",
            "text/plain",
            "draft.txt",
            "draft",
            existingAttachmentId: updated.Id,
            ifMatchContentVersion: original.ContentVersion,
            ifMatchAttachmentVersion: updated.Version));

        Assert.Equal(original.ContentVersion, ex.ExpectedVersion);
        Assert.Equal(updated.ContentVersion, ex.ActualVersion);
    }

    [Fact]
    public async Task DetachContent_WithStaleAttachmentVersion_ThrowsConflict()
    {
        var store = CreateStore();
        var space = await CreateSpaceAsync(store, "session", "session-1");
        var attachment = await WriteAttachmentTextAsync(
            store,
            space.Id,
            "upload",
            "text/plain",
            "upload.txt",
            "upload");

        await Assert.ThrowsAsync<WorkspaceConflictException>(() => store.DetachContentAsync(
            Principal,
            space.Id,
            attachment.Id,
            ifMatchVersion: "attachment:stale"));

        var stillAttached = await store.ListContentAsync(Principal, space.Id);
        Assert.Single(stillAttached);
    }

    [Fact]
    public async Task AppendEvent_AssignsSequenceAndRejectsStaleExpectedSequence()
    {
        var store = CreateStore();
        var session = await CreateSpaceAsync(store, "session", "session-1");
        var branch = await store.CreateChildSpaceAsync(
            Principal,
            session.Id,
            new CreateWorkspaceSpaceRequest
            {
                Kind = "branch",
                ExternalId = "main",
                Name = "main"
            });

        var first = await AppendEventAsync(store, branch.Id, "one", expectedSequenceNumber: 0);
        var second = await AppendEventAsync(store, branch.Id, "two", expectedSequenceNumber: 1);

        Assert.Equal(1, first.SequenceNumber);
        Assert.Equal(2, second.SequenceNumber);
        await Assert.ThrowsAsync<WorkspaceConflictException>(() =>
            AppendEventAsync(store, branch.Id, "three", expectedSequenceNumber: 1));

        var events = new List<WorkspaceEventRecord>();
        await foreach (var evt in store.ReadEventsAsync(
            Principal,
            branch.Id,
            new WorkspaceEventStreamQuery { Role = "branch_event_stream" }))
        {
            events.Add(evt);
        }

        Assert.Equal([1, 2], events.Select(e => e.SequenceNumber));
        Assert.Equal(["one", "two"], events.Select(e => Encoding.UTF8.GetString(e.Payload.ToArray())));
    }

    [Fact]
    public async Task AppendEvent_CreatesStableAttachmentAndAppendsToEventBackend()
    {
        var store = CreateStore();
        var session = await CreateSpaceAsync(store, "session", "session-1");
        var branch = await store.CreateChildSpaceAsync(
            Principal,
            session.Id,
            new CreateWorkspaceSpaceRequest
            {
                Kind = "branch",
                ExternalId = "main",
                Name = "main"
            });

        var first = await AppendEventAsync(store, branch.Id, "{\"event\":\"one\"}", expectedSequenceNumber: 0);
        var initialAttachments = await store.ListContentAsync(
            Principal,
            branch.Id,
            new WorkspaceContentAttachmentQuery { Role = "branch_event_stream", Name = "events.jsonl" });
        var initialAttachment = Assert.Single(initialAttachments);

        var second = await AppendEventAsync(store, branch.Id, "{\"event\":\"two\"}", expectedSequenceNumber: 1);

        Assert.Equal(first.EventStreamContentId, second.EventStreamContentId);

        var attachments = await store.ListContentAsync(
            Principal,
            branch.Id,
            new WorkspaceContentAttachmentQuery { Role = "branch_event_stream", Name = "events.jsonl" });
        var attachment = Assert.Single(attachments);
        Assert.Equal(first.EventStreamAttachmentId, attachment.Id);
        Assert.Equal(initialAttachment.Id, attachment.Id);
        Assert.Equal(second.EventStreamContentId, attachment.ContentId);
        Assert.Equal(initialAttachment.Version, attachment.Version);
        Assert.Equal(initialAttachment.ContentVersion, attachment.ContentVersion);

        var stat = await store.StatContentAsync(Principal, attachment.ContentId, attachment.ContentVersion);
        Assert.NotNull(stat);
        Assert.Equal("application/x-ndjson", stat.ContentType);
        Assert.Equal("events.jsonl", stat.Name);

        var events = new List<WorkspaceEventRecord>();
        await foreach (var evt in store.ReadEventsAsync(
            Principal,
            branch.Id,
            new WorkspaceEventStreamQuery { Role = "branch_event_stream" }))
        {
            events.Add(evt);
        }

        Assert.Equal(
            ["{\"event\":\"one\"}", "{\"event\":\"two\"}"],
            events.Select(evt => Encoding.UTF8.GetString(evt.Payload.ToArray())));
    }

    [Fact]
    public async Task DeleteSpace_RejectsNonRecursiveDeleteWhenSpaceHasChildren()
    {
        var store = CreateStore();
        var session = await CreateSpaceAsync(store, "session", "session-1");
        await store.CreateChildSpaceAsync(
            Principal,
            session.Id,
            new CreateWorkspaceSpaceRequest
            {
                Kind = "branch",
                ExternalId = "main",
                Name = "main"
            });

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            store.DeleteSpaceAsync(Principal, session.Id));

        var stillExists = await store.GetSpaceAsync(Principal, session.Id);
        Assert.NotNull(stillExists);
    }

    [Fact]
    public async Task DeleteSpace_RecursiveDeletesChildSpacesAttachmentsAndEventStreams()
    {
        var store = CreateStore();
        var session = await CreateSpaceAsync(store, "session", "session-1");
        var branch = await store.CreateChildSpaceAsync(
            Principal,
            session.Id,
            new CreateWorkspaceSpaceRequest
            {
                Kind = "branch",
                ExternalId = "main",
                Name = "main"
            });
        var content = await WriteAttachmentTextAsync(
            store,
            branch.Id,
            "upload",
            "text/plain",
            "upload.txt",
            "upload");
        await AppendEventAsync(store, branch.Id, "one", expectedSequenceNumber: 0);

        await store.DeleteSpaceAsync(Principal, session.Id, session.Version, recursive: true);

        Assert.Null(await store.GetSpaceAsync(Principal, session.Id));
        Assert.Null(await store.GetSpaceAsync(Principal, branch.Id));
        Assert.Empty(await store.ListContentAsync(Principal, branch.Id));
        Assert.NotNull(await store.StatContentAsync(Principal, content.ContentId));

        var events = new List<WorkspaceEventRecord>();
        await foreach (var evt in store.ReadEventsAsync(
            Principal,
            branch.Id,
            new WorkspaceEventStreamQuery { Role = "branch_event_stream" }))
        {
            events.Add(evt);
        }

        Assert.Empty(events);
    }

    [Fact]
    public async Task Access_UnsharedSpaceIsHiddenFromOtherPrincipals()
    {
        var store = CreateStore();
        var space = await store.CreateSpaceAsync(
            UserOne,
            new CreateWorkspaceSpaceRequest
            {
                Kind = "project",
                ExternalId = "project-1",
                Name = "Project 1"
            });

        Assert.NotNull(await store.GetSpaceAsync(UserOne, space.Id));
        Assert.Null(await store.GetSpaceAsync(UserTwo, space.Id));
        Assert.Empty(await store.ListSpacesAsync(UserTwo));

        var access = Assert.Single(await store.ListAccessAsync(UserOne, space.Id));
        Assert.Equal(UserOne, access.Principal);
        Assert.Equal(WorkspacePermissions.Owner, access.Permission);
    }

    [Fact]
    public async Task Access_GrantReadAllowsListingAndReadingAttachedContentOnly()
    {
        var store = CreateStore();
        var space = await CreateSpaceAsync(store, "project", "project-1");
        var attachment = await WriteAttachmentTextAsync(
            store,
            space.Id,
            "v1",
            "text/plain",
            "source.txt",
            "source_doc",
            WorkspacePermissions.Read);

        await store.GrantAccessAsync(
            Principal,
            space.Id,
            new GrantWorkspaceSpaceAccessRequest
            {
                Grantee = UserOne,
                Permission = WorkspacePermissions.Read,
                Role = "reader"
            });

        Assert.NotNull(await store.GetSpaceAsync(UserOne, space.Id));
        Assert.Single(await store.ListContentAsync(UserOne, space.Id));

        await using var stream = await store.OpenContentAsync(UserOne, attachment.ContentId, attachment.ContentVersion);
        Assert.NotNull(stream);
        Assert.Equal("v1", await new StreamReader(stream!).ReadToEndAsync());

        await Assert.ThrowsAsync<WorkspaceAccessDeniedException>(() => store.AttachContentAsync(
            UserOne,
            space.Id,
            attachment.ContentId,
            new AttachWorkspaceContentRequest
            {
                Role = "draft",
                Name = "draft.txt"
            }));
    }

    [Fact]
    public async Task Access_GrantWriteAllowsAttachingAndReplacingVisibleContent()
    {
        var store = CreateStore();
        var space = await CreateSpaceAsync(store, "project", "project-1");
        var content = await WriteAttachmentTextAsync(
            store,
            space.Id,
            "v1",
            "text/plain",
            "draft.txt",
            "draft",
            WorkspacePermissions.ReadWrite);
        await store.GrantAccessAsync(
            Principal,
            space.Id,
            new GrantWorkspaceSpaceAccessRequest
            {
                Grantee = UserOne,
                Permission = WorkspacePermissions.ReadWrite,
                Role = "collaborator"
            });

        var updated = await WriteAttachmentTextAsync(
            store,
            space.Id,
            "v2",
            "text/plain",
            "draft.txt",
            "draft",
            WorkspacePermissions.ReadWrite,
            existingAttachmentId: content.Id,
            ifMatchContentVersion: content.ContentVersion,
            ifMatchAttachmentVersion: content.Version,
            principal: UserOne);
        Assert.NotEqual(content.ContentVersion, updated.ContentVersion);

        var attachment = await WriteAttachmentTextAsync(
            store,
            space.Id,
            "note",
            "text/plain",
            "note.txt",
            "draft",
            WorkspacePermissions.ReadWrite,
            principal: UserOne);

        Assert.NotEqual(content.ContentId, attachment.ContentId);
    }

    [Fact]
    public async Task Access_RevokeRemovesVisibility()
    {
        var store = CreateStore();
        var space = await CreateSpaceAsync(store, "project", "project-1");
        await store.GrantAccessAsync(
            Principal,
            space.Id,
            new GrantWorkspaceSpaceAccessRequest
            {
                Grantee = UserOne,
                Permission = WorkspacePermissions.Read
            });

        Assert.NotNull(await store.GetSpaceAsync(UserOne, space.Id));

        await store.RevokeAccessAsync(Principal, space.Id, UserOne);

        Assert.Null(await store.GetSpaceAsync(UserOne, space.Id));
        var access = await store.ListAccessAsync(Principal, space.Id);
        Assert.Contains(access, grant => grant.Principal == UserOne && grant.RevokedAt is not null);
    }

    [Fact]
    public async Task Access_ChildSpaceInheritsParentGrants()
    {
        var store = CreateStore();
        var session = await CreateSpaceAsync(store, "session", "session-1");
        await store.GrantAccessAsync(
            Principal,
            session.Id,
            new GrantWorkspaceSpaceAccessRequest
            {
                Grantee = UserOne,
                Permission = WorkspacePermissions.Read
            });

        var branch = await store.CreateChildSpaceAsync(
            Principal,
            session.Id,
            new CreateWorkspaceSpaceRequest
            {
                Kind = "branch",
                ExternalId = "main",
                Name = "main"
            });

        Assert.NotNull(await store.GetSpaceAsync(UserOne, branch.Id));
        var children = await store.ListChildSpacesAsync(UserOne, session.Id, new WorkspaceSpaceQuery { Kind = "branch" });
        Assert.Single(children);
    }

    [Fact]
    public async Task Access_AttachmentVersionBoundsReadableContentVersion()
    {
        var store = CreateStore();
        var space = await CreateSpaceAsync(store, "project", "project-1");
        var original = await WriteAttachmentTextAsync(
            store,
            space.Id,
            "v1",
            "text/plain",
            "draft.txt",
            "draft",
            WorkspacePermissions.Read);
        var writerAttachment = await store.AttachContentAsync(
            Principal,
            space.Id,
            original.ContentId,
            new AttachWorkspaceContentRequest
            {
                Role = "draft_writer",
                Name = "draft-writer.txt",
                ContentVersion = original.ContentVersion,
                Permission = WorkspacePermissions.ReadWrite
            });
        var updated = await WriteAttachmentTextAsync(
            store,
            space.Id,
            "v2",
            "text/plain",
            "draft-writer.txt",
            "draft_writer",
            WorkspacePermissions.ReadWrite,
            existingAttachmentId: writerAttachment.Id,
            ifMatchContentVersion: writerAttachment.ContentVersion,
            ifMatchAttachmentVersion: writerAttachment.Version);
        await store.DetachContentAsync(Principal, space.Id, updated.Id, updated.Version);
        await store.GrantAccessAsync(
            Principal,
            space.Id,
            new GrantWorkspaceSpaceAccessRequest
            {
                Grantee = UserOne,
                Permission = WorkspacePermissions.Read
            });

        await using var originalStream = await store.OpenContentAsync(UserOne, original.ContentId, original.ContentVersion);
        Assert.NotNull(originalStream);
        Assert.Equal("v1", await new StreamReader(originalStream!).ReadToEndAsync());

        Assert.Null(await store.OpenContentAsync(UserOne, updated.ContentId, updated.ContentVersion));
        Assert.Null(await store.OpenContentAsync(UserOne, updated.ContentId));
    }

    [Fact]
    public async Task Policy_BranchUploadsAreSystemManagedForNonSystemPrincipals()
    {
        var store = CreateStore();
        var session = await CreateSpaceAsync(store, "session", "session-1");
        var branch = await store.CreateChildSpaceAsync(
            Principal,
            session.Id,
            new CreateWorkspaceSpaceRequest { Kind = "branch", ExternalId = "main", Name = "main" });
        var userProject = await store.CreateSpaceAsync(
            UserOne,
            new CreateWorkspaceSpaceRequest { Kind = "project", ExternalId = "user-project", Name = "User Project" });
        var upload = await WriteAttachmentTextAsync(
            store,
            userProject.Id,
            "upload",
            "text/plain",
            "upload.txt",
            "content",
            principal: UserOne);
        await store.GrantAccessAsync(
            Principal,
            branch.Id,
            new GrantWorkspaceSpaceAccessRequest
            {
                Grantee = UserOne,
                Permission = WorkspacePermissions.ReadWrite
            });

        await Assert.ThrowsAsync<WorkspaceAccessDeniedException>(() => store.AttachContentAsync(
            UserOne,
            branch.Id,
            upload.ContentId,
            new AttachWorkspaceContentRequest
            {
                Role = "upload",
                Name = "upload.txt"
            }));

        var artifact = await store.AttachContentAsync(
            UserOne,
            branch.Id,
            upload.ContentId,
            new AttachWorkspaceContentRequest
            {
                Role = "artifact",
                Name = "draft.txt",
                Permission = WorkspacePermissions.ReadWrite
            });
        Assert.Equal("artifact", artifact.Role);

        var systemUpload = await store.AttachContentAsync(
            Principal,
            branch.Id,
            upload.ContentId,
            new AttachWorkspaceContentRequest
            {
                Role = "upload",
                Name = "upload.txt"
            });

        await Assert.ThrowsAsync<WorkspaceAccessDeniedException>(() => store.DetachContentAsync(
            UserOne,
            branch.Id,
            systemUpload.Id));
    }

    [Fact]
    public async Task Policy_SkillSpacesAreReadOnlyForNonSystemPrincipals()
    {
        var store = CreateStore();
        var skill = await CreateSpaceAsync(store, "skill", "skill-1");
        var userProject = await store.CreateSpaceAsync(
            UserOne,
            new CreateWorkspaceSpaceRequest { Kind = "project", ExternalId = "user-skill-project", Name = "User Skill Project" });
        var instructions = await WriteAttachmentTextAsync(
            store,
            userProject.Id,
            "instructions",
            "text/plain",
            "instructions.md",
            "content",
            principal: UserOne);
        await store.GrantAccessAsync(
            Principal,
            skill.Id,
            new GrantWorkspaceSpaceAccessRequest
            {
                Grantee = UserOne,
                Permission = WorkspacePermissions.ReadWrite
            });

        await Assert.ThrowsAsync<WorkspaceAccessDeniedException>(() => store.AttachContentAsync(
            UserOne,
            skill.Id,
                instructions.ContentId,
            new AttachWorkspaceContentRequest
            {
                Role = "instruction",
                Name = "instructions.md",
                Permission = WorkspacePermissions.ReadWrite
            }));

        var attachment = await store.AttachContentAsync(
            Principal,
            skill.Id,
            instructions.ContentId,
            new AttachWorkspaceContentRequest
            {
                Role = "instruction",
                Name = "instructions.md",
                Permission = WorkspacePermissions.ReadWrite
            });

        Assert.Single(await store.ListContentAsync(UserOne, skill.Id));
        await Assert.ThrowsAsync<WorkspaceAccessDeniedException>(() => WriteAttachmentTextAsync(
            store,
            skill.Id,
            "updated",
            "text/plain",
            "instructions.md",
            "instruction",
            WorkspacePermissions.ReadWrite,
            existingAttachmentId: attachment.Id,
            ifMatchContentVersion: attachment.ContentVersion,
            ifMatchAttachmentVersion: attachment.Version,
            principal: UserOne));
        await Assert.ThrowsAsync<WorkspaceAccessDeniedException>(() => store.DetachContentAsync(
            UserOne,
            skill.Id,
            attachment.Id));
    }

    [Fact]
    public async Task Policy_MemoryDetachIsSystemManagedForNonSystemPrincipals()
    {
        var store = CreateStore();
        var memory = await CreateSpaceAsync(store, "memory", "memory-1");
        var attachment = await WriteAttachmentTextAsync(
            store,
            memory.Id,
            "remember this",
            "text/plain",
            "memory.md",
            "memory_note",
            WorkspacePermissions.ReadWrite);
        await store.GrantAccessAsync(
            Principal,
            memory.Id,
            new GrantWorkspaceSpaceAccessRequest
            {
                Grantee = UserOne,
                Permission = WorkspacePermissions.ReadWrite
            });

        await Assert.ThrowsAsync<WorkspaceAccessDeniedException>(() => store.DetachContentAsync(
            UserOne,
            memory.Id,
            attachment.Id));

        Assert.Single(await store.ListContentAsync(UserOne, memory.Id));
        await store.DetachContentAsync(Principal, memory.Id, attachment.Id);
        Assert.Empty(await store.ListContentAsync(Principal, memory.Id));
    }

    private static Task<WorkspaceSpaceInfo> CreateSpaceAsync(
        IWorkspaceStore store,
        string kind,
        string externalId) =>
        store.CreateSpaceAsync(
            Principal,
            new CreateWorkspaceSpaceRequest
            {
                Kind = kind,
                ExternalId = externalId,
                Name = externalId
            });

    private static Task<WorkspaceContentAttachmentInfo> WriteAttachmentTextAsync(
        IWorkspaceStore store,
        string spaceId,
        string text,
        string contentType,
        string name,
        string role,
        string permission = WorkspacePermissions.ReadWrite,
        string? existingAttachmentId = null,
        string? ifMatchContentVersion = null,
        string? ifMatchAttachmentVersion = null,
        WorkspacePrincipalRef? principal = null)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        return store.WriteContentAsync(
            principal ?? Principal,
            spaceId,
            existingAttachmentId,
            new MemoryStream(bytes),
            new WriteWorkspaceSpaceContentRequest
            {
                IfMatchContentVersion = ifMatchContentVersion,
                IfMatchAttachmentVersion = ifMatchAttachmentVersion,
                ContentType = contentType,
                Role = role,
                Name = name,
                Permission = permission
            });
    }

    private static Task<WorkspaceEventAppendResult> AppendEventAsync(
        IWorkspaceStore store,
        string branchSpaceId,
        string payload,
        long expectedSequenceNumber) =>
        store.AppendEventAsync(
            Principal,
            branchSpaceId,
            new AppendWorkspaceEventRequest
            {
                Role = "branch_event_stream",
                Payload = Encoding.UTF8.GetBytes(payload),
                ExpectedSequenceNumber = expectedSequenceNumber
            });
}

public sealed class InMemoryWorkspaceStoreContractTests : IWorkspaceStoreContractTests
{
    protected override IWorkspaceStore CreateStore() => new InMemoryWorkspaceStore();
}

public sealed class JsonWorkspaceStoreContractTests : IWorkspaceStoreContractTests, IDisposable
{
    private readonly string _tempDir = Path.Combine(Path.GetTempPath(), $"hpd-json-workspace-contract-{Guid.NewGuid():N}");

    protected override IWorkspaceStore CreateStore() => new JsonWorkspaceStore(_tempDir);

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, recursive: true);
    }
}
