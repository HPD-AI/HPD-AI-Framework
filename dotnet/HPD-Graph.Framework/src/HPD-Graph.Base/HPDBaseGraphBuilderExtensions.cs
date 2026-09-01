using HPD.Base;

namespace HPD.Graph.Base;

/// <summary>Provides HPD-Graph registration helpers for an HPD.Base application graph.</summary>
public static class HPDBaseGraphBuilderExtensions
{
    /// <summary>Installs Graph's private checkpoint collection and atomic persistence operation.</summary>
    public static HPDBaseBuilder AddGraphPersistence(this HPDBaseBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder
            .AddCollection(BaseGraphCheckpointRecord.Collection)
            .AddModuleMutation(BaseGraphCheckpointMutation.Definition, BaseGraphCheckpointMutation.Identity);
    }

    /// <summary>
    /// Installs one sealed graph activation definition and its graph-owned,
    /// Native-AOT-safe handler factory into the application graph.
    /// </summary>
    /// <param name="builder">The mutable HPD.Base application builder.</param>
    /// <param name="definition">The sealed graph activation definition.</param>
    /// <returns>The same builder for fluent configuration.</returns>
    public static HPDBaseBuilder AddGraphActivation(
        this HPDBaseBuilder builder,
        BaseGraphActivationDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(definition);
        builder.AddActivation(definition.Registration);
        builder.AddActivation(definition.ResumeRegistration);
        builder.AddActivation(definition.PollingResumeRegistration);
        builder.AddActivation(definition.OperatorResumeRegistration);
        return builder;
    }

    /// <summary>Installs one sealed graph activation together with one durable schedule.</summary>
    /// <param name="builder">The mutable HPD.Base application builder.</param>
    /// <param name="definition">The sealed graph activation definition.</param>
    /// <param name="schedule">The sealed schedule targeting the definition.</param>
    /// <returns>The same builder for fluent configuration.</returns>
    public static HPDBaseBuilder AddScheduledGraphActivation(
        this HPDBaseBuilder builder,
        BaseGraphActivationDefinition definition,
        BaseGeneratedScheduleRegistration schedule)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(schedule);
        if (!string.Equals(schedule.Definition.Activation.Id, definition.Registration.Definition.Id, StringComparison.Ordinal)
            || schedule.Definition.Activation.Version != definition.Registration.Definition.Version
            || !System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                schedule.Definition.Activation.Checksum.AsSpan(), definition.Registration.Definition.Checksum.AsSpan()))
            throw new InvalidOperationException("hpd.graph.activation.scheduleInvalid");
        builder.AddActivation(definition.Registration);
        builder.AddActivation(definition.ResumeRegistration);
        builder.AddActivation(definition.PollingResumeRegistration);
        builder.AddActivation(definition.OperatorResumeRegistration);
        builder.AddSchedule(schedule);
        return builder;
    }
}
