namespace HPD.Agent;

/// <summary>Subagent compaction never chooses a summarizer through implicit client inheritance.</summary>
internal static class SubAgentCompactionConfiguration
{
    internal static void Validate(CompactionSpecification? specification)
    {
        if (specification?.Strategy is not SummarizingCompaction summary) return;
        if (summary.Summarizer is not { } client ||
            string.IsNullOrWhiteSpace(client.Provider?.Key) || string.IsNullOrWhiteSpace(client.ModelName))
            throw new AgentRunConfigurationException("subagent_summarizer_required", "Compaction.Summarizer",
                "Subagent summarization requires an explicit summarizer provider and model.");
        if (SubAgentExecutionPolicy.HasRuntimeOverride(client) || SubAgentExecutionPolicy.HasProviderPayload(client))
            throw new InvalidOperationException("subagent_summarizer_not_portable");
    }
}
