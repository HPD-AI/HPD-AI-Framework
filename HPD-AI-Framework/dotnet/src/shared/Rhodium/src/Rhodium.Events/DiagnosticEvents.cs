using Rhodium.Primitives;

namespace Rhodium.Events;

// ==================== DIAGNOSTIC EVENTS ====================

/// <summary>
/// Performance snapshot for monitoring.
/// Background priority - processed when idle.
/// </summary>
public sealed record PerformanceSnapshot(
    Money Equity,
    Money Cash,
    Money UnrealizedPnL,
    Money RealizedPnL,
    int OpenPositions,
    int OpenOrders
) : DiagnosticEvent;

/// <summary>
/// Latency measurement for monitoring.
/// </summary>
public sealed record LatencyMeasured(
    string Operation,
    Duration Latency
) : DiagnosticEvent;
