using System;
using System.Security.Cryptography;
using System.Text;

namespace HPD.VCS.Core;

/// <summary>
/// Interface for objects that can provide their canonical byte representation for content hashing.
/// Implementing types must ensure that objects with the same logical content always produce
/// the same byte sequence, enabling content-addressable storage.
/// </summary>
public interface IContentHashable
{
    /// <summary>
    /// Returns the canonical byte representation of the object for hashing purposes.
    /// This method must be deterministic - the same logical content must always produce
    /// the same byte sequence, regardless of object creation order or internal state.
    /// </summary>
    /// <returns>Canonical byte representation suitable for content hashing</returns>
    byte[] GetBytesForHashing();
}

/// <summary>
/// Static utility class for computing content-addressable object IDs.
/// Provides type-prefixed hashing similar to Git's object model, where different
/// object types (commits, trees, blobs) are hashed with type prefixes to prevent
/// hash collisions between different object types with identical content.
/// </summary>
public static class ObjectHasher
{
    /// <summary>
    /// Type prefix for commit objects (borrowed from Git's object model)
    /// </summary>
    public const string CommitTypePrefix = "commit\0";

    /// <summary>
    /// Type prefix for tree (directory) objects (borrowed from Git's object model)
    /// </summary>
    public const string TreeTypePrefix = "tree\0";

    /// <summary>
    /// Type prefix for blob (file content) objects (borrowed from Git's object model)
    /// </summary>
    public const string BlobTypePrefix = "blob\0";

    /// <summary>
    /// Type prefix for change tracking objects (jj-specific concept)
    /// </summary>
    public const string ChangeTypePrefix = "change\0";

    /// <summary>
    /// Type prefix for view objects (repository state snapshots)
    /// </summary>
    public const string ViewTypePrefix = "view\0";    /// <summary>
    /// Type prefix for operation objects (repository modification records)
    /// </summary>
    public const string OperationTypePrefix = "operation\0";

    /// <summary>
    /// Type prefix for conflict objects (tree-level conflicts)
    /// </summary>
    public const string ConflictTypePrefix = "conflict\0";/// <summary>
    /// Computes a content-addressable ID for the given data object.
    /// The computation follows Git's object model: type prefix + content bytes are hashed together.
    /// This ensures that different object types with identical content will have different IDs.
    /// 
    /// Performance: Uses IncrementalHash to avoid byte array concatenation and memory allocations.
    /// The type prefix includes a null terminator to guarantee separation between prefix and content.
    /// </summary>
    /// <typeparam name="TData">The data type that implements IContentHashable</typeparam>
    /// <typeparam name="TId">The target object ID type</typeparam>
    /// <param name="data">The data object to hash</param>
    /// <param name="typePrefix">The type prefix to prepend (must include null terminator for separation)</param>
    /// <returns>A new object ID of type TId containing the computed hash</returns>
    /// <exception cref="ArgumentNullException">Thrown when data or typePrefix is null</exception>
    public static TId ComputeId<TData, TId>(TData data, string typePrefix) 
        where TData : IContentHashable 
        where TId : IObjectId, new()
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(typePrefix);

        // Use IncrementalHash to avoid memory allocations from concatenation
        using var hasher = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        
        // Hash the type prefix bytes (UTF-8 encoded)
        // The null terminator in the prefix guarantees separation between prefix and content
        var prefixBytes = Encoding.UTF8.GetBytes(typePrefix);
        hasher.AppendData(prefixBytes);
        
        // Hash the content bytes directly without creating intermediate arrays
        var contentBytes = data.GetBytesForHashing();
        hasher.AppendData(contentBytes);
        
        // Get the final hash
        var hashBytes = hasher.GetHashAndReset();
        
        // Create and return the object ID
        var objectId = new TId();
        return objectId.WithHashValue<TId>(hashBytes);
    }

    /// <summary>
    /// Convenience method to compute a CommitId from commit data
    /// </summary>
    /// <typeparam name="TData">The commit data type</typeparam>
    /// <param name="commitData">The commit data to hash</param>
    /// <returns>A CommitId for the given commit data</returns>
    public static CommitId ComputeCommitId<TData>(TData commitData) where TData : IContentHashable
    {
        return ComputeId<TData, CommitId>(commitData, CommitTypePrefix);
    }

    /// <summary>
    /// Convenience method to compute a TreeId from tree data
    /// </summary>
    /// <typeparam name="TData">The tree data type</typeparam>
    /// <param name="treeData">The tree data to hash</param>
    /// <returns>A TreeId for the given tree data</returns>
    public static TreeId ComputeTreeId<TData>(TData treeData) where TData : IContentHashable
    {
        return ComputeId<TData, TreeId>(treeData, TreeTypePrefix);
    }

    /// <summary>
    /// Convenience method to compute a FileContentId from file content data
    /// </summary>
    /// <typeparam name="TData">The file content data type</typeparam>
    /// <param name="fileData">The file content data to hash</param>
    /// <returns>A FileContentId for the given file data</returns>
    public static FileContentId ComputeFileContentId<TData>(TData fileData) where TData : IContentHashable
    {
        return ComputeId<TData, FileContentId>(fileData, BlobTypePrefix);
    }    /// <summary>
    /// Convenience method to compute a ViewId from view data
    /// </summary>
    /// <typeparam name="TData">The view data type</typeparam>
    /// <param name="viewData">The view data to hash</param>
    /// <returns>A ViewId for the given view data</returns>
    public static ViewId ComputeViewId<TData>(TData viewData) where TData : IContentHashable
    {
        return ComputeId<TData, ViewId>(viewData, ViewTypePrefix);
    }    /// <summary>
    /// Convenience method to compute an OperationId from operation data
    /// </summary>
    /// <typeparam name="TData">The operation data type</typeparam>
    /// <param name="operationData">The operation data to hash</param>
    /// <returns>An OperationId for the given operation data</returns>
    public static OperationId ComputeOperationId<TData>(TData operationData) where TData : IContentHashable
    {
        return ComputeId<TData, OperationId>(operationData, OperationTypePrefix);
    }

    /// <summary>
    /// Convenience method to compute a ConflictId from conflict data
    /// </summary>
    /// <typeparam name="TData">The conflict data type</typeparam>
    /// <param name="conflictData">The conflict data to hash</param>
    /// <returns>A ConflictId for the given conflict data</returns>
    public static ConflictId ComputeConflictId<TData>(TData conflictData) where TData : IContentHashable
    {
        return ComputeId<TData, ConflictId>(conflictData, ConflictTypePrefix);
    }
}
