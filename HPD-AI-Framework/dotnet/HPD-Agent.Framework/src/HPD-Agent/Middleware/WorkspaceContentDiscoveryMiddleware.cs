using System.Text;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Middleware;

/// <summary>
/// Injects the agent-facing workspace filesystem tools into the conversation.
/// </summary>
public class WorkspaceContentDiscoveryMiddleware : IAgentMiddleware
{
    private bool _injected;

    public WorkspaceContentDiscoveryMiddleware(string? agentName = null)
    {
    }

    internal void SetToolHarness(WorkspaceContentToolHarness toolHarness)
    {
        ArgumentNullException.ThrowIfNull(toolHarness);
    }

    public Task BeforeMessageTurnAsync(
        BeforeMessageTurnContext context,
        CancellationToken ct)
    {
        if (_injected)
            return Task.CompletedTask;

        var message = new ChatMessage(ChatRole.User, SerializeWorkspaceContentPrompt());
        var insertIndex = context.BranchHistory
            .TakeWhile(m => m.Role == ChatRole.System)
            .Count();
        context.BranchHistory.Insert(insertIndex, message);
        _injected = true;
        return Task.CompletedTask;
    }

    private static string SerializeWorkspaceContentPrompt()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<workspace_content>");
        sb.AppendLine("  You have access to workspace content through a filesystem-like view over spaces and content attachments.");
        sb.AppendLine();
        sb.AppendLine("  Common roots:");
        sb.AppendLine("  - /agents");
        sb.AppendLine("  - /projects");
        sb.AppendLine("  - /sessions");
        sb.AppendLine("  - /workspaces");
        sb.AppendLine();
        sb.AppendLine("  Available tools:");
        sb.AppendLine("  - content_ls(path) - List workspace roots, spaces, directories, and files");
        sb.AppendLine("  - content_tree(path?, depth?) - Show a recursive workspace tree");
        sb.AppendLine("  - content_read(path, offset?, limit?) - Read file contents");
        sb.AppendLine("  - content_write(path, content) - Write or update content in a writable workspace path");
        sb.AppendLine("  - content_stat(path) - Show path, space, attachment, and content metadata");
        sb.AppendLine("  - content_find(pattern, path?) - Find files by name pattern");
        sb.AppendLine("  - content_attach(space_path, content_ref, name?, role?) - Attach an existing content object to a space");
        sb.AppendLine("  - content_detach(path) - Detach content from the space path where it is visible");
        sb.AppendLine();
        sb.AppendLine("  Paths are navigational views. Durable references are IDs: space_id, content_id, attachment_id, and content version.");
        sb.AppendLine("  Start with content_ls(\"/\") to discover accessible workspace state.");
        sb.Append("</workspace_content>");

        return sb.ToString();
    }
}
