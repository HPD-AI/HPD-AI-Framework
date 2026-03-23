namespace HPD.ML.TimeSeries.Tests;

public class SsaTrainerTests
{
    [Fact]
    public void MaterializeSeries_ExtractsValues()
    {
        var data = TestHelper.SineData(10, period: 4);
        var series = SsaTrainer.MaterializeSeries(data, "Value");
        Assert.Equal(10, series.Length);
        Assert.All(series, v => Assert.True(double.IsFinite(v)));
    }

    [Fact]
    public void Train_ProducesValidParameters()
    {
        var series = new double[50];
        for (int i = 0; i < 50; i++) series[i] = Math.Sin(2 * Math.PI * i / 12);
        var p = SsaTrainer.Train(series, windowSize: 8, seriesLength: 0, rank: 0, RankSelectionMethod.Exact);

        Assert.Equal(8, p.WindowSize);
        Assert.Equal(50, p.SeriesLength);
        Assert.True(p.Rank >= 1);
        Assert.Equal(7, p.AutoRegressiveCoefficients.Length); // windowSize - 1
        Assert.Equal(p.Rank * 8, p.Eigenvectors.Length);
        Assert.Equal(p.Rank, p.SingularValues.Length);
        Assert.Equal(p.Rank, p.InitialStateVector.Length);
    }

    [Fact]
    public void Train_EigenvectorsOrthogonal()
    {
        var series = new double[50];
        for (int i = 0; i < 50; i++) series[i] = Math.Sin(2 * Math.PI * i / 12);
        var p = SsaTrainer.Train(series, 8, 0, 0, RankSelectionMethod.Exact);

        int L = p.WindowSize;
        for (int i = 0; i < p.Rank; i++)
            for (int j = i + 1; j < p.Rank; j++)
            {
                double dot = 0;
                for (int k = 0; k < L; k++)
                    dot += p.Eigenvectors[i * L + k] * p.Eigenvectors[j * L + k];
                Assert.True(Math.Abs(dot) < 0.1,
                    $"Eigenvectors {i} and {j} should be orthogonal, dot={dot}");
            }
    }

    [Fact]
    public void Train_SingularValuesDescending()
    {
        var series = new double[50];
        for (int i = 0; i < 50; i++) series[i] = Math.Sin(2 * Math.PI * i / 12);
        var p = SsaTrainer.Train(series, 8, 0, 0, RankSelectionMethod.Exact);

        for (int i = 0; i < p.SingularValues.Length - 1; i++)
            Assert.True(p.SingularValues[i] >= p.SingularValues[i + 1] - 1e-10,
                $"SV[{i}]={p.SingularValues[i]} < SV[{i + 1}]={p.SingularValues[i + 1]}");
    }

    [Fact]
    public void Train_ShortSeries_Throws()
    {
        var series = new double[] { 1, 2, 3 };
        Assert.Throws<ArgumentException>(() =>
            SsaTrainer.Train(series, windowSize: 8, seriesLength: 0, rank: 0, RankSelectionMethod.Exact));
    }

    [Fact]
    public void EigenDecompose_IdentityMatrix()
    {
        var cov = new double[] { 1, 0, 0, 0, 1, 0, 0, 0, 1 };
        var (eigenvalues, _) = SsaTrainer.EigenDecompose(cov, 3);
        Assert.All(eigenvalues, ev => Assert.True(Math.Abs(ev - 1.0) < 1e-10, $"Expected 1.0, got {ev}"));
    }
}
