using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace HPD.Base;

internal static class HPDBaseRelationalTelemetry
{
    private static readonly Counter<long> RelationalAttempts = HPDBaseRuntimeObservability.Meter.CreateCounter<long>(HPDBaseTelemetryInstruments.RelationalAttempts);
    private static readonly Histogram<double> RelationalDuration = HPDBaseRuntimeObservability.Meter.CreateHistogram<double>(HPDBaseTelemetryInstruments.RelationalDuration, "s");
    private static readonly Counter<long> SchemaAttempts = HPDBaseRuntimeObservability.Meter.CreateCounter<long>(HPDBaseTelemetryInstruments.SchemaAttempts);
    private static readonly Histogram<double> SchemaDuration = HPDBaseRuntimeObservability.Meter.CreateHistogram<double>(HPDBaseTelemetryInstruments.SchemaDuration, "s");

    internal static Scope StartRelational(string span, string operation, int sources, int joins) =>
        new(span, operation, Bucket(sources), Bucket(joins), schema: false);

    internal static Scope StartSchema(string span, string operation) =>
        new(span, operation, null, null, schema: true);

    private static string Bucket(int value) => value switch { 0 => "0", 1 => "1", <= 3 => "2-3", <= 7 => "4-7", _ => "8+" };

internal sealed class Scope : IDisposable
    {
        private readonly Activity? _activity;
        private readonly long _started = Stopwatch.GetTimestamp();
        private readonly TagList _tags;
        private readonly bool _schema;

        internal Scope(string span, string operation, string? sources, string? joins, bool schema)
        {
            _schema = schema;
            _tags = new TagList { { HPDBaseTelemetryTags.OperationKind, operation } };
            if (sources is not null) _tags.Add(HPDBaseTelemetryTags.SourceCountBucket, sources);
            if (joins is not null) _tags.Add(HPDBaseTelemetryTags.JoinCountBucket, joins);
            _activity = HPDBaseRuntimeObservability.ActivitySource.StartActivity(span, ActivityKind.Internal);
            _activity?.SetTag(HPDBaseTelemetryTags.OperationKind, operation);
            _activity?.SetTag(HPDBaseTelemetryTags.SourceCountBucket, sources);
            _activity?.SetTag(HPDBaseTelemetryTags.JoinCountBucket, joins);
            if (schema) SchemaAttempts.Add(1, _tags); else RelationalAttempts.Add(1, _tags);
        }

        internal void SetOutcome(OperationStatus status)
        {
            string outcome = status.ToString().ToLowerInvariant();
            _activity?.SetTag(HPDBaseTelemetryTags.ResultStatus, outcome);
        }

        internal void SetClassification(BaseSchemaPlanClassification classification)
        {
            string value = classification.ToString().ToLowerInvariant();
            _activity?.SetTag(HPDBaseTelemetryTags.SchemaPlanClassification, value);
        }

        /// <summary>Executes the dispose operation.</summary>
        public void Dispose()
        {
            double seconds = Stopwatch.GetElapsedTime(_started).TotalSeconds;
            if (_schema) SchemaDuration.Record(seconds, _tags); else RelationalDuration.Record(seconds, _tags);
            _activity?.Dispose();
        }
    }
}
