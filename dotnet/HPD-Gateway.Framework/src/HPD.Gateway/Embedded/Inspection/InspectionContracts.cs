
namespace HPD.Gateway;

public enum GatewayInspectionCompleteness : byte
{
    NoBody = 0,
    PrefixOnly = 1,
    CompleteBody = 2
}

public enum GatewayInspectionDisposition : byte
{
    Allowed = 0,
    Rejected = 1
}

public sealed class GatewayInspectionContext
{
    internal GatewayInspectionContext(Stream body, GatewayInspectionCompleteness completeness, long observedBytes, bool contentEncoded)
    {
        Body = body;
        Completeness = completeness;
        ObservedBytes = observedBytes;
        ContentEncoded = contentEncoded;
    }

    public Stream Body { get; }
    public GatewayInspectionCompleteness Completeness { get; }
    public long ObservedBytes { get; }
    public bool ContentEncoded { get; }
}

public readonly record struct GatewayInspectionDecision
{
    private GatewayInspectionDecision(GatewayInspectionDisposition disposition, int statusCode, string? reasonCode)
    {
        Disposition = disposition;
        StatusCode = statusCode;
        ReasonCode = reasonCode;
    }

    public GatewayInspectionDisposition Disposition { get; }
    public int StatusCode { get; }
    public string? ReasonCode { get; }

    public static GatewayInspectionDecision Allow() => new(GatewayInspectionDisposition.Allowed, 0, null);

    public static GatewayInspectionDecision Reject(string reasonCode, int statusCode = 422)
    {
        if (!GatewayIdentifier.IsCanonical(reasonCode)) throw new ArgumentException("Inspection reason code must be a canonical identifier.", nameof(reasonCode));
        if (statusCode is not (400 or 403 or 415 or 422)) throw new ArgumentOutOfRangeException(nameof(statusCode), "Inspection rejection status must be 400, 403, 415, or 422.");
        return new(GatewayInspectionDisposition.Rejected, statusCode, reasonCode);
    }
}

public interface IGatewayRequestInspector
{
    ValueTask<GatewayInspectionDecision> InspectAsync(GatewayInspectionContext context, CancellationToken cancellationToken);
}

public interface IGatewayInspectionFeature
{
    RequestInspectionMode Mode { get; }
    GatewayInspectionCompleteness Completeness { get; }
    long ObservedBytes { get; }
    GatewayInspectionDisposition Disposition { get; }
    string? ReasonCode { get; }
}

internal sealed record GatewayInspectionOutcome(
    RequestInspectionMode Mode,
    GatewayInspectionCompleteness Completeness,
    long ObservedBytes,
    GatewayInspectionDisposition Disposition,
    string? ReasonCode) : IGatewayInspectionFeature;

internal sealed record GatewayInspectionSelection(
    string InspectorName,
    RequestInspectionMode Mode,
    long MaximumAcceptedBodyBytes,
    int? MaximumInspectedBytes,
    int? MemoryThresholdBytes,
    RequestInspectionSpillPolicy SpillPolicy);
