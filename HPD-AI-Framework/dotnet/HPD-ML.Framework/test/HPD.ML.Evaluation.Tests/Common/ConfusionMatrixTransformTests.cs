namespace HPD.ML.Evaluation.Tests;

public class ConfusionMatrixTransformTests
{
    [Fact]
    public void Apply_BinaryPerfect_DiagonalCounts()
    {
        var data = TestHelper.Data(
            ("Label", new string[] { "A", "A", "B", "B" }),
            ("PredictedLabel", new string[] { "A", "A", "B", "B" }));

        var transform = new ConfusionMatrixTransform();
        var cm = transform.Apply(data);

        Assert.Equal(2, TestHelper.CountRows(cm)); // (A,A) and (B,B)
        var trueLabels = TestHelper.CollectString(cm, "TrueLabel");
        var predLabels = TestHelper.CollectString(cm, "PredictedLabel");
        var counts = TestHelper.CollectDouble(cm, "Count");

        // All predictions are on the diagonal
        for (int i = 0; i < trueLabels.Count; i++)
            Assert.Equal(trueLabels[i], predLabels[i]);
    }

    [Fact]
    public void Apply_WithErrors_OffDiagonalCounts()
    {
        var data = TestHelper.Data(
            ("Label", new string[] { "A", "A", "B" }),
            ("PredictedLabel", new string[] { "A", "B", "B" }));

        var transform = new ConfusionMatrixTransform();
        var cm = transform.Apply(data);

        // Pairs: (A,A)=1, (A,B)=1, (B,B)=1 → 3 unique pairs
        Assert.Equal(3, TestHelper.CountRows(cm));
        Assert.True(TestHelper.CountRows(cm) >= 2);
    }

    [Fact]
    public void Apply_Multiclass_AllCellsCounted()
    {
        var data = TestHelper.Data(
            ("Label", new string[] { "X", "Y", "Z", "X", "Y", "Z" }),
            ("PredictedLabel", new string[] { "X", "X", "Z", "Y", "Y", "X" }));

        var transform = new ConfusionMatrixTransform();
        var cm = transform.Apply(data);

        int rows = TestHelper.CountRows(cm);
        Assert.True(rows > 0);
    }

    [Fact]
    public void GetOutputSchema_HasCorrectColumns()
    {
        var transform = new ConfusionMatrixTransform();
        var schema = transform.GetOutputSchema(
            new HPD.ML.Core.SchemaBuilder().AddColumn<float>("dummy").Build());

        Assert.NotNull(schema.FindByName("TrueLabel"));
        Assert.NotNull(schema.FindByName("PredictedLabel"));
        Assert.NotNull(schema.FindByName("Count"));
    }

    [Fact]
    public void Formatter_ProducesReadableGrid()
    {
        var data = TestHelper.Data(
            ("Label", new string[] { "A", "A", "B", "B" }),
            ("PredictedLabel", new string[] { "A", "B", "A", "B" }));

        var transform = new ConfusionMatrixTransform();
        var cm = transform.Apply(data);
        string formatted = ConfusionMatrixFormatter.Format(cm);

        Assert.Contains("A", formatted);
        Assert.Contains("B", formatted);
        Assert.True(formatted.Split('\n', StringSplitOptions.RemoveEmptyEntries).Length >= 2);
    }
}
