using System.Text.Json.Serialization;

namespace HPD.Agent;

/// <summary>Declares whether a store's data remains resolvable after process restart.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StorePersistenceCapability>))]
public enum StorePersistenceCapability
{
    /// <summary>Data is valid only for the current store lifetime.</summary>
    Ephemeral,

    /// <summary>Data remains resolvable after process restart.</summary>
    RestartDurable
}
