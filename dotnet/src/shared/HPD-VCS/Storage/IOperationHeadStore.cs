using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HPD.VCS.Core;

namespace HPD.VCS.Storage;

/// <summary>
/// Interface for managing the current head(s) of the operation log.
/// The operation head store tracks which operations are considered "current" or "latest".
/// </summary>
public interface IOperationHeadStore : IDisposable
{
    /// <summary>
    /// Gets the current head operation IDs.
    /// For hpd V1, this will typically be a single head, but the interface
    /// supports multiple heads for future distributed scenarios.
    /// </summary>
    /// <returns>List of operation IDs that are current heads. Empty if no heads exist.</returns>
    Task<IReadOnlyList<OperationId>> GetHeadOperationIdsAsync();

    /// <summary>
    /// Updates the head operation IDs atomically.
    /// For hpd V1 (single head focus), this provides basic Compare-And-Swap (CAS) semantics.
    /// </summary>
    /// <param name="oldExpectedHeadIds">The expected current head IDs. For CAS validation.</param>
    /// <param name="newHeadId">The new head ID to set</param>
    /// <exception cref="InvalidOperationException">Thrown when concurrent operation is detected (CAS failure)</exception>
    /// <exception cref="ObjectStoreException">Thrown when the update operation fails</exception>
    Task UpdateHeadOperationIdsAsync(IReadOnlyList<OperationId> oldExpectedHeadIds, OperationId newHeadId);
}
