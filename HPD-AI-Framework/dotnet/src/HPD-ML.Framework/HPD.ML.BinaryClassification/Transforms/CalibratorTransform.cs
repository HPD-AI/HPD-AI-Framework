namespace HPD.ML.BinaryClassification;

using HPD.ML.Abstractions;
using HPD.ML.Core;

/// <summary>
/// Platt scaling: learns sigmoid parameters A, B to map raw scores to calibrated probabilities.
/// P(y=1|score) = 1 / (1 + exp(A*score + B))
/// Used by SVM and Perceptron which don't produce calibrated outputs natively.
/// </summary>
public sealed class CalibratorTransform : ITransform
{
    private readonly double _a;
    private readonly double _b;
    private readonly string _scoreColumn;

    public CalibratorTransform(double a, double b, string scoreColumn = "Score")
    {
        _a = a;
        _b = b;
        _scoreColumn = scoreColumn;
    }

    public TransformProperties Properties => new() { PreservesRowCount = true };

    public ISchema GetOutputSchema(ISchema inputSchema) => inputSchema;

    public IDataHandle Apply(IDataHandle input)
    {
        return new CursorDataHandle(
            input.Schema,
            columns => new MappedCursor(
                input.GetCursor(columns),
                row =>
                {
                    var values = new Dictionary<string, object>();
                    foreach (var col in input.Schema.Columns)
                    {
                        if (col.Name == "Probability")
                        {
                            float score = row.GetValue<float>(_scoreColumn);
                            float calibrated = (float)(1.0 / (1.0 + Math.Exp(_a * score + _b)));
                            values["Probability"] = calibrated;
                        }
                        else
                        {
                            values[col.Name] = row.GetValue<object>(col.Name);
                        }
                    }
                    return new DictionaryRow(input.Schema, values);
                }),
            input.RowCount,
            input.Ordering);
    }

    /// <summary>
    /// Fit Platt scaling parameters from scores and labels.
    /// </summary>
    public static CalibratorTransform Fit(IDataHandle scoredData, string labelColumn = "Label")
    {
        var scores = new List<double>();
        var labels = new List<bool>();

        using var cursor = scoredData.GetCursor(["Score", labelColumn]);
        while (cursor.MoveNext())
        {
            scores.Add(cursor.Current.GetValue<float>("Score"));
            labels.Add(Convert.ToBoolean(cursor.Current.GetValue<object>(labelColumn)));
        }

        // Platt scaling via Newton's method on log-likelihood
        int n = scores.Count;
        int nPos = labels.Count(l => l);
        int nNeg = n - nPos;

        // Target probabilities with Bayesian correction
        double hiTarget = (nPos + 1.0) / (nPos + 2.0);
        double loTarget = 1.0 / (nNeg + 2.0);

        double a = 0, b = Math.Log((nNeg + 1.0) / (nPos + 1.0));

        for (int iter = 0; iter < 100; iter++)
        {
            double dA = 0, dB = 0, d2A = 0, d2B = 0, d2AB = 0;

            for (int i = 0; i < n; i++)
            {
                double target = labels[i] ? hiTarget : loTarget;
                double p = 1.0 / (1.0 + Math.Exp(a * scores[i] + b));
                double d = p - target;

                dA += scores[i] * d;
                dB += d;
                double pq = p * (1 - p);
                d2A += scores[i] * scores[i] * pq;
                d2B += pq;
                d2AB += scores[i] * pq;
            }

            double det = d2A * d2B - d2AB * d2AB;
            if (Math.Abs(det) < 1e-15) break;

            a -= (d2B * dA - d2AB * dB) / det;
            b -= (d2A * dB - d2AB * dA) / det;
        }

        return new CalibratorTransform(a, b);
    }
}
