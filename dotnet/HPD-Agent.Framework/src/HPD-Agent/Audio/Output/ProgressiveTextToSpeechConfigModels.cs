namespace HPD.Agent.Audio.Output;

public enum PushTextInputAggregationMode
{
    ProviderDefault = 0,
    RawDelta = 1,
    Sentence = 2,
    Token = 3,
    ManualFlush = 4
}

public enum ProgressiveTextToSpeechRouteMode
{
    Auto = 0,
    ForceSegment = 1,
    ForcePushText = 2
}
