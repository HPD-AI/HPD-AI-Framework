namespace HPD.Base;

/// <summary>Provides immutable lookup over graph-installed durable schedules.</summary>
public sealed class BaseScheduleRegistry
{
    private readonly Dictionary<(string Id, int Version), BaseScheduleDefinition> _definitions;

    internal BaseScheduleRegistry(IEnumerable<BaseScheduleDefinition> definitions)
    {
        _definitions = definitions.ToDictionary(
            static value => (value.Id, value.Version),
            static value => BaseScheduleDefinitionBuilder.Create(value));
    }

    /// <summary>Finds one exact installed schedule definition.</summary>
    public BaseScheduleDefinition? Find(string id, int version) =>
        _definitions.TryGetValue((id, version), out BaseScheduleDefinition? value)
            ? BaseScheduleDefinitionBuilder.Create(value)
            : null;

    internal IReadOnlyCollection<BaseScheduleDefinition> All => _definitions.Values;
}

/// <summary>Creates an inert identity for one sealed durable schedule.</summary>
public static class BaseScheduleRegistration
{
    /// <summary>Seals the definition and returns its non-executable identity.</summary>
    public static BaseScheduleRegistrationIdentity Create(BaseScheduleDefinition definition)
    {
        BaseScheduleDefinition sealedDefinition = BaseScheduleDefinitionBuilder.Create(definition);
        return new BaseScheduleRegistrationIdentity(
            sealedDefinition.Id, sealedDefinition.Version, sealedDefinition.Checksum.ToArray());
    }
}
