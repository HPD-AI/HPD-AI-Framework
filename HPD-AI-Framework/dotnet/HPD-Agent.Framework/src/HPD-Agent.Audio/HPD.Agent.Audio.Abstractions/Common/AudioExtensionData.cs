namespace HPD.Agent.Audio;

public sealed record AudioExtensionData(IReadOnlyDictionary<string, object?> Values)
{
    public static AudioExtensionData Empty { get; } = new(new Dictionary<string, object?>());
}
