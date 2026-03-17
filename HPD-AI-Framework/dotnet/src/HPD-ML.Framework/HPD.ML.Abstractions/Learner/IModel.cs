namespace HPD.ML.Abstractions;

/// <summary>Immutable bundle of a scoring transform and learned parameters.</summary>
public interface IModel
{
    ITransform Transform { get; }
    ILearnedParameters Parameters { get; }
}
