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

        foreach (var line in File.ReadLines(path).Skip(1)) // Skip header
        {
            var parts = line.Split(',');
            if (parts.Length < 3) continue;

            long reqTs = long.Parse(parts[0].Trim());
            long exchTs = long.Parse(parts[1].Trim());
            long respTs = long.Parse(parts[2].Trim());

            measurements.Add(new LatencyMeasurement(
                EntryLatencyNanos: exchTs - reqTs,
                ResponseLatencyNanos: respTs - exchTs
            ));
        }

        return measurements;
    }
}
