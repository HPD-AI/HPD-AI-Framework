using System.Text;
using Microsoft.Extensions.AI;

namespace HPD.Agent.Middleware;

/// <summary>
/// Middleware that injects folder structure (NOT file listings) into the conversation.
/// Shows available folders with descriptions, permissions, and scope.
/// Agent uses content_list(), content_read() etc. to explore files on-demand.
/// </summary>
/// <remarks>
/// <para><b>Token Efficiency:</b></para>
/// Scales with folder count (5-10 folders ≈ 250 tokens), NOT file count.
/// Auto-injects the full listing on the first turn, then only diffs when folders change.
///
/// <para><b>How it works:</b></para>
/// <list type="bullet">
/// <item>First turn: inject full folder listing (~250 tokens)</item>
/// <item>Subsequent turns: only inject if folder structure changed</item>
/// <item>New session folder: appended when a session is available</item>
/// </list>
/// </remarks>
public class FolderDiscoveryMiddleware : IAgentMiddleware
{
    private readonly IContentStore _store;
    private readonly string? _agentName;
    private FolderStructureSnapshot? _lastSnapshot;
    private ContentStoreToolkit? _toolkit; // Set via SetToolkit for session ID threading

    public FolderDiscoveryMiddleware(IContentStore store, string? agentName = null)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _agentName = agentName;
    }

    /// <summary>
    /// Link the ContentStoreToolkit so FolderDiscoveryMiddleware can share session ID state.
    /// Called by AgentBuilder when registering both middleware and toolkit.
    /// </summary>
    internal void SetToolkit(ContentStoreToolkit toolkit)
    {
        _toolkit = toolkit;
    }

    public async Task BeforeMessageTurnAsync(
        BeforeMessageTurnContext context,
        CancellationToken ct)
    {
        var sessionId = context.Session?.Id;
        // Propagate session ID to toolkit so session-scoped tools (/uploads, /artifacts) work
        _toolkit?.SetSessionId(sessionId);
        var current = await BuildSnapshotAsync(sessionId, ct);

        string? contextXml = null;

        if (_lastSnapshot == null)
        {
            // First turn — inject full listing
            contextXml = current.SerializeToXml();
        }
        else if (!_lastSnapshot.StructurallyEquals(current))
        {
            // Folder structure changed — inject update
            contextXml = $"[Content Store Update]\n{current.SerializeToXml()}";
        }
        // else: no change, skip injection

        if (contextXml != null)
        {
            // Insert after system messages but before conversation history
            var folderMessage = new ChatMessage(ChatRole.User, contextXml);
            var insertIndex = context.ConversationHistory
                .TakeWhile(m => m.Role == ChatRole.System)
                .Count();
            context.ConversationHistory.Insert(insertIndex, folderMessage);
        }

        _lastSnapshot = current;
    }

    private async Task<FolderStructureSnapshot> BuildSnapshotAsync(string? sessionId, CancellationToken ct)
    {
        var folders = new List<FolderInfo>(await _store.ListFoldersAsync(ct));

        // Add session-scoped folders when a session is active
        if (sessionId != null)
        {
            folders.Add(new FolderInfo
            {
                Name = "uploads",
                Path = "/uploads",
                Description = "User-uploaded files for this session",
                Permissions = ContentPermissions.Read,
                Scope = "session"
            });
            folders.Add(new FolderInfo
            {
                Name = "artifacts",
                Path = "/artifacts",
                Description = "Agent-generated outputs for this session",
                Permissions = ContentPermissions.ReadWrite,
                Scope = "session"
            });
        }

        return new FolderStructureSnapshot
        {
            Folders = folders.OrderBy(f => f.Path).ToList(),
            HasSession = sessionId != null
        };
    }
}

/// <summary>
/// Snapshot of the folder structure at a point in time.
/// Used for diff-based injection (only re-inject if structure changed).
/// </summary>
internal sealed class FolderStructureSnapshot
{
    public required List<FolderInfo> Folders { get; init; }
    public bool HasSession { get; init; }

    /// <summary>
    /// Serialize to the compact XML format injected into the agent's context.
    /// ~250 tokens for a typical 5-folder setup.
    /// </summary>
    public string SerializeToXml()
    {
        var sb = new StringBuilder();
        sb.AppendLine("<content_store>");
        sb.AppendLine("  You have access to a Virtual Content Store with filesystem-like navigation tools.");
        sb.AppendLine();
        sb.AppendLine("  Folders:");

        foreach (var folder in Folders)
        {
            var scopeLabel = folder.Scope == "session" ? "session-scoped" : "agent-scoped";
            var permLabel = folder.Permissions.ToString().ToLower();
            sb.AppendLine($"  - {folder.Path} - {folder.Description} ({permLabel}, {scopeLabel})");
        }

        sb.AppendLine();
        sb.AppendLine("  Available tools:");
        sb.AppendLine("  - content_list(path) - List files in a folder");
        sb.AppendLine("  - content_read(path, offset?, limit?) - Read file contents");
        sb.AppendLine("  - content_glob(pattern, path?) - Find files by name pattern");
        sb.AppendLine("  - content_write(path, content) - Write/update a file");
        sb.AppendLine("  - content_delete(path) - Delete a file");
        sb.AppendLine("  - content_tree(path?, depth?) - Show folder hierarchy");
        sb.AppendLine("  - content_stat(path) - Show file metadata");
        sb.AppendLine();
        sb.AppendLine("  Start with content_list(\"/\") to explore what's available.");
        sb.Append("</content_store>");

        return sb.ToString();
    }

    /// <summary>
    /// Compare folder structure (paths + descriptions only) to detect changes.
    /// </summary>
    public bool StructurallyEquals(FolderStructureSnapshot other)
    {
        if (other == null || Folders.Count != other.Folders.Count || HasSession != other.HasSession)
            return false;

        for (int i = 0; i < Folders.Count; i++)
        {
            if (Folders[i].Path != other.Folders[i].Path ||
                Folders[i].Description != other.Folders[i].Description)
                return false;
        }

        return true;
    }
}
