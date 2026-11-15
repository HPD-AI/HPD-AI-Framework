namespace HPD.Agent;

/// <summary>Portable retry, loop protection, and function timeout policy.</summary>
public sealed class ResilienceConfig
{
    /// <summary>Requests provider-aware retry handling. Null disables retry middleware.</summary>
    public RetryPolicySection? Retry { get; set; }

    /// <summary>Requests repeated-call circuit-breaker protection.</summary>
    public CircuitBreakerPolicy? CircuitBreaker { get; set; }

    /// <summary>Requests consecutive-error tracking.</summary>
    public ConsecutiveErrorPolicy? ConsecutiveErrors { get; set; }

    /// <summary>Requests total-error tracking.</summary>
    public TotalErrorPolicy? TotalErrors { get; set; }

    /// <summary>Requests a per-function execution timeout.</summary>
    public TimeSpan? FunctionTimeout { get; set; }
}

/// <summary>Portable retry scopes.</summary>
public sealed class RetryPolicySection
{
    /// <summary>Gets or sets model-call retry behavior.</summary>
    public ModelRetryConfig ModelRetries { get; set; } = new();

    /// <summary>Gets or sets function-call retry behavior.</summary>
    public FunctionRetryConfig FunctionRetries { get; set; } = new();
}

/// <summary>Portable repeated-call circuit-breaker policy.</summary>
public sealed class CircuitBreakerPolicy
{
    public int MaxConsecutiveCalls { get; set; } = 3;

    public string TerminationMessageTemplate { get; set; } =
        "Circuit breaker triggered: Function '{toolName}' with same arguments would be called {count} times consecutively. " +
        "Stopping to prevent infinite loop.";
}

/// <summary>Portable consecutive-error policy.</summary>
public sealed class ConsecutiveErrorPolicy
{
    public int MaxConsecutiveErrors { get; set; } = 3;

    public string TerminationMessageTemplate { get; set; } =
        "Maximum consecutive errors ({count}/{max}) exceeded. " +
        "Stopping execution to prevent infinite error loop.";
}

/// <summary>Portable total-error policy.</summary>
public sealed class TotalErrorPolicy
{
    public int MaxTotalErrors { get; set; } = 10;

    public string TerminationMessageTemplate { get; set; } =
        "Total error threshold ({totalErrors}/{maxErrors}) exceeded. " +
        "Encountered too many errors of varying types. Stopping execution.";
}

/// <summary>Portable privacy policy.</summary>
public sealed class PrivacyConfig
{
    /// <summary>Requests built-in and declarative-regex PII protection.</summary>
    public PIIProtectionPolicy? PII { get; set; }
}

/// <summary>Portable built-in PII detection policy.</summary>
public sealed class PIIProtectionPolicy
{
    public PIIStrategy EmailStrategy { get; set; } = PIIStrategy.Redact;
    public PIIStrategy CreditCardStrategy { get; set; } = PIIStrategy.Block;
    public PIIStrategy SSNStrategy { get; set; } = PIIStrategy.Block;
    public PIIStrategy PhoneStrategy { get; set; } = PIIStrategy.Mask;
    public PIIStrategy IPAddressStrategy { get; set; } = PIIStrategy.Hash;
    public bool ApplyToInput { get; set; } = true;
    public bool ApplyToOutput { get; set; }
    public bool ApplyToToolResults { get; set; }
    public List<DeclarativePIIDetector> CustomDetectors { get; set; } = [];
}

/// <summary>Portable bounded regular-expression PII detector.</summary>
public sealed class DeclarativePIIDetector
{
    public string Name { get; set; } = string.Empty;
    public string Pattern { get; set; } = string.Empty;
    public PIIStrategy Strategy { get; set; } = PIIStrategy.Redact;
    public string? Replacement { get; set; }
}
