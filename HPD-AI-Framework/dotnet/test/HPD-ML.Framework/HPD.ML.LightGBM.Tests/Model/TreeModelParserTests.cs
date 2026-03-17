namespace HPD.ML.LightGBM.Tests;

public class TreeModelParserTests
{
    private static string SingleTreeModel(
        int numLeaves = 3,
        string splitFeature = "0 1",
        string threshold = "0.5 1.5",
        string leftChild = "-1 -2",
        string rightChild = "1 -3",
        string leafValue = "0.1 0.2 0.3",
        string? decisionType = null,
        string? catThreshold = null,
        string? catBoundaries = null,
        string? header = null)
    {
        var lines = new List<string>();
        if (header is not null)
            lines.Add(header);
        lines.Add("Tree=0");
        lines.Add($"num_leaves={numLeaves}");
        lines.Add($"split_feature={splitFeature}");
        lines.Add($"threshold={threshold}");
        lines.Add($"left_child={leftChild}");
        lines.Add($"right_child={rightChild}");
        lines.Add($"leaf_value={leafValue}");
        if (decisionType is not null)
            lines.Add($"decision_type={decisionType}");
        if (catThreshold is not null)
            lines.Add($"cat_threshold={catThreshold}");
        if (catBoundaries is not null)
            lines.Add($"cat_boundaries={catBoundaries}");
        lines.Add("");
        lines.Add("end of trees");
        return string.Join('\n', lines);
    }

    [Fact]
    public void Parse_SingleTree_ExtractsNumLeaves()
    {
        var model = SingleTreeModel(numLeaves: 3);
        var ensemble = TreeModelParser.Parse(model);

        Assert.Single(ensemble.Trees);
        Assert.Equal(3, ensemble.Trees[0].NumLeaves);
    }

    [Fact]
    public void Parse_SingleTree_ExtractsSplitFeatures()
    {
        var model = SingleTreeModel(splitFeature: "0 1");
        var ensemble = TreeModelParser.Parse(model);

        Assert.Equal([0, 1], ensemble.Trees[0].SplitFeatures);
    }

    [Fact]
    public void Parse_SingleTree_ExtractsThresholds()
    {
        var model = SingleTreeModel(threshold: "0.5 1.5");
        var ensemble = TreeModelParser.Parse(model);

        Assert.Equal(0.5, ensemble.Trees[0].Thresholds[0], 1e-10);
        Assert.Equal(1.5, ensemble.Trees[0].Thresholds[1], 1e-10);
    }

    [Fact]
    public void Parse_SingleTree_ExtractsLeafValues()
    {
        var model = SingleTreeModel(leafValue: "0.1 0.2 0.3");
        var ensemble = TreeModelParser.Parse(model);

        Assert.Equal(3, ensemble.Trees[0].LeafValues.Length);
        Assert.Equal(0.1, ensemble.Trees[0].LeafValues[0], 1e-10);
        Assert.Equal(0.2, ensemble.Trees[0].LeafValues[1], 1e-10);
        Assert.Equal(0.3, ensemble.Trees[0].LeafValues[2], 1e-10);
    }

    [Fact]
    public void Parse_MultipleTrees_ParsesAll()
    {
        var model = string.Join('\n',
            "Tree=0",
            "num_leaves=2",
            "split_feature=0",
            "threshold=1.0",
            "left_child=-1",
            "right_child=-2",
            "leaf_value=0.1 0.2",
            "",
            "Tree=1",
            "num_leaves=2",
            "split_feature=1",
            "threshold=2.0",
            "left_child=-1",
            "right_child=-2",
            "leaf_value=0.3 0.4",
            "",
            "Tree=2",
            "num_leaves=2",
            "split_feature=0",
            "threshold=3.0",
            "left_child=-1",
            "right_child=-2",
            "leaf_value=0.5 0.6",
            "",
            "end of trees");

        var ensemble = TreeModelParser.Parse(model);
        Assert.Equal(3, ensemble.Trees.Count);
    }

    [Fact]
    public void Parse_NumClass_ParsesCorrectly()
    {
        var model = "num_class=3\n" + SingleTreeModel();
        var ensemble = TreeModelParser.Parse(model);
        Assert.Equal(3, ensemble.NumberOfClasses);
    }

    [Fact]
    public void Parse_CategoricalSplits_ExtractsBitsets()
    {
        // One categorical split (node 0), one numerical split (node 1)
        // decision_type bit 0 = categorical
        // cat_boundaries: [0, 1] means cat split 0 spans catThreshold[0..1)
        // cat_threshold[0] = 0b1010 = bits 1 and 3 set → categories {1, 3}
        var model = SingleTreeModel(
            decisionType: "1 0",
            catThreshold: "10",       // 0b1010 = {1, 3}
            catBoundaries: "0 1");

        var ensemble = TreeModelParser.Parse(model);
        var tree = ensemble.Trees[0];

        Assert.True(tree.IsCategoricalSplit[0]);
        Assert.False(tree.IsCategoricalSplit[1]);
        Assert.NotNull(tree.CategoricalValues[0]);
        Assert.Contains(1, tree.CategoricalValues[0]!);
        Assert.Contains(3, tree.CategoricalValues[0]!);
        Assert.DoesNotContain(0, tree.CategoricalValues[0]!);
        Assert.DoesNotContain(2, tree.CategoricalValues[0]!);
    }

    [Fact]
    public void Parse_EndOfTrees_StopsProcessing()
    {
        var model = string.Join('\n',
            "Tree=0",
            "num_leaves=2",
            "split_feature=0",
            "threshold=1.0",
            "left_child=-1",
            "right_child=-2",
            "leaf_value=0.1 0.2",
            "",
            "end of trees",
            "",
            "Tree=1",
            "num_leaves=2",
            "split_feature=0",
            "threshold=2.0",
            "left_child=-1",
            "right_child=-2",
            "leaf_value=0.3 0.4");

        var ensemble = TreeModelParser.Parse(model);
        // Only tree 0 should be parsed — tree 1 is after "end of trees"
        Assert.Single(ensemble.Trees);
    }
}
