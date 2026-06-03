namespace HPD.Agent.Audio.Media;

public sealed record MediaFormatDescriptor
{
    public required string MediaType { get; init; }

    public string? Codec { get; init; }

    public int? SampleRateHz { get; init; }

    public int? ChannelCount { get; init; }

    public int? BitsPerSample { get; init; }
}

public sealed record AudioFormatDescriptor
{
    public required int SampleRateHz { get; init; }

    public required int ChannelCount { get; init; }

    public string? SampleFormat { get; init; }
}

public sealed record EncodedAudioFormatDescriptor
{
    public required string MediaType { get; init; }

    public string? Codec { get; init; }

    public int? SampleRateHz { get; init; }

    public int? ChannelCount { get; init; }
}
