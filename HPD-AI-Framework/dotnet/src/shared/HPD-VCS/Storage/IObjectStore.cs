using System.Threading.Tasks;
using HPD.VCS.Core;

namespace HPD.VCS.Storage;

/// <summary>
/// Interface for object storage operations in the VCS system.
/// Provides content-addressable storage for immutable data objects.
/// </summary>
public interface IObjectStore : IDisposable
{    /// <summary>
    /// Writes file content data to the object store and returns its content-addressable ID.
    /// </summary>
    /// <param name="data">The file content data to store</param>
    /// <returns>The content-addressable ID of the stored data</returns>
    /// <exception cref="ObjectStoreException">Thrown when the write operation fails</exception>
    Task<FileContentId> WriteFileContentAsync(FileContentData data);

    /// <summary>
    /// Reads file content data from the object store using its content-addressable ID.
    /// </summary>
    /// <param name="id">The content-addressable ID of the file content to retrieve</param>
    /// <returns>The file content data, or null if not found</returns>
    /// <exception cref="ObjectTypeMismatchException">Thrown when the object exists but has a different type than expected</exception>
    /// <exception cref="CorruptObjectException">Thrown when the object data is corrupted and cannot be parsed</exception>
    Task<FileContentData?> ReadFileContentAsync(FileContentId id);

    /// <summary>
    /// Writes tree data to the object store and returns its content-addressable ID.
    /// </summary>
    /// <param name="data">The tree data to store</param>
    /// <returns>The content-addressable ID of the stored data</returns>
    /// <exception cref="ObjectStoreException">Thrown when the write operation fails</exception>
    Task<TreeId> WriteTreeAsync(TreeData data);
      /// <summary>
    /// Reads tree data from the object store using its content-addressable ID.
    /// </summary>
    /// <param name="id">The content-addressable ID of the tree to retrieve</param>
    /// <returns>The tree data, or null if not found</returns>
    /// <exception cref="ObjectTypeMismatchException">Thrown when the object exists but has a different type than expected</exception>
    /// <exception cref="CorruptObjectException">Thrown when the object data is corrupted and cannot be parsed</exception>
    Task<TreeData?> ReadTreeAsync(TreeId id);

    /// <summary>
    /// Writes commit data to the object store and returns its content-addressable ID.
    /// </summary>
    /// <param name="data">The commit data to store</param>
    /// <returns>The content-addressable ID of the stored data</returns>
    /// <exception cref="ObjectStoreException">Thrown when the write operation fails</exception>
    Task<CommitId> WriteCommitAsync(CommitData data);    /// <summary>
    /// Reads commit data from the object store using its content-addressable ID.
    /// </summary>
    /// <param name="id">The content-addressable ID of the commit to retrieve</param>
    /// <returns>The commit data, or null if not found</returns>
    /// <exception cref="ObjectTypeMismatchException">Thrown when the object exists but has a different type than expected</exception>
    /// <exception cref="CorruptObjectException">Thrown when the object data is corrupted and cannot be parsed</exception>
    Task<CommitData?> ReadCommitAsync(CommitId id);

    /// <summary>
    /// Writes conflict data to the object store and returns its content-addressable ID.
    /// </summary>
    /// <param name="data">The conflict data to store</param>
    /// <returns>The content-addressable ID of the stored data</returns>
    /// <exception cref="ObjectStoreException">Thrown when the write operation fails</exception>
    Task<ConflictId> WriteConflictAsync(ConflictData data);

    /// <summary>
    /// Reads conflict data from the object store using its content-addressable ID.
    /// </summary>
    /// <param name="id">The content-addressable ID of the conflict to retrieve</param>
    /// <returns>The conflict data, or null if not found</returns>
    /// <exception cref="ObjectTypeMismatchException">Thrown when the object exists but has a different type than expected</exception>
    /// <exception cref="CorruptObjectException">Thrown when the object data is corrupted and cannot be parsed</exception>
    Task<ConflictData?> ReadConflictAsync(ConflictId id);
}
