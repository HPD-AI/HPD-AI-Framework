namespace HPD.Base.AspNetCore;

/// <summary>
/// Names HTTP headers used by the HPD.BASE ASP.NET Core projection.
/// </summary>
public static class BaseHttpHeaders
{
    /// <summary>Conditional revision header accepted by mutation routes.</summary>
    public const string IfMatch = "If-Match";

    /// <summary>Idempotency key header accepted by create routes.</summary>
    public const string IdempotencyKey = "Idempotency-Key";

    /// <summary>Correlation id header echoed by BASE endpoints.</summary>
    public const string CorrelationId = "X-Correlation-ID";

    /// <summary>Record or descriptor revision header emitted by BASE endpoints.</summary>
    public const string Revision = "HPD-Base-Revision";

    /// <summary>Compact event ids header emitted by mutation endpoints when available.</summary>
    public const string EventIds = "HPD-Base-Event-Ids";

    /// <summary>Preference tokens applied by the HTTP projection.</summary>
    public const string PreferenceApplied = "Preference-Applied";

    /// <summary>Retry hint emitted when a safe retry interval is known.</summary>
    public const string RetryAfter = "Retry-After";
}
