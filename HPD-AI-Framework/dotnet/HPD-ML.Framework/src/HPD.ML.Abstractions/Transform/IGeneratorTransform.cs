namespace HPD.ML.Abstractions;

/// <summary>
/// Stateful transform: seed input produces variable-length output stream.
/// Each output feeds back as input to the next step. Used for
/// autoregressive generation, Monte Carlo simulation, recursive expansion.
/// </summary>
/// <remarks>
/// Concrete implementations may also implement IChatClient from
/// Microsoft.Extensions.AI for chat middleware interop.
/// </remarks>
public interface IGeneratorTransform<TState> : ITransform
{
    TState InitializeState(IDataHandle seed);

    /// <summary>Produce one output and advance state. Null = generation complete.</summary>
    (TState NextState, IRow Output)? Step(TState state);

    int? MaxOutputLength { get; }
}
