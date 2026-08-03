using HPD.Agent;
using Microsoft.Extensions.AI.Evaluation;

namespace HPD.Agent.Evaluations;

/// <summary>Evaluation policy and judge selection captured for one agent run.</summary>
public sealed class EvaluationRunConfig : IAgentRunEvaluationConfig
{
    private double? _samplingRate;

    /// <summary>Gets or sets whether live evaluation is enabled for this run.</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Gets or sets a sampling override from zero through one.</summary>
    public double? SamplingRate
    {
        get => _samplingRate;
        set
        {
            if (value is < 0 or > 1)
                throw new ArgumentOutOfRangeException(nameof(value), value, "Sampling rate must be between zero and one.");
            _samplingRate = value;
        }
    }

    /// <summary>Gets or sets evaluator instances added for this run.</summary>
    public IReadOnlyList<IEvaluator>? AdditionalEvaluators { get; set; }

    /// <summary>Gets or sets the judge configuration used for this run.</summary>
    public EvaluationJudgeRunConfig? Judge { get; set; }

    internal EvaluationSuppressionReason SuppressionReason { get; set; }

    /// <inheritdoc />
    public IAgentRunEvaluationConfig Snapshot() => new EvaluationRunConfig
    {
        Enabled = Enabled,
        SamplingRate = SamplingRate,
        AdditionalEvaluators = AdditionalEvaluators?.ToArray(),
        Judge = Judge?.Snapshot(),
        SuppressionReason = SuppressionReason
    };
}

internal enum EvaluationSuppressionReason
{
    None = 0,
    BatchExecution = 1,
    JudgeCall = 2
}
