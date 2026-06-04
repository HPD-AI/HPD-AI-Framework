namespace HPD.Agent;

/// <summary>
/// Canonical agent-facing paths derived from workspace spaces.
/// </summary>
public static class WorkspaceContentPaths
{
    public const string AgentsRoot = "/agents";
    public const string ProjectsRoot = "/projects";
    public const string SessionsRoot = "/sessions";
    public const string WorkspacesRoot = "/workspaces";

    public static string AgentSkills(string agentId) => $"{Agent(agentId)}/skills";

    public static string AgentKnowledge(string agentId) => $"{Agent(agentId)}/knowledge";

    public static string AgentMemory(string agentId) => $"{Agent(agentId)}/memory";

    public static string AgentMemoryEvents(string agentId) => $"{AgentMemory(agentId)}/events";

    public static string SessionBranch(string sessionId, string branchId) => $"{Session(sessionId)}/branches/{NormalizeSegment(branchId)}";

    public static string BranchUploads(string sessionId, string branchId) => $"{SessionBranch(sessionId, branchId)}/uploads";

    public static string BranchArtifacts(string sessionId, string branchId) => $"{SessionBranch(sessionId, branchId)}/artifacts";

    public static string BranchArtifact(string sessionId, string branchId, string artifactName) => $"{BranchArtifacts(sessionId, branchId)}/{NormalizeRelativePath(artifactName)}";

    public static string Agent(string agentId) => $"{AgentsRoot}/{NormalizeSegment(agentId)}";

    public static string Project(string projectId) => $"{ProjectsRoot}/{NormalizeSegment(projectId)}";

    public static string Session(string sessionId) => $"{SessionsRoot}/{NormalizeSegment(sessionId)}";

    public static string NormalizeSegment(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim().Trim('/').Replace('\\', '/');
    }

    public static string NormalizeRelativePath(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return value.Trim().Trim('/').Replace('\\', '/');
    }

    public static string NormalizePath(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "/";

        var normalized = "/" + value.Trim().Trim('/').Replace('\\', '/');
        while (normalized.Contains("//", StringComparison.Ordinal))
            normalized = normalized.Replace("//", "/", StringComparison.Ordinal);

        return normalized;
    }

    public static IReadOnlyList<string> Split(string? value)
    {
        var normalized = NormalizePath(value);
        if (normalized == "/")
            return [];

        return normalized.Trim('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
    }
}
