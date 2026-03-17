namespace HPD.ML.Abstractions;

/// <summary>
/// Stateful transform: ordered input, state carried across rows,
/// one output per input. Used for time series, sliding windows,
/// running aggregations.
/// </summary>
public interface IScanTransform<TState> : ITransform
{
    TState InitializeState();
    (TState NextState, IRow Output) ProcessRow(TState state, IRow input);

    /// <summary>Null if state is ephemeral (not checkpointable).</summary>
    IStateSerializer<TState>? StateSerializer { get; }
}
