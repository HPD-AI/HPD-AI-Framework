namespace HPD.ML.TimeSeries;

using HPD.ML.Abstractions;

/// <summary>
/// Shared SSA training logic: materialize series → build trajectory matrix → SVD → AR coefficients.
/// Used by both SsaAnomalyLearner and SsaForecastingLearner.
/// </summary>
internal static class SsaTrainer
{
    public static double[] MaterializeSeries(IDataHandle data, string inputColumn)
    {
        var values = new List<double>();
        var allColumns = data.Schema.Columns.Select(c => c.Name);
        using var cursor = data.GetCursor(allColumns);
        while (cursor.MoveNext())
            values.Add(cursor.Current.GetValue<float>(inputColumn));
        return values.ToArray();
    }

    public static SsaModelParameters Train(
        double[] series,
        int windowSize,
        int seriesLength,
        int rank,
        RankSelectionMethod rankSelection)
    {
        int n = seriesLength > 0 ? Math.Min(seriesLength, series.Length) : series.Length;
        int L = windowSize;

        if (n < L + 1)
            throw new ArgumentException($"Series length ({n}) must be at least windowSize + 1 ({L + 1}).");

        // Build trajectory (Hankel) matrix: L × K where K = N - L + 1
        int K = n - L + 1;
        var trajectory = new double[L * K]; // column-major
        for (int j = 0; j < K; j++)
            for (int i = 0; i < L; i++)
                trajectory[j * L + i] = series[i + j];

        // SVD via covariance method: eigenvectors of (1/K) * X * X^T
        var cov = new double[L * L];
        for (int i = 0; i < L; i++)
            for (int j = 0; j <= i; j++)
            {
                double sum = 0;
                for (int k = 0; k < K; k++)
                    sum += trajectory[k * L + i] * trajectory[k * L + j];
                cov[i * L + j] = sum / K;
                cov[j * L + i] = cov[i * L + j];
            }

        var (eigenvalues, eigenvectors) = EigenDecompose(cov, L);

        // Select rank
        int selectedRank = rank > 0 ? rank : SelectRank(eigenvalues, rankSelection);

        // Compute AR coefficients from top-rank eigenvectors
        double denominator = 1.0;
        for (int k = 0; k < selectedRank; k++)
        {
            double lastComponent = eigenvectors[k * L + (L - 1)];
            denominator -= lastComponent * lastComponent;
        }
        denominator = Math.Max(denominator, 1e-10);

        var alpha = new double[L - 1];
        for (int j = 0; j < L - 1; j++)
        {
            double sum = 0;
            for (int k = 0; k < selectedRank; k++)
                sum += eigenvectors[k * L + (L - 1)] * eigenvectors[k * L + j];
            alpha[j] = sum / denominator;
        }

        // Initial state vector
        var lastWindow = new double[L];
        Array.Copy(series, n - L, lastWindow, 0, L);
        var stateVec = new double[selectedRank];
        for (int k = 0; k < selectedRank; k++)
        {
            double dot = 0;
            for (int i = 0; i < L; i++)
                dot += eigenvectors[k * L + i] * lastWindow[i];
            stateVec[k] = dot;
        }

        var singularVals = new double[selectedRank];
        for (int k = 0; k < selectedRank; k++)
            singularVals[k] = Math.Sqrt(Math.Max(eigenvalues[k], 0));

        var eigenvectorSubset = new double[selectedRank * L];
        Array.Copy(eigenvectors, 0, eigenvectorSubset, 0, selectedRank * L);

        return new SsaModelParameters(L, n, selectedRank, alpha, eigenvectorSubset, singularVals, stateVec);
    }

    private static int SelectRank(double[] eigenvalues, RankSelectionMethod method)
    {
        if (method == RankSelectionMethod.Fixed)
            return Math.Min(10, eigenvalues.Length);

        // Exact: keep components explaining >1/(2L) of total variance
        double total = eigenvalues.Sum();
        if (total < 1e-10) return 1;
        double threshold = total / (2 * eigenvalues.Length);
        int rank = 0;
        for (int i = 0; i < eigenvalues.Length; i++)
        {
            if (eigenvalues[i] > threshold) rank++;
            else break;
        }
        return Math.Max(rank, 1);
    }

    internal static (double[] eigenvalues, double[] eigenvectors) EigenDecompose(double[] cov, int n)
    {
        var eigenvalues = new double[n];
        var eigenvectors = new double[n * n];

        for (int i = 0; i < n; i++)
            eigenvectors[i * n + i] = 1.0;

        var A = (double[])cov.Clone();

        // Jacobi rotations
        for (int sweep = 0; sweep < 100; sweep++)
        {
            double offDiag = 0;
            for (int i = 0; i < n; i++)
                for (int j = i + 1; j < n; j++)
                    offDiag += A[i * n + j] * A[i * n + j];

            if (offDiag < 1e-20) break;

            for (int p = 0; p < n - 1; p++)
                for (int q = p + 1; q < n; q++)
                {
                    if (Math.Abs(A[p * n + q]) < 1e-15) continue;

                    double tau = (A[q * n + q] - A[p * n + p]) / (2 * A[p * n + q]);
                    double t = Math.Sign(tau) / (Math.Abs(tau) + Math.Sqrt(1 + tau * tau));
                    double c = 1.0 / Math.Sqrt(1 + t * t);
                    double s = t * c;

                    double app = A[p * n + p], aqq = A[q * n + q], apq = A[p * n + q];
                    A[p * n + p] = c * c * app - 2 * s * c * apq + s * s * aqq;
                    A[q * n + q] = s * s * app + 2 * s * c * apq + c * c * aqq;
                    A[p * n + q] = 0;
                    A[q * n + p] = 0;

                    for (int i = 0; i < n; i++)
                    {
                        if (i == p || i == q) continue;
                        double aip = A[i * n + p], aiq = A[i * n + q];
                        A[i * n + p] = c * aip - s * aiq;
                        A[p * n + i] = A[i * n + p];
                        A[i * n + q] = s * aip + c * aiq;
                        A[q * n + i] = A[i * n + q];
                    }

                    for (int i = 0; i < n; i++)
                    {
                        double vip = eigenvectors[p * n + i], viq = eigenvectors[q * n + i];
                        eigenvectors[p * n + i] = c * vip - s * viq;
                        eigenvectors[q * n + i] = s * vip + c * viq;
                    }
                }
        }

        for (int i = 0; i < n; i++)
            eigenvalues[i] = A[i * n + i];

        // Sort by eigenvalue descending
        var indices = Enumerable.Range(0, n).OrderByDescending(i => eigenvalues[i]).ToArray();
        var sortedEvals = indices.Select(i => eigenvalues[i]).ToArray();
        var sortedEvecs = new double[n * n];
        for (int k = 0; k < n; k++)
            for (int i = 0; i < n; i++)
                sortedEvecs[k * n + i] = eigenvectors[indices[k] * n + i];

        return (sortedEvals, sortedEvecs);
    }
}
