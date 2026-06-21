using System;
using System.IO;
using System.IO.Abstractions;
using System.Text;
using System.Threading.Tasks;
using HPD.VCS.Core;

namespace HPD.VCS.Storage;

/// <summary>
/// Filesystem-based implementation of the operation store interface.
/// Stores operations and views in a content-addressable manner using 2-character prefix sharding.
/// </summary>
public class FileSystemOperationStore : IOperationStore, IDisposable
{
    private readonly string _storeDirectoryPath;
    private readonly IFileSystem _fileSystem;

    /// <summary>
    /// Initializes a new instance of the FileSystemOperationStore class.
    /// </summary>
    /// <param name="storeDirectoryPath">The root directory path for storing operations and views (e.g., .hpd/op_store)</param>
    public FileSystemOperationStore(string storeDirectoryPath)
        : this(new FileSystem(), storeDirectoryPath)
    {
    }

    /// <summary>
    /// Initializes a new instance of the FileSystemOperationStore class with a custom file system.
    /// </summary>
    /// <param name="fileSystem">The file system abstraction to use</param>
    /// <param name="storeDirectoryPath">The root directory path for storing operations and views</param>
    public FileSystemOperationStore(IFileSystem fileSystem, string storeDirectoryPath)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _storeDirectoryPath = storeDirectoryPath ?? throw new ArgumentNullException(nameof(storeDirectoryPath));
        
        // Ensure the store directory exists
        _fileSystem.Directory.CreateDirectory(_storeDirectoryPath);
    }

    public void Dispose()
    {
        // Nothing to dispose for this implementation
    }

    /// <summary>
    /// Gets the file path for an object based on its ID using 2-character prefix sharding
    /// </summary>
    /// <param name="id">The object ID to convert</param>
    /// <returns>The full file path for the object</returns>
    private string GetObjectPath(IObjectId id)
    {
        var hexString = id.ToHexString();
        
        if (hexString.Length < 2)
        {
            throw new ArgumentException("Object ID hex string must be at least 2 characters long");
        }
        
        var prefix = hexString[..2];
        var suffix = hexString[2..];
        
        var prefixDir = _fileSystem.Path.Combine(_storeDirectoryPath, prefix);
        return _fileSystem.Path.Combine(prefixDir, suffix);
    }    /// <summary>
    /// Writes data to storage with the specified type prefix
    /// </summary>
    private async Task<TId> WriteObjectAsync<TData, TId>(TData data, string typePrefix) 
        where TData : IContentHashable
        where TId : IObjectId, new()
    {
        var canonicalBytes = data.GetBytesForHashing();
        var typePrefixBytes = Encoding.UTF8.GetBytes(typePrefix);
        
        // Combine type prefix with canonical bytes
        var contentWithPrefix = new byte[typePrefixBytes.Length + canonicalBytes.Length];
        Array.Copy(typePrefixBytes, 0, contentWithPrefix, 0, typePrefixBytes.Length);
        Array.Copy(canonicalBytes, 0, contentWithPrefix, typePrefixBytes.Length, canonicalBytes.Length);
        
        // Compute the object ID from the original data (with correct type)
        var objectId = ObjectHasher.ComputeId<TData, TId>(data, typePrefix);
        var objectPath = GetObjectPath(objectId);
        
        // Check if the object already exists (write-if-absent optimization)
        if (_fileSystem.File.Exists(objectPath))
        {
            return objectId;
        }
        
        // Ensure the directory exists
        var directory = _fileSystem.Path.GetDirectoryName(objectPath)!;
        _fileSystem.Directory.CreateDirectory(directory);
        
        try
        {
            // Write to a temporary file first, then atomically move it
            var tempPath = objectPath + ".tmp";
            await _fileSystem.File.WriteAllBytesAsync(tempPath, contentWithPrefix);
            
            // Atomic move (handles race conditions)
            try
            {
                _fileSystem.File.Move(tempPath, objectPath);
            }
            catch (IOException) when (_fileSystem.File.Exists(objectPath))
            {
                // Another process wrote the same object, delete our temp file
                _fileSystem.File.Delete(tempPath);
            }
            
            return objectId;
        }
        catch (Exception ex)
        {
            throw new CorruptObjectException(
                objectId.ToHexString(),
                typeof(TData),
                $"Failed to write {typeof(TData).Name} to storage: {ex.Message}",
                ex);
        }
    }    /// <summary>
    /// Reads data from storage and validates the type prefix
    /// </summary>
    private async Task<TData?> ReadObjectAsync<TData, TId>(TId id, string expectedTypePrefix, Func<byte[], TData> parser)
        where TId : IObjectId
    {
        var objectPath = GetObjectPath(id);
        
        if (!_fileSystem.File.Exists(objectPath))
        {
            return default(TData?);
        }
        
        try
        {
            var contentWithPrefix = await _fileSystem.File.ReadAllBytesAsync(objectPath);
            var expectedPrefixBytes = Encoding.UTF8.GetBytes(expectedTypePrefix);
            
            // Verify type prefix
            if (contentWithPrefix.Length < expectedPrefixBytes.Length)
            {
                throw new CorruptObjectException(
                    id.ToHexString(),
                    typeof(TData),
                    $"Object too short to contain type prefix",
                    null);
            }
              var actualPrefix = contentWithPrefix[..expectedPrefixBytes.Length];
            if (!actualPrefix.AsSpan().SequenceEqual(expectedPrefixBytes))
            {
                var actualPrefixString = Encoding.UTF8.GetString(actualPrefix);
                var actualType = GetTypeFromPrefix(actualPrefixString);
                throw new ObjectTypeMismatchException(
                    id.ToHexString(),
                    typeof(TData), // Expected type
                    actualType); // Actual type
            }
            
            // Extract content without type prefix
            var contentBytes = contentWithPrefix[expectedPrefixBytes.Length..];
            
            // Parse the content
            return parser(contentBytes);
        }
        catch (ObjectStoreException)
        {
            throw; // Re-throw our custom exceptions
        }
        catch (Exception ex)
        {
            throw new CorruptObjectException(
                id.ToHexString(),
                typeof(TData),
                $"Failed to read or parse {typeof(TData).Name}: {ex.Message}",
                ex);
        }
    }

    /// <summary>
    /// Gets the expected type from a type prefix string
    /// </summary>
    private static Type GetTypeFromPrefix(string prefix)
    {
        return prefix switch
        {
            ObjectHasher.ViewTypePrefix => typeof(ViewData),
            ObjectHasher.OperationTypePrefix => typeof(OperationData),
            ObjectHasher.CommitTypePrefix => typeof(CommitData),
            ObjectHasher.TreeTypePrefix => typeof(TreeData),
            ObjectHasher.BlobTypePrefix => typeof(FileContentData),
            _ => typeof(object)
        };
    }    public async Task<ViewId> WriteViewAsync(ViewData data)
    {
        return await WriteObjectAsync<ViewData, ViewId>(data, ObjectHasher.ViewTypePrefix);
    }    public async Task<ViewData?> ReadViewAsync(ViewId id)
    {
        var objectPath = GetObjectPath(id);
        
        if (!_fileSystem.File.Exists(objectPath))
        {
            return null;
        }
        
        var result = await ReadObjectAsync<ViewData, ViewId>(
            id, 
            ObjectHasher.ViewTypePrefix, 
            ViewData.ParseFromCanonicalBytes);
            
        return result;
    }public async Task<OperationId> WriteOperationAsync(OperationData data)
    {
        return await WriteObjectAsync<OperationData, OperationId>(data, ObjectHasher.OperationTypePrefix);
    }    public async Task<OperationData?> ReadOperationAsync(OperationId id)
    {
        var objectPath = GetObjectPath(id);
        
        if (!_fileSystem.File.Exists(objectPath))
        {
            return null;
        }
        
        var result = await ReadObjectAsync<OperationData, OperationId>(
            id, 
            ObjectHasher.OperationTypePrefix, 
            OperationData.ParseFromCanonicalBytes);
            
        return result;
    }
}
