using System;
using System.IO;
using System.IO.Abstractions;

namespace HPD.VCS.Storage;

/// <summary>
/// Provides cross-platform file-based repository locking to prevent concurrent mutating operations.
/// Uses exclusive file access to ensure only one process can hold the lock at a time.
/// </summary>
public static class FileLock
{    /// <summary>
    /// Acquires an exclusive lock on the specified file path.
    /// The lock is held until the returned IDisposable is disposed.
    /// </summary>
    /// <param name="fileSystem">File system abstraction for testing support</param>
    /// <param name="lockFilePath">Path to the lock file</param>
    /// <returns>An IDisposable that releases the lock when disposed</returns>
    /// <exception cref="InvalidOperationException">Thrown when the lock cannot be acquired</exception>
    public static IDisposable Acquire(IFileSystem fileSystem, string lockFilePath)
    {
        ArgumentNullException.ThrowIfNull(fileSystem);
        ArgumentNullException.ThrowIfNull(lockFilePath);

        try
        {
            // Create the directory if it doesn't exist
            var lockDir = fileSystem.Path.GetDirectoryName(lockFilePath);
            if (!string.IsNullOrEmpty(lockDir) && !fileSystem.Directory.Exists(lockDir))
            {
                fileSystem.Directory.CreateDirectory(lockDir);
            }

            // Open the file with exclusive access - this will fail if another process has it locked
            var fileStream = fileSystem.File.Open(
                lockFilePath, 
                FileMode.OpenOrCreate, 
                FileAccess.ReadWrite, 
                FileShare.None);

            // Write process ID and timestamp to the lock file
            var processId = System.Diagnostics.Process.GetCurrentProcess().Id;
            var timestamp = DateTimeOffset.UtcNow;
            var lockInfo = $"Process ID: {processId}\nTimestamp: {timestamp:yyyy-MM-dd HH:mm:ss UTC}\n";
            var lockBytes = System.Text.Encoding.UTF8.GetBytes(lockInfo);
            
            fileStream.SetLength(0); // Clear any existing content
            fileStream.Write(lockBytes, 0, lockBytes.Length);
            fileStream.Flush();

            return new FileLockHandle(fileStream, lockFilePath);
        }
        catch (IOException ex)
        {
            // Try to read lock file to provide better error message
            string lockErrorDetails = "";
            try
            {
                if (fileSystem.File.Exists(lockFilePath))
                {
                    var lockContent = fileSystem.File.ReadAllText(lockFilePath);
                    lockErrorDetails = $" Lock file contents:\n{lockContent}";
                }
            }
            catch
            {
                // If we can't read the lock file, continue with basic error
            }            throw new InvalidOperationException(
                $"Failed to acquire repository lock. Another jj process may be running. Process ID: {System.Diagnostics.Process.GetCurrentProcess().Id}.{lockErrorDetails}", 
                ex);
        }
        catch (UnauthorizedAccessException ex)
        {
            throw new InvalidOperationException(
                "Failed to acquire repository lock due to insufficient permissions.", 
                ex);
        }
    }

    /// <summary>
    /// Internal implementation of the lock handle that manages the file stream lifecycle.
    /// </summary>
    private class FileLockHandle : IDisposable
    {
        private readonly Stream _fileStream;
        private readonly string _lockFilePath;
        private bool _disposed = false;

        public FileLockHandle(Stream fileStream, string lockFilePath)
        {
            _fileStream = fileStream ?? throw new ArgumentNullException(nameof(fileStream));
            _lockFilePath = lockFilePath ?? throw new ArgumentNullException(nameof(lockFilePath));
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                try
                {
                    _fileStream?.Dispose();
                }
                catch
                {
                    // Swallow exceptions during disposal to avoid masking original exceptions
                }
                finally
                {
                    _disposed = true;
                }
            }
        }
    }
}
