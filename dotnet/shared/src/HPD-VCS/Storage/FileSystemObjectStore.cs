using System;
using System.IO;
using System.IO.Abstractions;
using System.Text;
using System.Threading.Tasks;
using HPD.VCS.Core;

namespace HPD.VCS.Storage;

/// <summary>
/// Filesystem-based implementation of the object store interface.
/// Stores objects in a content-addressable manner using 2-character prefix sharding.
/// </summary>
public class FileSystemObjectStore : IObjectStore, IDisposable
{
    private readonly string _storeDirectoryPath;
    private readonly IFileSystem _fileSystem;

    /// <summary>
    /// Initializes a new instance of the FileSystemObjectStore class.
    /// </summary>
    /// <param name="storeDirectoryPath">The root directory path for storing objects (e.g., .hpd/objects)</param>
    public FileSystemObjectStore(string storeDirectoryPath)
        : this(new FileSystem(), storeDirectoryPath)
    {
    }

    /// <summary>
    /// Initializes a new instance of the FileSystemObjectStore class with a custom file system.
    /// </summary>
    /// <param name="fileSystem">The file system abstraction to use</param>
    /// <param name="storeDirectoryPath">The root directory path for storing objects (e.g., .hpd/objects)</param>
    public FileSystemObjectStore(IFileSystem fileSystem, string storeDirectoryPath)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        _storeDirectoryPath = storeDirectoryPath ?? throw new ArgumentNullException(nameof(storeDirectoryPath));
        // Ensure the store directory exists
        _fileSystem.Directory.CreateDirectory(_storeDirectoryPath);
    }

    public void Dispose()
    {
        // No resources to dispose in this implementation
    }

    /// <summary>
    /// Gets the file path for an object based on its ID using 2-character prefix sharding
    /// </summary>
    /// <param name="id">The object ID to convert</param>
    /// <returns>The full file path for the object</returns>
    private string GetObjectPath(IObjectId id)
    {
        var hexString = id.ToHexString().ToLowerInvariant();
        
        if (hexString.Length < 2)
        {
            throw new ArgumentException($"Object ID hex string is too short: {hexString}");
        }
        
        // Use first 2 characters as directory prefix
        var prefix = hexString.Substring(0, 2);
        var suffix = hexString.Substring(2);
        
        return Path.Combine(_storeDirectoryPath, prefix, suffix);
    }

    /// <summary>
    /// Writes a commit object to the store.
    /// </summary>
    /// <param name="data">The commit data to write</param>
    /// <returns>The computed commit ID</returns>
    public async Task<CommitId> WriteCommitAsync(CommitData data)
    {
        // Compute the commit ID
        var commitId = ObjectHasher.ComputeCommitId(data);
        
        // Determine the final object path
        var finalPath = GetObjectPath(commitId);
        
        // Write-if-absent optimization
        if (_fileSystem.File.Exists(finalPath))
        {
            return commitId;
        }
        
        // Get canonical object bytes
        var contentBytes = data.GetBytesForHashing();
        
        // Get type prefix bytes
        var prefixBytes = Encoding.UTF8.GetBytes(ObjectHasher.CommitTypePrefix);
        
        // Combine into bytes to write
        var bytesToWrite = new byte[prefixBytes.Length + contentBytes.Length];
        Array.Copy(prefixBytes, 0, bytesToWrite, 0, prefixBytes.Length);
        Array.Copy(contentBytes, 0, bytesToWrite, prefixBytes.Length, contentBytes.Length);
        
        // Perform atomic write
        await WriteObjectAtomically(finalPath, bytesToWrite);
        
        return commitId;
    }

    /// <summary>
    /// Writes a tree object to the store.
    /// </summary>
    /// <param name="data">The tree data to write</param>
    /// <returns>The computed tree ID</returns>
    public async Task<TreeId> WriteTreeAsync(TreeData data)
    {
        // Compute the tree ID
        var treeId = ObjectHasher.ComputeTreeId(data);
        
        // Determine the final object path
        var finalPath = GetObjectPath(treeId);
        
        // Write-if-absent optimization
        if (_fileSystem.File.Exists(finalPath))
        {
            return treeId;
        }
        
        // Get canonical object bytes
        var contentBytes = data.GetBytesForHashing();
        
        // Get type prefix bytes
        var prefixBytes = Encoding.UTF8.GetBytes(ObjectHasher.TreeTypePrefix);
        
        // Combine into bytes to write
        var bytesToWrite = new byte[prefixBytes.Length + contentBytes.Length];
        Array.Copy(prefixBytes, 0, bytesToWrite, 0, prefixBytes.Length);
        Array.Copy(contentBytes, 0, bytesToWrite, prefixBytes.Length, contentBytes.Length);
        
        // Perform atomic write
        await WriteObjectAtomically(finalPath, bytesToWrite);
        
        return treeId;
    }

    /// <summary>
    /// Writes a file content object to the store.
    /// </summary>
    /// <param name="data">The file content data to write</param>
    /// <returns>The computed file content ID</returns>
    public async Task<FileContentId> WriteFileContentAsync(FileContentData data)
    {
        // Compute the file content ID
        var fileContentId = ObjectHasher.ComputeFileContentId(data);
          // Determine the final object path
        var finalPath = GetObjectPath(fileContentId);
          // Write-if-absent optimization
        if (_fileSystem.File.Exists(finalPath))
        {
            return fileContentId;
        }
        
        // Get canonical object bytes
        var contentBytes = data.GetBytesForHashing();
        
        // Get type prefix bytes
        var prefixBytes = Encoding.UTF8.GetBytes(ObjectHasher.BlobTypePrefix);
          // Combine into bytes to write
        var bytesToWrite = new byte[prefixBytes.Length + contentBytes.Length];
        Array.Copy(prefixBytes, 0, bytesToWrite, 0, prefixBytes.Length);
        Array.Copy(contentBytes, 0, bytesToWrite, prefixBytes.Length, contentBytes.Length);
          // Perform atomic write
        await WriteObjectAtomically(finalPath, bytesToWrite);
        
        return fileContentId;
    }

    /// <summary>
    /// Reads a commit object from the store.
    /// </summary>
    /// <param name="id">The commit ID to read</param>
    /// <returns>The commit data, or null if not found</returns>
    public async Task<CommitData?> ReadCommitAsync(CommitId id)
    {
        // Determine file path
        var filePath = GetObjectPath(id);
        
        // Check if file exists
        if (!_fileSystem.File.Exists(filePath))
        {
            return null;
        }
        
        // Read file bytes
        var storedBytes = await _fileSystem.File.ReadAllBytesAsync(filePath);
        
        // Verify type prefix
        var expectedPrefixBytes = Encoding.UTF8.GetBytes(ObjectHasher.CommitTypePrefix);
        if (!StartsWithPrefix(storedBytes, expectedPrefixBytes))
        {
            var actualType = DetectObjectTypeFromPrefix(storedBytes);
            throw new ObjectTypeMismatchException(
                id.ToHexString(), 
                typeof(CommitData), 
                actualType
            );
        }
        
        // Extract content bytes
        var contentBytes = new byte[storedBytes.Length - expectedPrefixBytes.Length];
        Array.Copy(storedBytes, expectedPrefixBytes.Length, contentBytes, 0, contentBytes.Length);
        
        // Deserialize content bytes
        try
        {
            return CommitData.ParseFromCanonicalBytes(contentBytes);
        }
        catch (Exception ex)
        {
            throw new CorruptObjectException(
                id.ToHexString(),
                typeof(CommitData),
                "Failed to parse commit data from canonical bytes",
                ex
            );
        }
    }

    /// <summary>
    /// Reads a tree object from the store.
    /// </summary>
    /// <param name="id">The tree ID to read</param>
    /// <returns>The tree data, or null if not found</returns>
    public async Task<TreeData?> ReadTreeAsync(TreeId id)
    {
        // Determine file path
        var filePath = GetObjectPath(id);
        
        // Check if file exists
        if (!_fileSystem.File.Exists(filePath))
        {
            return null;
        }
        
        // Read file bytes
        var storedBytes = await _fileSystem.File.ReadAllBytesAsync(filePath);
        
        // Verify type prefix
        var expectedPrefixBytes = Encoding.UTF8.GetBytes(ObjectHasher.TreeTypePrefix);
        if (!StartsWithPrefix(storedBytes, expectedPrefixBytes))
        {
            var actualType = DetectObjectTypeFromPrefix(storedBytes);
            throw new ObjectTypeMismatchException(
                id.ToHexString(), 
                typeof(TreeData), 
                actualType
            );
        }
        
        // Extract content bytes
        var contentBytes = new byte[storedBytes.Length - expectedPrefixBytes.Length];
        Array.Copy(storedBytes, expectedPrefixBytes.Length, contentBytes, 0, contentBytes.Length);
        
        // Deserialize content bytes
        try
        {
            return TreeData.ParseFromCanonicalBytes(contentBytes);
        }
        catch (Exception ex)
        {
            throw new CorruptObjectException(
                id.ToHexString(),
                typeof(TreeData),
                "Failed to parse tree data from canonical bytes",
                ex
            );
        }
    }

    /// <summary>
    /// Reads a file content object from the store.
    /// </summary>
    /// <param name="id">The file content ID to read</param>
    /// <returns>The file content data, or null if not found</returns>
    public async Task<FileContentData?> ReadFileContentAsync(FileContentId id)
    {        // Determine file path
        var filePath = GetObjectPath(id);
          // Check if file exists
        if (!_fileSystem.File.Exists(filePath))
        {
            return null;        }
          // Read file bytes
        // TODO: For large blobs (>10MB), consider streaming to avoid loading entire content into memory
        var storedBytes = await _fileSystem.File.ReadAllBytesAsync(filePath);
        
        // Verify type prefix
        var expectedPrefixBytes = Encoding.UTF8.GetBytes(ObjectHasher.BlobTypePrefix);
        if (!StartsWithPrefix(storedBytes, expectedPrefixBytes))
        {
            var actualType = DetectObjectTypeFromPrefix(storedBytes);
            throw new ObjectTypeMismatchException(
                id.ToHexString(), 
                typeof(FileContentData), 
                actualType
            );
        }
          // Extract content bytes (the actual file content)
        var contentBytes = new byte[storedBytes.Length - expectedPrefixBytes.Length];
        Array.Copy(storedBytes, expectedPrefixBytes.Length, contentBytes, 0, contentBytes.Length);
        
        // For FileContentData, the content bytes are the file content directly
        return new FileContentData(contentBytes);
    }

    /// <summary>
    /// Performs an atomic write operation using a temporary file.
    /// </summary>
    /// <param name="finalPath">The final path where the file should be written</param>
    /// <param name="bytesToWrite">The bytes to write</param>
    private async Task WriteObjectAtomically(string finalPath, byte[] bytesToWrite)
    {
        // Ensure the shard directory exists
        var directoryPath = Path.GetDirectoryName(finalPath);
        if (!string.IsNullOrEmpty(directoryPath))
        {
            _fileSystem.Directory.CreateDirectory(directoryPath);
        }
        
        // Generate a temporary file path in the same directory
        var tempPath = Path.Combine(directoryPath!, Path.GetRandomFileName());
        
        try
        {
            // Write to temporary file
            await _fileSystem.File.WriteAllBytesAsync(tempPath, bytesToWrite);
            
            // Move to final location
            try
            {
                _fileSystem.File.Move(tempPath, finalPath, overwrite: false);
            }
            catch (IOException) when (_fileSystem.File.Exists(finalPath))
            {
                // Race condition: another process/thread wrote the same file. This is acceptable.
                // Clean up the temp file if it still exists.
                if (_fileSystem.File.Exists(tempPath))
                {
                    _fileSystem.File.Delete(tempPath);
                }
            }
            catch
            {
                // Other errors during move, clean up temp file and rethrow.
                if (_fileSystem.File.Exists(tempPath))
                {
                    _fileSystem.File.Delete(tempPath);
                }
                throw;
            }
        }
        catch
        {
            // Clean up temp file on any error during write
            if (_fileSystem.File.Exists(tempPath))
            {
                _fileSystem.File.Delete(tempPath);
            }
            throw;
        }
    }

    /// <summary>
    /// Checks if the byte array starts with the specified prefix using high-performance comparison.
    /// </summary>
    /// <param name="bytes">The byte array to check</param>
    /// <param name="prefix">The prefix to look for</param>
    /// <returns>True if the bytes start with the prefix, false otherwise</returns>
    private static bool StartsWithPrefix(byte[] bytes, byte[] prefix)
    {
        if (bytes.Length < prefix.Length)
        {
            return false;
        }
        
        // Use ReadOnlySpan for better performance (available in .NET 9)
        return bytes.AsSpan(0, prefix.Length).SequenceEqual(prefix.AsSpan());
    }    /// <summary>
    /// Detects the object type from the stored prefix bytes.
    /// </summary>
    /// <param name="storedBytes">The stored object bytes including prefix</param>
    /// <returns>The detected object type</returns>
    private static Type DetectObjectTypeFromPrefix(byte[] storedBytes)
    {
        var commitPrefixBytes = Encoding.UTF8.GetBytes(ObjectHasher.CommitTypePrefix);
        var treePrefixBytes = Encoding.UTF8.GetBytes(ObjectHasher.TreeTypePrefix);
        var blobPrefixBytes = Encoding.UTF8.GetBytes(ObjectHasher.BlobTypePrefix);
        var conflictPrefixBytes = Encoding.UTF8.GetBytes(ObjectHasher.ConflictTypePrefix);
        
        if (StartsWithPrefix(storedBytes, commitPrefixBytes))
        {
            return typeof(CommitData);
        }
        
        if (StartsWithPrefix(storedBytes, treePrefixBytes))
        {
            return typeof(TreeData);
        }
        
        if (StartsWithPrefix(storedBytes, blobPrefixBytes))
        {
            return typeof(FileContentData);
        }
        
        if (StartsWithPrefix(storedBytes, conflictPrefixBytes))
        {
            return typeof(ConflictData);
        }
        
        return typeof(object); // Unknown type
    }

    /// <summary>
    /// Writes a conflict object to the store.
    /// </summary>
    /// <param name="data">The conflict data to write</param>
    /// <returns>The computed conflict ID</returns>
    public async Task<ConflictId> WriteConflictAsync(ConflictData data)
    {
        // Compute the conflict ID
        var conflictId = ObjectHasher.ComputeConflictId(data);
        
        // Determine the final object path
        var finalPath = GetObjectPath(conflictId);
        
        // Write-if-absent optimization
        if (_fileSystem.File.Exists(finalPath))
        {
            return conflictId;
        }
        
        // Get canonical object bytes
        var contentBytes = data.GetBytesForHashing();
        
        // Get type prefix bytes
        var prefixBytes = Encoding.UTF8.GetBytes(ObjectHasher.ConflictTypePrefix);
        
        // Combine into bytes to write
        var bytesToWrite = new byte[prefixBytes.Length + contentBytes.Length];
        Array.Copy(prefixBytes, 0, bytesToWrite, 0, prefixBytes.Length);
        Array.Copy(contentBytes, 0, bytesToWrite, prefixBytes.Length, contentBytes.Length);
        
        // Perform atomic write
        await WriteObjectAtomically(finalPath, bytesToWrite);
        
        return conflictId;
    }

    /// <summary>
    /// Reads a conflict object from the store.
    /// </summary>
    /// <param name="id">The conflict ID to read</param>
    /// <returns>The conflict data, or null if not found</returns>
    public async Task<ConflictData?> ReadConflictAsync(ConflictId id)
    {
        // Determine file path
        var filePath = GetObjectPath(id);
        
        // Check if file exists
        if (!_fileSystem.File.Exists(filePath))
        {
            return null;
        }
        
        // Read file bytes
        var storedBytes = await _fileSystem.File.ReadAllBytesAsync(filePath);
          // Verify type prefix
        var expectedPrefixBytes = Encoding.UTF8.GetBytes(ObjectHasher.ConflictTypePrefix);
        if (!StartsWithPrefix(storedBytes, expectedPrefixBytes))
        {
            var actualType = DetectObjectTypeFromPrefix(storedBytes);
            throw new ObjectTypeMismatchException(id.ToHexString(), typeof(ConflictData), actualType);
        }
        
        // Extract content bytes (skip prefix)
        var contentBytes = new byte[storedBytes.Length - expectedPrefixBytes.Length];
        Array.Copy(storedBytes, expectedPrefixBytes.Length, contentBytes, 0, contentBytes.Length);
        
        // Parse content
        try
        {
            return ParseConflictData(contentBytes);
        }
        catch (Exception ex)
        {
            throw new CorruptObjectException(id.ToHexString(), typeof(ConflictData), "Failed to parse conflict object", ex);
        }
    }    /// <summary>
    /// Parses conflict data from bytes.
    /// </summary>
    private static ConflictData ParseConflictData(byte[] contentBytes)
    {
        var content = Encoding.UTF8.GetString(contentBytes);
        
        if (!content.StartsWith("conflict:"))
        {
            throw new FormatException("Invalid conflict data format");
        }
        
        // Parse removes and adds sections
        var parts = content.Split(new[] { "removes:", "adds:" }, StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3) // [0] = "conflict:", [1] = removes data, [2] = adds data
        {
            throw new FormatException("Invalid conflict data format - missing removes or adds section");
        }
        
        var removesPart = parts[1].TrimEnd(',');
        var addsPart = parts[2].TrimEnd(',');
        
        var removes = new List<TreeValue?>();
        var adds = new List<TreeValue?>();
        
        // Parse removes
        if (!string.IsNullOrEmpty(removesPart))
        {
            var removeStrings = removesPart.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var removeString in removeStrings)
            {
                if (removeString.Trim() == "null")
                {
                    removes.Add(null);
                }
                else
                {
                    removes.Add(ParseTreeValue(removeString.Trim()));
                }
            }
        }
        
        // Parse adds
        if (!string.IsNullOrEmpty(addsPart))
        {
            var addStrings = addsPart.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var addString in addStrings)
            {
                if (addString.Trim() == "null")
                {
                    adds.Add(null);
                }
                else
                {
                    adds.Add(ParseTreeValue(addString.Trim()));
                }
            }
        }
        
        var merge = new Merge<TreeValue?>(removes, adds);
        return new ConflictData(merge);
    }

    /// <summary>
    /// Parses a TreeValue from a string in format "Type:ObjectIdHex".
    /// </summary>
    private static TreeValue ParseTreeValue(string value)
    {
        var parts = value.Split(':', 2);
        if (parts.Length != 2)
        {
            throw new FormatException($"Invalid TreeValue format: {value}");
        }
        
        var typeString = parts[0];
        var objectIdHex = parts[1];
        
        if (!Enum.TryParse<TreeEntryType>(typeString, out var type))
        {
            throw new FormatException($"Invalid TreeEntryType: {typeString}");
        }
        
        var objectIdBase = new ObjectIdBase(Convert.FromHexString(objectIdHex));
        return new TreeValue(type, objectIdBase);
    }
}
