using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Abstractions;
using System.Linq;
using System.Threading.Tasks;
using HPD.VCS.Core;

namespace HPD.VCS.Storage;

/// <summary>
/// Filesystem-based implementation of the operation head store interface.
/// Stores operation head IDs in a "heads" file with atomic updates.
/// </summary>
public class FileSystemOperationHeadStore : IOperationHeadStore, IDisposable
{
    private readonly string _headsFilePath;
    private readonly IFileSystem _fileSystem;

    /// <summary>
    /// Initializes a new instance of the FileSystemOperationHeadStore class.
    /// </summary>
    /// <param name="storeDirectoryPath">The root directory path for the operation head store (e.g., .hpd/op_store)</param>
    public FileSystemOperationHeadStore(string storeDirectoryPath)
        : this(new FileSystem(), storeDirectoryPath)
    {
    }

    /// <summary>
    /// Initializes a new instance of the FileSystemOperationHeadStore class with a custom file system.
    /// </summary>
    /// <param name="fileSystem">The file system abstraction to use</param>
    /// <param name="storeDirectoryPath">The root directory path for the operation head store</param>
    public FileSystemOperationHeadStore(IFileSystem fileSystem, string storeDirectoryPath)
    {
        _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));
        var storePath = storeDirectoryPath ?? throw new ArgumentNullException(nameof(storeDirectoryPath));
        
        _headsFilePath = _fileSystem.Path.Combine(storePath, "heads");
        
        // Ensure the store directory exists
        _fileSystem.Directory.CreateDirectory(storePath);
    }

    public void Dispose()
    {
        // Nothing to dispose for this implementation
    }

    /// <summary>
    /// Gets the current head operation IDs from the heads file.
    /// </summary>
    public async Task<IReadOnlyList<OperationId>> GetHeadOperationIdsAsync()
    {
        try
        {
            if (!_fileSystem.File.Exists(_headsFilePath))
            {
                return new List<OperationId>();
            }

            var content = await _fileSystem.File.ReadAllTextAsync(_headsFilePath);
            var lines = content.Split('\n', StringSplitOptions.RemoveEmptyEntries)
                              .Select(line => line.Trim())
                              .Where(line => !string.IsNullOrEmpty(line))
                              .ToList();            var headIds = new List<OperationId>();
            foreach (var line in lines)
            {
                try
                {
                    var operationId = OperationId.FromHexString(line);
                    headIds.Add(operationId);
                }
                catch (ArgumentException ex)
                {
                    throw new CorruptObjectException(
                        line,
                        typeof(OperationId),
                        $"Invalid operation ID format in heads file: {line}",
                        ex);
                }
            }

            return headIds;
        }
        catch (ObjectStoreException)
        {
            throw; // Re-throw our custom exceptions
        }
        catch (Exception ex)
        {
            throw new CorruptObjectException(
                "heads",
                typeof(IReadOnlyList<OperationId>),
                $"Failed to read operation heads: {ex.Message}",
                ex);
        }
    }

    /// <summary>
    /// Updates the head operation IDs with basic Compare-And-Swap (CAS) semantics.
    /// For hpd V1, this focuses on single head management with concurrency detection.
    /// </summary>
    public async Task UpdateHeadOperationIdsAsync(IReadOnlyList<OperationId> oldExpectedHeadIds, OperationId newHeadId)
    {
        ArgumentNullException.ThrowIfNull(oldExpectedHeadIds);

        try
        {
            // Read current heads for CAS validation
            var currentHeads = await GetHeadOperationIdsAsync();

            // For hpd V1: Basic CAS validation
            if (oldExpectedHeadIds.Count > 0)
            {
                // Validate that the first expected ID matches the current single head (if any)
                var expectedFirstHead = oldExpectedHeadIds[0];
                
                if (currentHeads.Count == 0)
                {
                    throw new InvalidOperationException(
                        $"Concurrent operation detected: expected head {expectedFirstHead.ToShortHexString()}, but no heads exist");
                }
                
                if (currentHeads.Count > 0 && !currentHeads[0].Equals(expectedFirstHead))
                {
                    throw new InvalidOperationException(
                        $"Concurrent operation detected: expected head {expectedFirstHead.ToShortHexString()}, " +
                        $"but current head is {currentHeads[0].ToShortHexString()}");
                }
            }

            // Write new head to temporary file
            var tempFilePath = _headsFilePath + ".tmp";
            var newContent = newHeadId.ToHexString() + "\n";
            
            await _fileSystem.File.WriteAllTextAsync(tempFilePath, newContent);

            // Atomic rename to replace the heads file
            try
            {
                if (_fileSystem.File.Exists(_headsFilePath))
                {
                    _fileSystem.File.Delete(_headsFilePath);
                }
                _fileSystem.File.Move(tempFilePath, _headsFilePath);
            }
            catch (Exception)
            {
                // Clean up temp file if atomic operation failed
                if (_fileSystem.File.Exists(tempFilePath))
                {
                    try
                    {
                        _fileSystem.File.Delete(tempFilePath);
                    }
                    catch
                    {
                        // Ignore cleanup errors
                    }
                }
                throw;
            }
        }
        catch (InvalidOperationException)
        {
            throw; // Re-throw CAS failures
        }
        catch (ObjectStoreException)
        {
            throw; // Re-throw our custom exceptions
        }
        catch (Exception ex)
        {
            throw new CorruptObjectException(
                newHeadId.ToHexString(),
                typeof(OperationId),
                $"Failed to update operation heads: {ex.Message}",
                ex);
        }
    }
}
