namespace HPD.Agent.ToolHarness.Coding.Debugging;

internal static class DotNetDebugExecutable
{
    public static string Resolve()
    {
        var configured = System.Environment.GetEnvironmentVariable("HPD_DOTNET");
        return string.IsNullOrWhiteSpace(configured) ? "dotnet" : configured;
    }
}
