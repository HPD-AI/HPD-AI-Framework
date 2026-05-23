using Rhodium.Simulation;

namespace Rhodium.Simulation.Tests.Models;

/// <summary>
/// Tests for QueueParams and all 7 queue models.
/// </summary>
public class QueueParamsTests
{
    [Fact]
    public void AlwaysFront_CreatesCorrectModel()
    {
        var queue = QueueParams.AlwaysFront();

        Assert.Equal(QueueModelType.AlwaysFront, queue.Model);
    }

    [Fact]
    public void RiskAverse_CreatesCorrectModel()
    {
        var queue = QueueParams.RiskAverse();

        Assert.Equal(QueueModelType.RiskAverse, queue.Model);
    }

    [Fact]
    public void PowerQuadratic_UsesAlpha2()
    {
        var queue = QueueParams.PowerQuadratic();

        Assert.Equal(QueueModelType.PowerProbabilistic, queue.Model);
        Assert.Equal(2.0, queue.Alpha);
    }

    [Fact]
    public void PowerCubic_UsesAlpha3()
    {
        var queue = QueueParams.PowerCubic();

        Assert.Equal(QueueModelType.PowerProbabilistic, queue.Model);
        Assert.Equal(3.0, queue.Alpha);
    }

    [Fact]
    public void PowerAsymmetric_UsesCorrectParameters()
    {
        var queue = QueueParams.PowerAsymmetric();

        Assert.Equal(QueueModelType.PowerProbabilistic2, queue.Model);
        Assert.Equal(3.0, queue.Alpha1);
        Assert.Equal(1.5, queue.Alpha2);
        Assert.Equal(0.5, queue.Transition);
    }

    [Fact]
    public void Logarithmic_UsesCorrectScale()
    {
        var queue = QueueParams.Logarithmic(scale: 15.0);

        Assert.Equal(QueueModelType.LogProbabilistic, queue.Model);
        Assert.Equal(15.0, queue.Scale);
    }

    [Fact]
    public void Logarithmic_DefaultScale()
    {
        var queue = QueueParams.Logarithmic();

        Assert.Equal(10.0, queue.Scale);
    }

    [Fact]
    public void RealisticLiquid_EnablesProbabilisticEntry()
    {
        var queue = QueueParams.RealisticLiquid();

        Assert.Equal(QueueModelType.PowerProbabilistic, queue.Model);
        Assert.Equal(2.0, queue.Alpha);
        Assert.True(queue.ProbabilisticEntry);
    }

    [Fact]
    public void RealisticIlliquid_DisablesProbabilisticEntry()
    {
        var queue = QueueParams.RealisticIlliquid();

        Assert.Equal(QueueModelType.PowerProbabilistic, queue.Model);
        Assert.Equal(3.0, queue.Alpha);
        Assert.False(queue.ProbabilisticEntry);
    }

    [Fact]
    public void CustomParameters_WorksCorrectly()
    {
        var queue = new QueueParams
        {
            Model = QueueModelType.PowerProbabilistic,
            Alpha = 2.5,
            ProbabilisticEntry = true
        };

        Assert.Equal(QueueModelType.PowerProbabilistic, queue.Model);
        Assert.Equal(2.5, queue.Alpha);
        Assert.True(queue.ProbabilisticEntry);
    }

    [Fact]
    public void AllQueueModelTypes_AreDefined()
    {
        // Ensure all 7 queue model types are accounted for
        var allModels = Enum.GetValues<QueueModelType>();

        Assert.Equal(7, allModels.Length);
        Assert.Contains(QueueModelType.AlwaysFront, allModels);
        Assert.Contains(QueueModelType.RiskAverse, allModels);
        Assert.Contains(QueueModelType.PowerProbabilistic, allModels);
        Assert.Contains(QueueModelType.PowerProbabilistic2, allModels);
        Assert.Contains(QueueModelType.PowerProbabilistic3, allModels);
        Assert.Contains(QueueModelType.LogProbabilistic, allModels);
        Assert.Contains(QueueModelType.LogProbabilistic2, allModels);
    }
}
