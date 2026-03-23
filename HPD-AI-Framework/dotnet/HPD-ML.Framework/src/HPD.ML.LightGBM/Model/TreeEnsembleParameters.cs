namespace HPD.ML.LightGBM;

using HPD.ML.Abstractions;

/// <summary>
/// Serializable wrapper for a tree ensemble — implements ILearnedParameters
/// so the model can be saved/loaded via HPD.ML.Serialization.
/// </summary>
public sealed class TreeEnsembleParameters : ILearnedParameters
{
    public TreeEnsemble Ensemble { get; }
    public double[]? FeatureImportance { get; }

    public TreeEnsembleParameters(TreeEnsemble ensemble, double[]? featureImportance = null)
    {
        Ensemble = ensemble;
        FeatureImportance = featureImportance;
    }
}
