namespace HPD.ML.Core;

using HPD.ML.Abstractions;

/// <summary>
/// Default IModel implementation. Immutable bundle of a scoring transform
/// and the learned parameters that produced it.
/// </summary>
public sealed record Model(ITransform Transform, ILearnedParameters Parameters) : IModel;
