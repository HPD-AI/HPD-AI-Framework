namespace HPD.ML.LightGBM;

using System.Runtime.InteropServices;
using HPD.ML.Abstractions;
using HPD.ML.Core;
using HPD.ML.LightGBM.Native;

/// <summary>
/// LightGBM gradient boosted decision tree learner.
/// Supports binary classification, multiclass, regression, and ranking
/// via a single class with configurable objective.
/// </summary>
public sealed class LightGbmLearner : ILearner
{
    private readonly string _labelColumn;
    private readonly string _featureColumn;
    private readonly LightGbmOptions _options;
    private readonly ProgressSubject _progress = new();

    public LightGbmLearner(
        string labelColumn = "Label",
        string featureColumn = "Features",
        LightGbmOptions? options = null)
    {
        _labelColumn = labelColumn;
        _featureColumn = featureColumn;
        _options = options ?? new LightGbmOptions();
    }

    public IObservable<ProgressEvent> Progress => _progress;

    public ISchema GetOutputSchema(ISchema inputSchema)
    {
        // Create a dummy ensemble to get the output schema shape
        var mode = ObjectiveToOutputMode(_options.Objective);
        var dummyEnsemble = new TreeEnsemble([], 0, _options.NumberOfClasses ?? 1);
        var transform = new TreeEnsembleScoringTransform(
            dummyEnsemble, _featureColumn, mode, _options.NumberOfClasses ?? 2);
        return transform.GetOutputSchema(inputSchema);
    }

    public IModel Fit(LearnerInput input)
    {
        var paramString = _options.ToParameterString();

        // ── Build native datasets ──
        using var trainDataset = DatasetBuilder.Build(
            input.TrainData, _featureColumn, _labelColumn,
            weightColumn: null,
            groupColumn: _options.Objective == LightGbmObjective.Ranking ? "GroupId" : null,
            paramString);

        SafeDatasetHandle? validDataset = null;
        if (input.ValidationData is not null)
        {
            validDataset = DatasetBuilder.Build(
                input.ValidationData, _featureColumn, _labelColumn,
                weightColumn: null,
                groupColumn: _options.Objective == LightGbmObjective.Ranking ? "GroupId" : null,
                paramString,
                reference: trainDataset);
        }

        try
        {
            // ── Create booster ──
            NativeHelper.Check(LightGbmApi.BoosterCreate(
                trainDataset.Handle, paramString, out var boosterHandle));

            using var booster = new SafeBoosterHandle(boosterHandle);

            if (validDataset is not null)
                NativeHelper.Check(LightGbmApi.BoosterAddValidData(booster.Handle, validDataset.Handle));

            // ── Training loop ──
            double bestScore = double.MaxValue;
            int noImprovementCount = 0;

            for (int iter = 0; iter < _options.NumberOfIterations; iter++)
            {
                NativeHelper.Check(LightGbmApi.BoosterUpdateOneIter(booster.Handle, out int isFinished));

                // Get eval metrics
                double evalScore = GetEvalScore(booster, validDataset is not null ? 1 : 0);

                _progress.OnNext(new ProgressEvent
                {
                    Epoch = iter,
                    MetricValue = evalScore,
                    MetricName = "Eval"
                });

                // Early stopping
                if (_options.EarlyStoppingRounds > 0 && validDataset is not null)
                {
                    if (evalScore < bestScore)
                    {
                        bestScore = evalScore;
                        noImprovementCount = 0;
                    }
                    else
                    {
                        noImprovementCount++;
                        if (noImprovementCount >= _options.EarlyStoppingRounds)
                            break;
                    }
                }

                if (isFinished != 0)
                    break;
            }

            // ── Export model ──
            string modelString = NativeHelper.GetModelString(booster);
            var ensemble = TreeModelParser.Parse(modelString);

            double[]? importance = null;
            try { importance = NativeHelper.GetFeatureImportance(booster); }
            catch { /* Feature importance is optional */ }

            var parameters = new TreeEnsembleParameters(ensemble, importance);
            var mode = ObjectiveToOutputMode(_options.Objective);
            var transform = new TreeEnsembleScoringTransform(
                ensemble, _featureColumn, mode, _options.NumberOfClasses ?? 2);

            _progress.OnCompleted();
            return new Model(transform, parameters);
        }
        finally
        {
            validDataset?.Dispose();
        }
    }

    public Task<IModel> FitAsync(LearnerInput input, CancellationToken ct = default)
        => Task.Run(() => Fit(input), ct);

    private static double GetEvalScore(SafeBoosterHandle booster, int dataIdx)
    {
        NativeHelper.Check(LightGbmApi.BoosterGetEvalCounts(booster.Handle, out int evalCount));
        if (evalCount == 0) return 0;

        var results = new double[evalCount];
        var pinned = GCHandle.Alloc(results, GCHandleType.Pinned);
        try
        {
            NativeHelper.Check(LightGbmApi.BoosterGetEval(
                booster.Handle, dataIdx, out _, pinned.AddrOfPinnedObject()));
            return results[0];
        }
        finally { pinned.Free(); }
    }

    private static TreeEnsembleScoringTransform.OutputMode ObjectiveToOutputMode(LightGbmObjective objective)
        => objective switch
        {
            LightGbmObjective.Binary => TreeEnsembleScoringTransform.OutputMode.BinaryClassification,
            LightGbmObjective.Multiclass => TreeEnsembleScoringTransform.OutputMode.Multiclass,
            _ => TreeEnsembleScoringTransform.OutputMode.Regression
        };
}
