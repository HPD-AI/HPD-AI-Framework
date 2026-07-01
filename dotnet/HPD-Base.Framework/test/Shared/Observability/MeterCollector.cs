using System.Collections.Concurrent;
using System.Diagnostics.Metrics;

namespace HPD.Base.Tests.Observability;

internal sealed class MeterCollector : IDisposable
{
    private readonly MeterListener _listener = new();
    private readonly ConcurrentBag<string> _instrumentNames = new();

    public MeterCollector(string meterName)
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == meterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };
        _listener.SetMeasurementEventCallback<long>((instrument, _, _, _) => _instrumentNames.Add(instrument.Name));
        _listener.SetMeasurementEventCallback<double>((instrument, _, _, _) => _instrumentNames.Add(instrument.Name));
        _listener.Start();
    }

    public string[] InstrumentNames => _instrumentNames.ToArray();

    public void RecordObservableInstruments() => _listener.RecordObservableInstruments();

    public void Dispose() => _listener.Dispose();
}
