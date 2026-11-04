namespace HPD.Agent;

/// <summary>Configures optional framework communication capabilities.</summary>
public static class AgentBuilderCommunicationExtensions
{
    /// <summary>
    /// Enables or disables the <c>Parent</c> capability for this agent. Runtime exposure still
    /// requires a controlled subagent execution bound to its invoking parent.
    /// </summary>
    /// <param name="builder">The agent builder.</param>
    /// <param name="enabled">Whether to include the capability.</param>
    /// <returns>The same builder for fluent composition.</returns>
    public static AgentBuilder WithParentCommunication(
        this AgentBuilder builder,
        bool enabled = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Config.EnableParentCommunication = enabled;
        return builder;
    }

    /// <summary>
    /// Enables or disables the <c>AskUser</c> capability for this agent. A run can further
    /// restrict exposure through <see cref="AgentRunConfig.AllowUserQuestions"/>.
    /// </summary>
    /// <param name="builder">The agent builder.</param>
    /// <param name="enabled">Whether to include and expose the capability by default.</param>
    /// <returns>The same builder for fluent composition.</returns>
    public static AgentBuilder WithUserQuestions(
        this AgentBuilder builder,
        bool enabled = true)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Config.AllowUserQuestions = enabled;
        return builder;
    }
}
