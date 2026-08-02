using Microsoft.Extensions.Logging;

namespace HPD.Base.Auth;

internal static partial class HPDBaseHPDAuthLog
{
    /// <summary>Executes the auth services unavailable operation.</summary>
    [LoggerMessage(EventId = 6000, Level = LogLevel.Warning, EventName = "AuthServicesUnavailable",
        Message = "Required HPD.Auth integration services are unavailable ({DiagnosticCode}).")]
    public static partial void AuthServicesUnavailable(ILogger logger, string diagnosticCode);

    /// <summary>Executes the grant configuration missing operation.</summary>
    [LoggerMessage(EventId = 6001, Level = LogLevel.Warning, EventName = "GrantConfigurationMissing",
        Message = "Required HPD.Auth grant configuration is unavailable ({DiagnosticCode}).")]
    public static partial void GrantConfigurationMissing(ILogger logger, string diagnosticCode);

    /// <summary>Executes the grant provider failed operation.</summary>
    [LoggerMessage(EventId = 6002, Level = LogLevel.Warning, EventName = "GrantProviderFailed",
        Message = "An HPD.Auth grant provider failed ({ErrorCategory}, {ErrorCode}).")]
    public static partial void GrantProviderFailed(ILogger logger, string errorCategory, string errorCode);

    /// <summary>Executes the auth policy denied operation.</summary>
    [LoggerMessage(EventId = 6003, Level = LogLevel.Debug, EventName = "AuthPolicyDenied",
        Message = "HPD.Auth policy denied {OperationKind} ({PolicyReasonCode}).")]
    public static partial void AuthPolicyDenied(ILogger logger, string operationKind, string policyReasonCode);

    /// <summary>Executes the privileged bypass used operation.</summary>
    [LoggerMessage(EventId = 6004, Level = LogLevel.Debug, EventName = "PrivilegedBypassUsed",
        Message = "An HPD.Auth privileged bypass was applied ({BypassKind}).")]
    public static partial void PrivilegedBypassUsed(ILogger logger, string bypassKind);

    /// <summary>Executes the operation kind operation.</summary>
    public static string OperationKind(BaseOperationKind operation) => operation switch
    {
        BaseOperationKind.List => "list",
        BaseOperationKind.Query => "query",
        BaseOperationKind.Get => "get",
        BaseOperationKind.Create => "create",
        BaseOperationKind.Patch => "patch",
        BaseOperationKind.Replace => "replace",
        BaseOperationKind.Delete => "delete",
        BaseOperationKind.AdminInspect => "adminInspect",
        _ => "unknown"
    };

    /// <summary>Executes the policy reason code operation.</summary>
    public static string PolicyReasonCode(string? reasonCode) => reasonCode switch
    {
        "hpd.auth.base.missingAuthServices" => "missingAuthServices",
        "hpd.auth.base.unauthenticated" => "unauthenticated",
        "hpd.auth.base.noMatchingGrant" => "noMatchingGrant",
        "hpd.auth.base.grantDenied" => "grantDenied",
        _ => "unknown"
    };
}
