namespace HPD.Agent.Audio;

/// <summary>Declares the immutable schema identity carried by an Audio-session start binding.</summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = false, Inherited = false)]
public sealed class AudioSessionBindingAttribute : Attribute
{
    public required string Component { get; init; }
    public required string Schema { get; init; }
    public uint Version { get; init; } = 1;
}
