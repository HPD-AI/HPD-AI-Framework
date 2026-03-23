namespace HPD.ML.Abstractions;

[Flags]
public enum SaveContent
{
    LearnedParameters = 1,
    PipelineTopology = 2,
    InferenceState = 4,
    All = LearnedParameters | PipelineTopology | InferenceState
}
