namespace HPD.Agent.Audio.Output;

public interface ITtsPacer
{
    IReadOnlyList<TextToSpeechSegment> PushText(
        string textDelta,
        TextToSpeechPacingContext context);

    IReadOnlyList<TextToSpeechSegment> Flush(TextToSpeechPacingContext context);

    void Reset();
}
