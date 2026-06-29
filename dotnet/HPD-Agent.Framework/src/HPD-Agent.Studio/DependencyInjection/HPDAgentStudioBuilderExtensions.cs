namespace HPD.AI.Studio;

public static class HPDAgentStudioBuilderExtensions
{
    public static HPDAIStudioBuilder AddAgentStudio(this HPDAIStudioBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        return builder.AddModule(
            "agents",
            "Agents",
            "HPD Agent Studio",
            "active",
            "agents",
            "sessions",
            "threads",
            "streaming",
            "content",
            "multi-agent",
            "agent-evals");
    }
}
