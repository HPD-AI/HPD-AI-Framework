namespace HPD.ML.LightGBM;

/// <summary>
/// Collection of regression trees with a bias term.
/// Prediction = bias + Σ tree[i].Predict(features).
/// For multiclass, trees are interleaved by class.
/// </summary>
public sealed class TreeEnsemble
{
    public IReadOnlyList<RegressionTree> Trees { get; }
    public double Bias { get; }
    public int NumberOfClasses { get; }

    public TreeEnsemble(IReadOnlyList<RegressionTree> trees, double bias, int numberOfClasses = 1)
    {
        Trees = trees;
        Bias = bias;
        NumberOfClasses = numberOfClasses;
    }

    /// <summary>
    /// Score a single sample. Returns raw score (regression/ranking),
    /// logit (binary), or per-class raw scores (multiclass).
    /// </summary>
    public double[] Score(ReadOnlySpan<float> features)
    {
        if (NumberOfClasses <= 1)
        {
            double score = Bias;
            for (int i = 0; i < Trees.Count; i++)
                score += Trees[i].Predict(features);
            return [score];
        }
        else
        {
            // Multiclass: trees are interleaved [class0_tree0, class1_tree0, ..., class0_tree1, ...]
            var scores = new double[NumberOfClasses];
            for (int i = 0; i < Trees.Count; i++)
                scores[i % NumberOfClasses] += Trees[i].Predict(features);

            for (int c = 0; c < NumberOfClasses; c++)
                scores[c] += Bias;

            return scores;
        }
    }
}
