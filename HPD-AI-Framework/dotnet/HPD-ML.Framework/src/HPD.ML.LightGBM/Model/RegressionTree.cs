namespace HPD.ML.LightGBM;

/// <summary>
/// A single decision tree. Immutable after construction.
/// Nodes are stored in arrays for cache-friendly traversal.
/// </summary>
public sealed class RegressionTree
{
    /// <summary>Number of leaves (= internal nodes + 1).</summary>
    public int NumLeaves { get; }

    // ── Internal node arrays (length = NumLeaves - 1) ──

    /// <summary>Feature index to split on at each internal node.</summary>
    public int[] SplitFeatures { get; }

    /// <summary>Threshold for numerical splits.</summary>
    public double[] Thresholds { get; }

    /// <summary>Left child index. Negative = ~leafIndex.</summary>
    public int[] LeftChild { get; }

    /// <summary>Right child index. Negative = ~leafIndex.</summary>
    public int[] RightChild { get; }

    /// <summary>Whether each split is categorical (bitset membership test).</summary>
    public bool[] IsCategoricalSplit { get; }

    /// <summary>Category membership bitsets for categorical splits. Null for numerical splits.</summary>
    public HashSet<int>?[] CategoricalValues { get; }

    // ── Leaf arrays (length = NumLeaves) ──

    /// <summary>Predicted value at each leaf.</summary>
    public double[] LeafValues { get; }

    public RegressionTree(
        int numLeaves,
        int[] splitFeatures,
        double[] thresholds,
        int[] leftChild,
        int[] rightChild,
        bool[] isCategoricalSplit,
        HashSet<int>?[] categoricalValues,
        double[] leafValues)
    {
        NumLeaves = numLeaves;
        SplitFeatures = splitFeatures;
        Thresholds = thresholds;
        LeftChild = leftChild;
        RightChild = rightChild;
        IsCategoricalSplit = isCategoricalSplit;
        CategoricalValues = categoricalValues;
        LeafValues = leafValues;
    }

    /// <summary>
    /// Traverse the tree and return the leaf value for the given features.
    /// </summary>
    public double Predict(ReadOnlySpan<float> features)
    {
        if (NumLeaves == 1)
            return LeafValues[0];

        int node = 0;
        while (node >= 0)
        {
            int featureIdx = SplitFeatures[node];
            float featureVal = featureIdx < features.Length ? features[featureIdx] : 0f;

            bool goLeft;
            if (IsCategoricalSplit[node])
                goLeft = CategoricalValues[node]?.Contains((int)featureVal) ?? false;
            else
                goLeft = featureVal <= Thresholds[node];

            node = goLeft ? LeftChild[node] : RightChild[node];
        }

        return LeafValues[~node];
    }
}
