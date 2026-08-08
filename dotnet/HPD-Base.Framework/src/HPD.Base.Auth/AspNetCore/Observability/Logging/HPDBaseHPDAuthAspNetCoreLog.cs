using Microsoft.Extensions.Logging;

namespace HPD.Base.Auth;

internal static partial class HPDBaseHPDAuthAspNetCoreLog
{
    /// <summary>Executes the principal enrichment failed operation.</summary>
    [LoggerMessage(EventId = 6502, Level = LogLevel.Warning, EventName = "PrincipalEnrichmentFailed",
        Message = "HPD.Auth principal enrichment failed ({ErrorCategory}, {ErrorCode}).")]
    public static partial void PrincipalEnrichmentFailed(ILogger logger, string errorCategory, string errorCode);
}
