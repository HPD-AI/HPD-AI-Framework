using Microsoft.Extensions.Logging;

namespace HPD.Base.Auth.HPDAuth.AspNetCore.Observability.Logging;

internal static partial class HPDBaseHPDAuthAspNetCoreLog
{
    [LoggerMessage(EventId = 6500, Level = LogLevel.Warning, EventName = "HostIntegrationUnavailable",
        Message = "The HPD.Auth ASP.NET host integration is unavailable ({DiagnosticCode}).")]
    public static partial void HostIntegrationUnavailable(ILogger logger, string diagnosticCode);

    [LoggerMessage(EventId = 6502, Level = LogLevel.Warning, EventName = "PrincipalEnrichmentFailed",
        Message = "HPD.Auth principal enrichment failed ({ErrorCategory}, {ErrorCode}).")]
    public static partial void PrincipalEnrichmentFailed(ILogger logger, string errorCategory, string errorCode);
}
