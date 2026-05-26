using System.Globalization;
using Rhodium.Tensor;

namespace Rhodium.Simulation;

/// <summary>
/// Latency measurement from live trading data.
/// </summary>
public readonly record struct LatencyMeasurement(
    long EntryLatencyNanos,
    long ResponseLatencyNanos);

/// <summary>
/// Data-driven latency model using real measurements from CSV.
/// Provides more realistic latency simulation than synthetic distributions.
/// </summary>
public sealed class InterpolatedLatencyModel
{
    private readonly List<LatencyMeasurement> _measurements;

    public InterpolatedLatencyModel(string csvPath)
    {
        _measurements = LoadFromCsv(csvPath);
        if (_measurements.Count == 0)
            throw new ArgumentException("Latency CSV file is empty", nameof(csvPath));
    }

    /// <summary>
    /// Pre-sample latencies into tensor columns during initialization.
    /// Uses linear interpolation based on virtual index (deterministic).
    /// </summary>
    public void PreSampleIntoTensors(
        ITensorStore tensors,
        VectorField<FactorF64> entryLatencyField,
        VectorField<FactorF64> responseLatencyField,
        int startVirtualIndex,
        int count)
    {
        for (int i = 0; i < count; i++)
        {
            int vi = startVirtualIndex + i;

            // Deterministic sampling: map VI to measurement index
            int measurementIndex = vi % _measurements.Count;
            var measurement = _measurements[measurementIndex];

            tensors.GetScalar(entryLatencyField, vi) =
                new FactorF64(measurement.EntryLatencyNanos);
            tensors.GetScalar(responseLatencyField, vi) =
                new FactorF64(measurement.ResponseLatencyNanos);
        }
    }

    private static List<LatencyMeasurement> LoadFromCsv(string path)
    {
        var measurements = new List<LatencyMeasurement>();
        var isHeader = true;
        var lineNumber = 0;

        foreach (var line in File.ReadLines(path))
        {
            lineNumber++;
            if (isHeader)
            {
                isHeader = false;
                continue;
            }

            if (!TryParseLatencyRow(line.AsSpan(), out var reqTs, out var exchTs, out var respTs))
                throw new FormatException($"Latency CSV line {lineNumber} must contain three integer timestamp columns.");

            measurements.Add(new LatencyMeasurement(
                EntryLatencyNanos: exchTs - reqTs,
                ResponseLatencyNanos: respTs - exchTs
            ));
        }

        return measurements;
    }

    private static bool TryParseLatencyRow(
        ReadOnlySpan<char> row,
        out long requestTimestamp,
        out long exchangeTimestamp,
        out long responseTimestamp)
    {
        requestTimestamp = 0;
        exchangeTimestamp = 0;
        responseTimestamp = 0;

        if (!TryReadCsvField(ref row, out var request)
            || !TryReadCsvField(ref row, out var exchange)
            || !TryReadCsvField(ref row, out var response))
            return false;

        return long.TryParse(request, NumberStyles.Integer, CultureInfo.InvariantCulture, out requestTimestamp)
            && long.TryParse(exchange, NumberStyles.Integer, CultureInfo.InvariantCulture, out exchangeTimestamp)
            && long.TryParse(response, NumberStyles.Integer, CultureInfo.InvariantCulture, out responseTimestamp);
    }

    private static bool TryReadCsvField(ref ReadOnlySpan<char> row, out ReadOnlySpan<char> field)
    {
        if (row.Length == 0)
        {
            field = default;
            return false;
        }

        var comma = row.IndexOf(',');
        if (comma < 0)
        {
            field = row.Trim();
            row = [];
            return true;
        }

        field = row[..comma].Trim();
        row = row[(comma + 1)..];
        return true;
    }
}
