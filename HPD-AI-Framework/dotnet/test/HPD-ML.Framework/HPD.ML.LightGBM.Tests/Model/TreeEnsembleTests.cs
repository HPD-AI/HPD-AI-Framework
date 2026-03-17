namespace HPD.ML.LightGBM.Tests;

public class TreeEnsembleTests
{
    [Fact]
    public void Score_EmptyEnsemble_ReturnsBias()
    {
        var ensemble = new TreeEnsemble([], bias: 0.5);
        var scores = ensemble.Score([1f, 2f]);
        Assert.Single(scores);
        Assert.Equal(0.5, scores[0], 1e-10);
    }

    [Fact]
    public void Score_SingleTree_ReturnsBiasPlusTreeValue()
    {
        var tree = TestHelper.SingleLeafTree(2.0);
        var ensemble = new TreeEnsemble([tree], bias: 1.0);
        var scores = ensemble.Score([1f]);
        Assert.Equal(3.0, scores[0], 1e-10);
    }

    [Fact]
    public void Score_MultipleTrees_SumsAll()
    {
        var trees = new[]
        {
            TestHelper.SingleLeafTree(1.0),
            TestHelper.SingleLeafTree(2.0),
            TestHelper.SingleLeafTree(3.0)
        };
        var ensemble = new TreeEnsemble(trees, bias: 0.5);
        var scores = ensemble.Score([0f]);
        Assert.Equal(6.5, scores[0], 1e-10);
    }

    [Fact]
    public void Score_Multiclass_InterleavesTreesByClass()
    {
        // 4 trees, 2 classes: interleaved [c0_t0, c1_t0, c0_t1, c1_t1]
        // class0 = tree0(1.0) + tree2(3.0) = 4.0
        // class1 = tree1(2.0) + tree3(4.0) = 6.0
        var trees = new[]
        {
            TestHelper.SingleLeafTree(1.0),
            TestHelper.SingleLeafTree(2.0),
            TestHelper.SingleLeafTree(3.0),
            TestHelper.SingleLeafTree(4.0)
        };
        var ensemble = new TreeEnsemble(trees, bias: 0, numberOfClasses: 2);
        var scores = ensemble.Score([0f]);

        Assert.Equal(2, scores.Length);
        Assert.Equal(4.0, scores[0], 1e-10);
        Assert.Equal(6.0, scores[1], 1e-10);
    }

    [Fact]
    public void Score_Multiclass_AddsBiasToEachClass()
    {
        var trees = new[]
        {
            TestHelper.SingleLeafTree(1.0),
            TestHelper.SingleLeafTree(2.0)
        };
        var ensemble = new TreeEnsemble(trees, bias: 0.5, numberOfClasses: 2);
        var scores = ensemble.Score([0f]);

        Assert.Equal(1.5, scores[0], 1e-10);  // 1.0 + 0.5
        Assert.Equal(2.5, scores[1], 1e-10);  // 2.0 + 0.5
    }

    [Fact]
    public void Score_Regression_NegativeValues()
    {
        var trees = new[]
        {
            TestHelper.SingleLeafTree(-3.0),
            TestHelper.SingleLeafTree(1.0)
        };
        var ensemble = new TreeEnsemble(trees, bias: -1.0);
        var scores = ensemble.Score([0f]);
        Assert.Equal(-3.0, scores[0], 1e-10);  // -3 + 1 + (-1) = -3
    }
}
