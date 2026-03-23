using HPD.ML.Abstractions;
using HPD.ML.Core;

namespace HPD.ML.Core.Tests;

public class SplitDataHandleTests
{
    [Fact]
    public void TrainTestSplit_DefaultFraction_80_20()
    {
        var handle = TestHelpers.CreateSimpleHandle(100);
        var (train, test) = DataHandleSplitter.TrainTestSplit(handle, seed: 42);

        Assert.Equal(80L, train.RowCount);
        Assert.Equal(20L, test.RowCount);
    }

    [Fact]
    public void TrainTestSplit_CustomFraction()
    {
        var handle = TestHelpers.CreateSimpleHandle(100);
        var (train, test) = DataHandleSplitter.TrainTestSplit(handle, testFraction: 0.3, seed: 42);

        Assert.Equal(70L, train.RowCount);
        Assert.Equal(30L, test.RowCount);
    }

    [Fact]
    public void TrainTestSplit_NoOverlap()
    {
        var handle = TestHelpers.CreateSimpleHandle(100);
        var (train, test) = DataHandleSplitter.TrainTestSplit(handle, seed: 42);

        var trainIds = TestHelpers.CollectIntColumn(train, "Id").ToHashSet();
        var testIds = TestHelpers.CollectIntColumn(test, "Id").ToHashSet();

        Assert.Empty(trainIds.Intersect(testIds));
    }

    [Fact]
    public void TrainTestSplit_UnionIsComplete()
    {
        var handle = TestHelpers.CreateSimpleHandle(100);
        var (train, test) = DataHandleSplitter.TrainTestSplit(handle, seed: 42);

        var trainIds = TestHelpers.CollectIntColumn(train, "Id");
        var testIds = TestHelpers.CollectIntColumn(test, "Id");
        var all = trainIds.Concat(testIds).Order().ToList();

        Assert.Equal(Enumerable.Range(0, 100).ToList(), all);
    }

    [Fact]
    public void TrainTestSplit_Deterministic_WithSeed()
    {
        var handle = TestHelpers.CreateSimpleHandle(50);
        var (train1, _) = DataHandleSplitter.TrainTestSplit(handle, seed: 42);
        var (train2, _) = DataHandleSplitter.TrainTestSplit(handle, seed: 42);

        var ids1 = TestHelpers.CollectIntColumn(train1, "Id");
        var ids2 = TestHelpers.CollectIntColumn(train2, "Id");
        Assert.Equal(ids1, ids2);
    }

    [Fact]
    public void CrossValidationSplit_DefaultFolds_Returns5Pairs()
    {
        var handle = TestHelpers.CreateSimpleHandle(100);
        var folds = DataHandleSplitter.CrossValidationSplit(handle, seed: 42);

        Assert.Equal(5, folds.Count);
    }

    [Fact]
    public void CrossValidationSplit_EachFoldTestIsDisjoint()
    {
        var handle = TestHelpers.CreateSimpleHandle(100);
        var folds = DataHandleSplitter.CrossValidationSplit(handle, seed: 42);

        var allTestIds = new List<HashSet<int>>();
        foreach (var (_, test) in folds)
            allTestIds.Add(TestHelpers.CollectIntColumn(test, "Id").ToHashSet());

        for (int i = 0; i < allTestIds.Count; i++)
            for (int j = i + 1; j < allTestIds.Count; j++)
                Assert.Empty(allTestIds[i].Intersect(allTestIds[j]));
    }

    [Fact]
    public void CrossValidationSplit_EachFoldUnionIsComplete()
    {
        var handle = TestHelpers.CreateSimpleHandle(100);
        var folds = DataHandleSplitter.CrossValidationSplit(handle, seed: 42);

        foreach (var (train, test) in folds)
        {
            var trainIds = TestHelpers.CollectIntColumn(train, "Id");
            var testIds = TestHelpers.CollectIntColumn(test, "Id");
            var all = trainIds.Concat(testIds).Order().ToList();
            Assert.Equal(Enumerable.Range(0, 100).ToList(), all);
        }
    }

    [Fact]
    public void CrossValidationSplit_Deterministic_WithSeed()
    {
        var handle = TestHelpers.CreateSimpleHandle(50);
        var a = DataHandleSplitter.CrossValidationSplit(handle, seed: 42);
        var b = DataHandleSplitter.CrossValidationSplit(handle, seed: 42);

        for (int i = 0; i < a.Count; i++)
        {
            var aTest = TestHelpers.CollectIntColumn(a[i].Test, "Id");
            var bTest = TestHelpers.CollectIntColumn(b[i].Test, "Id");
            Assert.Equal(aTest, bTest);
        }
    }
}
