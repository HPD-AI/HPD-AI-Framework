namespace HPD.Agent;

public interface IContentSpacePolicy
{
    string Kind { get; }

    WorkspacePolicyDecision CanCreate(
        WorkspacePolicyContext context,
        CreateWorkspaceSpaceRequest request);

    WorkspacePolicyDecision CanAttachContent(
        WorkspacePolicyContext context,
        WorkspaceSpaceInfo space,
        WorkspaceContentInfo content,
        AttachWorkspaceContentRequest request);

    WorkspacePolicyDecision CanReadContent(
        WorkspacePolicyContext context,
        WorkspaceSpaceInfo space,
        WorkspaceContentAttachmentInfo attachment);

    WorkspacePolicyDecision CanWriteContent(
        WorkspacePolicyContext context,
        WorkspaceSpaceInfo space,
        WorkspaceContentAttachmentInfo attachment,
        WriteWorkspaceSpaceContentRequest request);

    WorkspacePolicyDecision CanDetachContent(
        WorkspacePolicyContext context,
        WorkspaceSpaceInfo space,
        WorkspaceContentAttachmentInfo attachment);

    WorkspacePolicyDecision CanAppendEvent(
        WorkspacePolicyContext context,
        WorkspaceSpaceInfo space,
        AppendWorkspaceEventRequest request);
}

public sealed record WorkspacePolicyContext(WorkspacePrincipalRef Principal)
{
    public bool IsSystem => Principal == WorkspacePrincipalRef.System;
}

public sealed record WorkspacePolicyDecision(bool Allowed, string? Reason = null)
{
    public static WorkspacePolicyDecision Allow { get; } = new(true);

    public static WorkspacePolicyDecision Deny(string reason) => new(false, reason);
}

public abstract class ContentSpacePolicyBase : IContentSpacePolicy
{
    protected ContentSpacePolicyBase(string kind)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(kind);
        Kind = kind;
    }

    public string Kind { get; }

    public virtual WorkspacePolicyDecision CanCreate(
        WorkspacePolicyContext context,
        CreateWorkspaceSpaceRequest request) =>
        WorkspacePolicyDecision.Allow;

    public virtual WorkspacePolicyDecision CanAttachContent(
        WorkspacePolicyContext context,
        WorkspaceSpaceInfo space,
        WorkspaceContentInfo content,
        AttachWorkspaceContentRequest request) =>
        WorkspacePolicyDecision.Allow;

    public virtual WorkspacePolicyDecision CanReadContent(
        WorkspacePolicyContext context,
        WorkspaceSpaceInfo space,
        WorkspaceContentAttachmentInfo attachment) =>
        WorkspacePolicyDecision.Allow;

    public virtual WorkspacePolicyDecision CanWriteContent(
        WorkspacePolicyContext context,
        WorkspaceSpaceInfo space,
        WorkspaceContentAttachmentInfo attachment,
        WriteWorkspaceSpaceContentRequest request) =>
        WorkspacePolicyDecision.Allow;

    public virtual WorkspacePolicyDecision CanDetachContent(
        WorkspacePolicyContext context,
        WorkspaceSpaceInfo space,
        WorkspaceContentAttachmentInfo attachment) =>
        WorkspacePolicyDecision.Allow;

    public virtual WorkspacePolicyDecision CanAppendEvent(
        WorkspacePolicyContext context,
        WorkspaceSpaceInfo space,
        AppendWorkspaceEventRequest request) =>
        WorkspacePolicyDecision.Allow;
}

public sealed class WorkspacePolicyRegistry
{
    private readonly IReadOnlyDictionary<string, IContentSpacePolicy> _policies;

    public WorkspacePolicyRegistry(IEnumerable<IContentSpacePolicy>? policies = null)
    {
        _policies = (policies ?? BuiltInPolicies())
            .GroupBy(policy => policy.Kind, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
    }

    public static WorkspacePolicyRegistry Default { get; } = new();

    public IContentSpacePolicy Resolve(string kind) =>
        _policies.TryGetValue(kind, out var policy)
            ? policy
            : DefaultContentSpacePolicy.Instance;

    private static IEnumerable<IContentSpacePolicy> BuiltInPolicies()
    {
        yield return new SessionContentSpacePolicy();
        yield return new BranchContentSpacePolicy();
        yield return new SkillContentSpacePolicy();
        yield return new MemoryContentSpacePolicy();
    }
}

internal sealed class DefaultContentSpacePolicy : ContentSpacePolicyBase
{
    public static DefaultContentSpacePolicy Instance { get; } = new();

    private DefaultContentSpacePolicy()
        : base("*")
    {
    }
}

internal sealed class SessionContentSpacePolicy : ContentSpacePolicyBase
{
    public SessionContentSpacePolicy()
        : base("session")
    {
    }

    public override WorkspacePolicyDecision CanAttachContent(
        WorkspacePolicyContext context,
        WorkspaceSpaceInfo space,
        WorkspaceContentInfo content,
        AttachWorkspaceContentRequest request) =>
        IsUploadRole(request.Role) && !context.IsSystem
            ? WorkspacePolicyDecision.Deny("Session uploads are system-managed.")
            : WorkspacePolicyDecision.Allow;

    public override WorkspacePolicyDecision CanDetachContent(
        WorkspacePolicyContext context,
        WorkspaceSpaceInfo space,
        WorkspaceContentAttachmentInfo attachment) =>
        IsUploadRole(attachment.Role) && !context.IsSystem
            ? WorkspacePolicyDecision.Deny("Session uploads are system-managed.")
            : WorkspacePolicyDecision.Allow;

    private static bool IsUploadRole(string role) =>
        string.Equals(role, "upload", StringComparison.Ordinal);
}

internal sealed class BranchContentSpacePolicy : ContentSpacePolicyBase
{
    public BranchContentSpacePolicy()
        : base("branch")
    {
    }

    public override WorkspacePolicyDecision CanAttachContent(
        WorkspacePolicyContext context,
        WorkspaceSpaceInfo space,
        WorkspaceContentInfo content,
        AttachWorkspaceContentRequest request) =>
        IsUploadRole(request.Role) && !context.IsSystem
            ? WorkspacePolicyDecision.Deny("Branch uploads are system-managed.")
            : WorkspacePolicyDecision.Allow;

    public override WorkspacePolicyDecision CanDetachContent(
        WorkspacePolicyContext context,
        WorkspaceSpaceInfo space,
        WorkspaceContentAttachmentInfo attachment) =>
        IsUploadRole(attachment.Role) && !context.IsSystem
            ? WorkspacePolicyDecision.Deny("Branch uploads are system-managed.")
            : WorkspacePolicyDecision.Allow;

    private static bool IsUploadRole(string role) =>
        string.Equals(role, "upload", StringComparison.Ordinal);
}

internal sealed class SkillContentSpacePolicy : ContentSpacePolicyBase
{
    public SkillContentSpacePolicy()
        : base("skill")
    {
    }

    public override WorkspacePolicyDecision CanAttachContent(
        WorkspacePolicyContext context,
        WorkspaceSpaceInfo space,
        WorkspaceContentInfo content,
        AttachWorkspaceContentRequest request) =>
        context.IsSystem
            ? WorkspacePolicyDecision.Allow
            : WorkspacePolicyDecision.Deny("Skill spaces are read-only at runtime.");

    public override WorkspacePolicyDecision CanWriteContent(
        WorkspacePolicyContext context,
        WorkspaceSpaceInfo space,
        WorkspaceContentAttachmentInfo attachment,
        WriteWorkspaceSpaceContentRequest request) =>
        context.IsSystem
            ? WorkspacePolicyDecision.Allow
            : WorkspacePolicyDecision.Deny("Skill spaces are read-only at runtime.");

    public override WorkspacePolicyDecision CanDetachContent(
        WorkspacePolicyContext context,
        WorkspaceSpaceInfo space,
        WorkspaceContentAttachmentInfo attachment) =>
        context.IsSystem
            ? WorkspacePolicyDecision.Allow
            : WorkspacePolicyDecision.Deny("Skill spaces are read-only at runtime.");

    public override WorkspacePolicyDecision CanAppendEvent(
        WorkspacePolicyContext context,
        WorkspaceSpaceInfo space,
        AppendWorkspaceEventRequest request) =>
        context.IsSystem
            ? WorkspacePolicyDecision.Allow
            : WorkspacePolicyDecision.Deny("Skill spaces are read-only at runtime.");
}

internal sealed class MemoryContentSpacePolicy : ContentSpacePolicyBase
{
    public MemoryContentSpacePolicy()
        : base("memory")
    {
    }

    public override WorkspacePolicyDecision CanDetachContent(
        WorkspacePolicyContext context,
        WorkspaceSpaceInfo space,
        WorkspaceContentAttachmentInfo attachment) =>
        context.IsSystem
            ? WorkspacePolicyDecision.Allow
            : WorkspacePolicyDecision.Deny("Memory spaces do not allow runtime destructive detach.");
}
