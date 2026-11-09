namespace HPD.Agent.ToolHarness.FileSystem;

internal static class FileSystemDeprecation
{
    internal const string Message =
        "The HPD-Agent FileSystem ToolHarness is deprecated and is not a supported " +
        "workspace-isolation or command-execution security boundary. Use " +
        "HPD-Agent.ToolHarness.Coding for trusted coding workspaces or a sandbox " +
        "runtime for untrusted execution. See DEPRECATION.md.";
}
