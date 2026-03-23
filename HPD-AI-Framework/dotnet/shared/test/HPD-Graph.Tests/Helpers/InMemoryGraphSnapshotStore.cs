// NOTE: InMemoryGraphSnapshotStore has been moved to HPD.Graph.Core.Caching.
// This file is kept for backward compatibility in tests but simply re-exports the Core implementation.
// Import directly from HPDAgent.Graph.Core.Caching namespace in new code.

namespace HPD.Graph.Tests.Helpers;

/// <summary>
/// Re-exports the Core implementation for test backward compatibility.
/// Use HPDAgent.Graph.Core.Caching.InMemoryGraphSnapshotStore directly in new code.
/// </summary>
public class InMemoryGraphSnapshotStore : HPDAgent.Graph.Core.Caching.InMemoryGraphSnapshotStore
{
}
