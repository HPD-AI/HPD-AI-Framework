using System.Threading.Tasks;
using HPD.VCS.Core;

namespace HPD.VCS.Storage;

/// <summary>
/// Interface for operation storage operations in the VCS system.
/// Provides content-addressable storage for ViewData and OperationData objects.
/// </summary>
public interface IOperationStore : IDisposable
{
    /// <summary>
    /// Writes view data to the operation store and returns its content-addressable ID.
    /// </summary>
    /// <param name="data">The view data to store</param>
    /// <returns>The content-addressable ID of the stored view data</returns>
    /// <exception cref="ObjectStoreException">Thrown when the write operation fails</exception>
    Task<ViewId> WriteViewAsync(ViewData data);

    /// <summary>
    /// Reads view data from the operation store using its content-addressable ID.
    /// </summary>
    /// <param name="id">The content-addressable ID of the view data to retrieve</param>
    /// <returns>The view data, or null if not found</returns>
    /// <exception cref="ObjectTypeMismatchException">Thrown when the object exists but has a different type than expected</exception>
    /// <exception cref="CorruptObjectException">Thrown when the object data is corrupted and cannot be parsed</exception>
    Task<ViewData?> ReadViewAsync(ViewId id);

    /// <summary>
    /// Writes operation data to the operation store and returns its content-addressable ID.
    /// </summary>
    /// <param name="data">The operation data to store</param>
    /// <returns>The content-addressable ID of the stored operation data</returns>
    /// <exception cref="ObjectStoreException">Thrown when the write operation fails</exception>
    Task<OperationId> WriteOperationAsync(OperationData data);

    /// <summary>
    /// Reads operation data from the operation store using its content-addressable ID.
    /// </summary>
    /// <param name="id">The content-addressable ID of the operation data to retrieve</param>
    /// <returns>The operation data, or null if not found</returns>
    /// <exception cref="ObjectTypeMismatchException">Thrown when the object exists but has a different type than expected</exception>
    /// <exception cref="CorruptObjectException">Thrown when the object data is corrupted and cannot be parsed</exception>
    Task<OperationData?> ReadOperationAsync(OperationId id);
}
