namespace HPD.ML.Abstractions;

/// <summary>A function (DataHandle, Model?) -> Model that learns parameters from data.</summary>
/// <remarks>
/// The Learner does NOT own inference state. If inference requires mutable state
/// (sliding windows, KV cache), that belongs to a ScanTransform or GeneratorTransform.
/// Discovery uses C# 14 extension members.
/// </remarks>
public interface ILearner
{
    ISchema GetOutputSchema(ISchema inputSchema);
    IModel Fit(LearnerInput input);
    Task<IModel> FitAsync(LearnerInput input, CancellationToken ct = default);

    /// <summary>Observable training progress. Subscribe before calling Fit/FitAsync.</summary>
    IObservable<ProgressEvent> Progress { get; }
}
