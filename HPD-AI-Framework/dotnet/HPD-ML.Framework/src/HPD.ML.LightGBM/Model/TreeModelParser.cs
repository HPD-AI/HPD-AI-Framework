namespace HPD.ML.LightGBM;

using System.Globalization;

/// <summary>
/// Parses LightGBM's text model export into a managed TreeEnsemble.
///
/// LightGBM model text format uses sections like:
/// <code>
/// num_class=1
/// ...
/// Tree=0
/// num_leaves=X
/// split_feature=...
/// threshold=...
/// left_child=...
/// right_child=...
/// leaf_value=...
/// decision_type=...
/// cat_threshold=...
/// cat_boundaries=...
///
/// end of trees
/// </code>
/// </summary>
internal static class TreeModelParser
{
    internal static TreeEnsemble Parse(string modelString)
    {
        var lines = modelString.Split('\n');
        var trees = new List<RegressionTree>();
        double bias = 0;
        int numberOfClasses = 1;

        int i = 0;
        while (i < lines.Length)
        {
            var line = lines[i].Trim();

            if (line.StartsWith("num_class=", StringComparison.Ordinal))
                numberOfClasses = int.Parse(line.AsSpan()["num_class=".Length..], CultureInfo.InvariantCulture);

            // LightGBM stores init_score as a line like "average_output" (flag) for regression
            // or the sigmoid init score. The actual init score for binary is embedded differently.
            // For simplicity, extract from "init_score:" header if present.
            if (line.StartsWith("init_score:", StringComparison.Ordinal))
            {
                var parts = line["init_score:".Length..].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0)
                    bias = double.Parse(parts[0], CultureInfo.InvariantCulture);
            }

            // Tree blocks start with "Tree=N"
            if (line.StartsWith("Tree=", StringComparison.Ordinal))
            {
                i++;
                var tree = ParseTree(lines, ref i);
                trees.Add(tree);
                continue;
            }

            if (line == "end of trees")
                break;

            i++;
        }

        return new TreeEnsemble(trees, bias, numberOfClasses);
    }

    private static RegressionTree ParseTree(string[] lines, ref int i)
    {
        int numLeaves = 0;
        int[]? splitFeatures = null;
        double[]? thresholds = null;
        int[]? leftChild = null;
        int[]? rightChild = null;
        int[]? decisionType = null;
        double[]? leafValues = null;
        int[]? catThreshold = null;
        int[]? catBoundaries = null;

        while (i < lines.Length)
        {
            var line = lines[i].Trim();
            if (line.Length == 0 || line.StartsWith("Tree=", StringComparison.Ordinal)
                || line == "end of trees")
                break;

            if (line.StartsWith("num_leaves=", StringComparison.Ordinal))
                numLeaves = int.Parse(line.AsSpan()["num_leaves=".Length..], CultureInfo.InvariantCulture);
            else if (line.StartsWith("split_feature=", StringComparison.Ordinal))
                splitFeatures = ParseIntArray(line);
            else if (line.StartsWith("threshold=", StringComparison.Ordinal))
                thresholds = ParseDoubleArray(line);
            else if (line.StartsWith("left_child=", StringComparison.Ordinal))
                leftChild = ParseIntArray(line);
            else if (line.StartsWith("right_child=", StringComparison.Ordinal))
                rightChild = ParseIntArray(line);
            else if (line.StartsWith("decision_type=", StringComparison.Ordinal))
                decisionType = ParseIntArray(line);
            else if (line.StartsWith("leaf_value=", StringComparison.Ordinal))
                leafValues = ParseDoubleArray(line);
            else if (line.StartsWith("cat_threshold=", StringComparison.Ordinal))
                catThreshold = ParseIntArray(line);
            else if (line.StartsWith("cat_boundaries=", StringComparison.Ordinal))
                catBoundaries = ParseIntArray(line);

            i++;
        }

        int numNodes = numLeaves - 1;
        var isCategorical = new bool[Math.Max(numNodes, 0)];
        var categoricalValues = new HashSet<int>?[Math.Max(numNodes, 0)];

        if (decisionType is not null && catThreshold is not null && catBoundaries is not null)
        {
            int catSplitIndex = 0;
            for (int n = 0; n < numNodes && n < decisionType.Length; n++)
            {
                isCategorical[n] = (decisionType[n] & 1) != 0;

                if (isCategorical[n])
                {
                    categoricalValues[n] = ExtractCategorySet(catThreshold, catBoundaries, catSplitIndex);
                    catSplitIndex++;
                }
            }
        }
        else if (decisionType is not null)
        {
            for (int n = 0; n < numNodes && n < decisionType.Length; n++)
                isCategorical[n] = (decisionType[n] & 1) != 0;
        }

        return new RegressionTree(
            numLeaves,
            splitFeatures ?? [],
            thresholds ?? [],
            leftChild ?? [],
            rightChild ?? [],
            isCategorical,
            categoricalValues,
            leafValues ?? []);
    }

    private static HashSet<int> ExtractCategorySet(int[] catThreshold, int[] catBoundaries, int catSplitIndex)
    {
        var categories = new HashSet<int>();

        if (catSplitIndex >= catBoundaries.Length - 1) return categories;

        int start = catBoundaries[catSplitIndex];
        int end = catBoundaries[catSplitIndex + 1];

        for (int word = start; word < end && word < catThreshold.Length; word++)
        {
            int bits = catThreshold[word];
            for (int bit = 0; bit < 32; bit++)
            {
                if ((bits & (1 << bit)) != 0)
                    categories.Add((word - start) * 32 + bit);
            }
        }

        return categories;
    }

    private static int[] ParseIntArray(string line)
        => line.Split('=', 2)[1].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)
               .Select(s => int.Parse(s, CultureInfo.InvariantCulture)).ToArray();

    private static double[] ParseDoubleArray(string line)
        => line.Split('=', 2)[1].Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)
               .Select(s => double.Parse(s, CultureInfo.InvariantCulture)).ToArray();
}
