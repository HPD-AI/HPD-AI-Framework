namespace HPD.ML.LightGBM;

using System.Globalization;
using System.Text;

/// <summary>
/// LightGBM training objective.
/// </summary>
public enum LightGbmObjective
{
    Binary,
    Multiclass,
    Regression,
    RegressionMae,
    Huber,
    Poisson,
    Tweedie,
    Ranking
}

/// <summary>
/// Tree growth / boosting strategy.
/// </summary>
public enum BoostingType
{
    Gbdt,
    RandomForest,
    Dart,
    Goss
}

/// <summary>
/// All LightGBM hyperparameters. Sensible defaults for most use cases.
/// </summary>
public sealed record LightGbmOptions
{
    // ── Task ──
    public LightGbmObjective Objective { get; init; } = LightGbmObjective.Regression;
    public int? NumberOfClasses { get; init; }

    // ── Boosting ──
    public BoostingType Boosting { get; init; } = BoostingType.Gbdt;
    public int NumberOfIterations { get; init; } = 100;
    public double LearningRate { get; init; } = 0.1;

    // ── Tree structure ──
    public int NumberOfLeaves { get; init; } = 31;
    public int MaxDepth { get; init; } = -1;
    public int MinDataInLeaf { get; init; } = 20;
    public double MinSumHessianInLeaf { get; init; } = 1e-3;

    // ── Regularization ──
    public double L1Regularization { get; init; } = 0;
    public double L2Regularization { get; init; } = 0;
    public double MaxDeltaStep { get; init; } = 0;

    // ── Feature & data sampling ──
    public double FeatureFraction { get; init; } = 1.0;
    public double BaggingFraction { get; init; } = 1.0;
    public int BaggingFrequency { get; init; } = 0;
    public int MaxBin { get; init; } = 255;

    // ── Categorical ──
    public int MaxCategoricalThreshold { get; init; } = 32;
    public double CategoricalSmoothing { get; init; } = 10;

    // ── Missing values ──
    public bool HandleMissingValue { get; init; } = true;
    public bool UseZeroAsMissing { get; init; } = false;

    // ── Early stopping ──
    public int EarlyStoppingRounds { get; init; } = 0;

    // ── Tweedie specific ──
    public double TweedieVariancePower { get; init; } = 1.5;

    // ── Misc ──
    public int? Seed { get; init; }
    public int NumberOfThreads { get; init; } = 0;
    public int Verbosity { get; init; } = -1;

    /// <summary>
    /// Convert to LightGBM parameter string for native API.
    /// All numeric values use InvariantCulture to avoid locale issues.
    /// </summary>
    internal string ToParameterString()
    {
        var ci = CultureInfo.InvariantCulture;
        var sb = new StringBuilder();

        sb.Append(ci, $"objective={ObjectiveToString()} ");
        sb.Append(ci, $"boosting_type={BoostingToString()} ");
        sb.Append(ci, $"num_iterations={NumberOfIterations} ");
        sb.Append(ci, $"learning_rate={LearningRate} ");
        sb.Append(ci, $"num_leaves={NumberOfLeaves} ");
        sb.Append(ci, $"max_depth={MaxDepth} ");
        sb.Append(ci, $"min_data_in_leaf={MinDataInLeaf} ");
        sb.Append(ci, $"min_sum_hessian_in_leaf={MinSumHessianInLeaf} ");
        sb.Append(ci, $"lambda_l1={L1Regularization} ");
        sb.Append(ci, $"lambda_l2={L2Regularization} ");
        sb.Append(ci, $"max_delta_step={MaxDeltaStep} ");
        sb.Append(ci, $"feature_fraction={FeatureFraction} ");
        sb.Append(ci, $"bagging_fraction={BaggingFraction} ");
        sb.Append(ci, $"bagging_freq={BaggingFrequency} ");
        sb.Append(ci, $"max_bin={MaxBin} ");
        sb.Append(ci, $"max_cat_threshold={MaxCategoricalThreshold} ");
        sb.Append(ci, $"cat_smooth={CategoricalSmoothing} ");
        sb.Append(ci, $"use_missing={HandleMissingValue.ToString().ToLowerInvariant()} ");
        sb.Append(ci, $"zero_as_missing={UseZeroAsMissing.ToString().ToLowerInvariant()} ");
        sb.Append(ci, $"seed={Seed ?? 0} ");
        sb.Append(ci, $"num_threads={NumberOfThreads} ");
        sb.Append(ci, $"verbosity={Verbosity} ");

        if (Objective == LightGbmObjective.Multiclass && NumberOfClasses.HasValue)
            sb.Append(ci, $"num_class={NumberOfClasses.Value} ");

        if (Objective == LightGbmObjective.Tweedie)
            sb.Append(ci, $"tweedie_variance_power={TweedieVariancePower} ");

        if (Objective == LightGbmObjective.Ranking)
            sb.Append("metric=ndcg ");

        return sb.ToString().TrimEnd();
    }

    private string ObjectiveToString() => Objective switch
    {
        LightGbmObjective.Binary => "binary",
        LightGbmObjective.Multiclass => "multiclass",
        LightGbmObjective.Regression => "regression",
        LightGbmObjective.RegressionMae => "regression_l1",
        LightGbmObjective.Huber => "huber",
        LightGbmObjective.Poisson => "poisson",
        LightGbmObjective.Tweedie => "tweedie",
        LightGbmObjective.Ranking => "lambdarank",
        _ => "regression"
    };

    private string BoostingToString() => Boosting switch
    {
        BoostingType.Gbdt => "gbdt",
        BoostingType.RandomForest => "rf",
        BoostingType.Dart => "dart",
        BoostingType.Goss => "goss",
        _ => "gbdt"
    };
}
