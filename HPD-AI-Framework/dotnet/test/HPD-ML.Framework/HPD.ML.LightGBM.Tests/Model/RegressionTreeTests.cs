namespace HPD.ML.LightGBM.Tests;

public class RegressionTreeTests
{
    [Fact]
    public void Predict_SingleLeafTree_ReturnsBias()
    {
        var tree = TestHelper.SingleLeafTree(3.5);
        double result = tree.Predict([1f, 2f]);
        Assert.Equal(3.5, result);
    }

    [Fact]
    public void Predict_TwoLeaves_GoesLeft()
    {
        // feature[0] <= 5.0 → left (value=1.0)
        var tree = TestHelper.TwoLeafTree(splitFeature: 0, threshold: 5.0, leftValue: 1.0, rightValue: 9.0);
        double result = tree.Predict([3.0f, 0f]);
        Assert.Equal(1.0, result);
    }

    [Fact]
    public void Predict_TwoLeaves_GoesRight()
    {
        // feature[0] > 5.0 → right (value=9.0)
        var tree = TestHelper.TwoLeafTree(splitFeature: 0, threshold: 5.0, leftValue: 1.0, rightValue: 9.0);
        double result = tree.Predict([7.0f, 0f]);
        Assert.Equal(9.0, result);
    }

    [Fact]
    public void Predict_DeepTree_TraversesCorrectly()
    {
        // 3 leaves, 2 internal nodes:
        //   node0: feature[0] <= 5.0 → left=leaf0(10), right=node1
        //   node1: feature[1] <= 3.0 → left=leaf1(20), right=leaf2(30)
        var tree = new RegressionTree(
            numLeaves: 3,
            splitFeatures: [0, 1],
            thresholds: [5.0, 3.0],
            leftChild: [~0, ~1],    // node0→leaf0, node1→leaf1
            rightChild: [1, ~2],    // node0→node1, node1→leaf2
            isCategoricalSplit: [false, false],
            categoricalValues: [null, null],
            leafValues: [10.0, 20.0, 30.0]);

        // feature[0]=3 <= 5 → leaf0
        Assert.Equal(10.0, tree.Predict([3f, 1f]));
        // feature[0]=7 > 5 → node1, feature[1]=2 <= 3 → leaf1
        Assert.Equal(20.0, tree.Predict([7f, 2f]));
        // feature[0]=7 > 5 → node1, feature[1]=5 > 3 → leaf2
        Assert.Equal(30.0, tree.Predict([7f, 5f]));
    }

    [Fact]
    public void Predict_MissingFeature_TreatsAsZero()
    {
        // Split on feature[5] with threshold 1.0 — features only has 2 elements
        // Missing feature → 0.0 → 0 <= 1.0 → left
        var tree = TestHelper.TwoLeafTree(splitFeature: 5, threshold: 1.0, leftValue: 42.0, rightValue: 99.0);
        double result = tree.Predict([1f, 2f]);
        Assert.Equal(42.0, result);
    }

    [Fact]
    public void Predict_CategoricalSplit_InSet_GoesLeft()
    {
        var tree = new RegressionTree(
            numLeaves: 2,
            splitFeatures: [0],
            thresholds: [0.0],  // threshold not used for categorical
            leftChild: [~0],
            rightChild: [~1],
            isCategoricalSplit: [true],
            categoricalValues: [new HashSet<int> { 1, 3, 5 }],
            leafValues: [100.0, 200.0]);

        // feature=3 is in {1,3,5} → left
        double result = tree.Predict([3f]);
        Assert.Equal(100.0, result);
    }

    [Fact]
    public void Predict_CategoricalSplit_NotInSet_GoesRight()
    {
        var tree = new RegressionTree(
            numLeaves: 2,
            splitFeatures: [0],
            thresholds: [0.0],
            leftChild: [~0],
            rightChild: [~1],
            isCategoricalSplit: [true],
            categoricalValues: [new HashSet<int> { 1, 3, 5 }],
            leafValues: [100.0, 200.0]);

        // feature=4 is NOT in {1,3,5} → right
        double result = tree.Predict([4f]);
        Assert.Equal(200.0, result);
    }
}
