using System;
using System.Linq;
using System.Security.Cryptography;

namespace HPD.VCS.Core;

/// <summary>
/// Base type for all content-addressable object IDs in the VCS system.
/// Provides type-safe wrappers around hash values with validation and conversion utilities.
/// </summary>
public readonly record struct ObjectIdBase
{
    /// <summary>
    /// Expected hash length in bytes (SHA256 = 32 bytes)
    /// </summary>
    public const int ExpectedHashLength = 32;

    /// <summary>
    /// The raw hash value bytes as read-only memory to prevent mutation
    /// </summary>
    private readonly byte[] _hashValue;

    /// <summary>
    /// Gets the raw hash value as read-only memory to prevent external mutation
    /// </summary>
    public ReadOnlyMemory<byte> HashValue => _hashValue;

    /// <summary>
    /// Primary constructor for ObjectIdBase
    /// </summary>
    /// <param name="hashValue">The hash value bytes (must be 32 bytes for SHA256)</param>
    /// <exception cref="ArgumentNullException">Thrown when hashValue is null</exception>
    /// <exception cref="ArgumentException">Thrown when hashValue length is invalid</exception>
    public ObjectIdBase(byte[] hashValue)
    {
        ArgumentNullException.ThrowIfNull(hashValue);
        
        if (hashValue.Length != ExpectedHashLength)
        {
            throw new ArgumentException(
                $"Hash value must be exactly {ExpectedHashLength} bytes, got {hashValue.Length}",
                nameof(hashValue));
        }

        // Create a defensive copy to ensure immutability
        _hashValue = new byte[hashValue.Length];
        Array.Copy(hashValue, _hashValue, hashValue.Length);
    }

    /// <summary>
    /// Converts the hash value to a lowercase hexadecimal string
    /// </summary>
    /// <returns>Full hex string representation of the hash</returns>
    public string ToHexString()
    {
        return Convert.ToHexString(_hashValue).ToLowerInvariant();
    }

    /// <summary>
    /// Returns a truncated hex string for display purposes
    /// </summary>
    /// <param name="length">Number of hex characters to include (default: 12)</param>
    /// <returns>Shortened hex string</returns>
    public string ToShortHexString(int length = 12)
    {
        var hex = ToHexString();
        return hex.Length > length ? hex[..length] : hex;
    }

    /// <summary>
    /// Parses a hex string into a specific ObjectId type
    /// </summary>
    /// <typeparam name="T">The target ObjectId type</typeparam>
    /// <param name="hex">Hex string to parse</param>
    /// <returns>Parsed ObjectId of the specified type</returns>
    /// <exception cref="ArgumentException">Thrown when hex string is invalid</exception>
    public static T FromHexString<T>(string hex) where T : IObjectId, new()
    {
        ArgumentNullException.ThrowIfNull(hex);
        
        // Remove any whitespace, newlines, and convert to uppercase for parsing
        hex = hex.Trim().Replace("\r", "").Replace("\n", "").ToUpperInvariant();
        
        if (hex.Length != ExpectedHashLength * 2)
        {
            throw new ArgumentException(
                $"Hex string must be exactly {ExpectedHashLength * 2} characters, got {hex.Length}",
                nameof(hex));
        }

        try
        {
            var bytes = Convert.FromHexString(hex);
            var objectId = new T();
            return objectId.WithHashValue<T>(bytes);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException($"Invalid hex string format: {hex}", nameof(hex), ex);
        }
    }

    /// <summary>
    /// Compares two ObjectIdBase instances for equality
    /// </summary>
    public bool Equals(ObjectIdBase other)
    {
        if (_hashValue == null && other._hashValue == null)
            return true;
        if (_hashValue == null || other._hashValue == null)
            return false;
            
        return _hashValue.AsSpan().SequenceEqual(other._hashValue.AsSpan());
    }

    /// <summary>
    /// Returns hash code based on the hash value
    /// </summary>
    public override int GetHashCode()
    {
        if (_hashValue == null || _hashValue.Length == 0)
            return 0;
            
        // Use first 4 bytes of hash for performance, as SHA256 has good distribution
        if (_hashValue.Length >= 4)
        {
            return BitConverter.ToInt32(_hashValue, 0);
        }
        
        // Fallback for shorter hashes
        return HashCode.Combine(_hashValue);
    }

    /// <summary>
    /// Returns short hex string representation
    /// </summary>
    public override string ToString()
    {
        return ToShortHexString();
    }
}

/// <summary>
/// Interface for all object ID types to enable generic operations
/// </summary>
public interface IObjectId
{
    /// <summary>
    /// Gets the underlying hash value as read-only memory
    /// </summary>
    ReadOnlyMemory<byte> HashValue { get; }
    
    /// <summary>
    /// Converts the hash value to a hexadecimal string
    /// </summary>
    string ToHexString();
    
    /// <summary>
    /// Returns a truncated hex string for display purposes
    /// </summary>
    string ToShortHexString(int length = 12);
    
    /// <summary>
    /// Creates a new instance with the specified hash value
    /// </summary>
    T WithHashValue<T>(byte[] hashValue) where T : IObjectId, new();
}

/// <summary>
/// Commit identifier - uniquely identifies a commit object
/// </summary>
public readonly record struct CommitId : IObjectId
{
    private readonly ObjectIdBase _base;

    public ReadOnlyMemory<byte> HashValue => _base.HashValue;

    public CommitId(byte[] hashValue)
    {
        _base = new ObjectIdBase(hashValue);
    }

    public T WithHashValue<T>(byte[] hashValue) where T : IObjectId, new()
    {
        if (typeof(T) == typeof(CommitId))
        {
            return (T)(object)new CommitId(hashValue);
        }
        throw new InvalidOperationException($"Cannot create {typeof(T).Name} from CommitId");
    }

    public string ToHexString() => _base.ToHexString();
    public string ToShortHexString(int length = 12) => _base.ToShortHexString(length);

    // Fix: Implement proper equality that delegates to ObjectIdBase
    public bool Equals(CommitId other) => _base.Equals(other._base);
    public override int GetHashCode() => _base.GetHashCode();

    public override string ToString()
    {
        return $"CommitId({_base.ToShortHexString()})";
    }
}

/// <summary>
/// Tree identifier - uniquely identifies a tree (directory) object
/// </summary>
public readonly record struct TreeId : IObjectId
{
    private readonly ObjectIdBase _base;

    public ReadOnlyMemory<byte> HashValue => _base.HashValue;

    public TreeId(byte[] hashValue)
    {
        _base = new ObjectIdBase(hashValue);
    }

    public T WithHashValue<T>(byte[] hashValue) where T : IObjectId, new()
    {
        if (typeof(T) == typeof(TreeId))
        {
            return (T)(object)new TreeId(hashValue);
        }
        throw new InvalidOperationException($"Cannot create {typeof(T).Name} from TreeId");
    }

    public string ToHexString() => _base.ToHexString();
    public string ToShortHexString(int length = 12) => _base.ToShortHexString(length);

    // Fix: Implement proper equality that delegates to ObjectIdBase
    public bool Equals(TreeId other) => _base.Equals(other._base);
    public override int GetHashCode() => _base.GetHashCode();

    public override string ToString()
    {
        return $"TreeId({_base.ToShortHexString()})";
    }
}

/// <summary>
/// File content identifier - uniquely identifies file content
/// </summary>
public readonly record struct FileContentId : IObjectId
{
    private readonly ObjectIdBase _base;

    public ReadOnlyMemory<byte> HashValue => _base.HashValue;

    public FileContentId(byte[] hashValue)
    {
        _base = new ObjectIdBase(hashValue);
    }

    public T WithHashValue<T>(byte[] hashValue) where T : IObjectId, new()
    {
        if (typeof(T) == typeof(FileContentId))
        {
            return (T)(object)new FileContentId(hashValue);
        }
        throw new InvalidOperationException($"Cannot create {typeof(T).Name} from FileContentId");
    }

    public string ToHexString() => _base.ToHexString();
    public string ToShortHexString(int length = 12) => _base.ToShortHexString(length);

    // Fix: Implement proper equality that delegates to ObjectIdBase
    public bool Equals(FileContentId other) => _base.Equals(other._base);
    public override int GetHashCode() => _base.GetHashCode();

    public override string ToString()
    {
        return $"FileContentId({_base.ToShortHexString()})";
    }
}

/// <summary>
/// View identifier - uniquely identifies a view (repository state) object
/// </summary>
public readonly record struct ViewId : IObjectId
{
    private readonly ObjectIdBase _base;

    public ReadOnlyMemory<byte> HashValue => _base.HashValue;

    public ViewId(byte[] hashValue)
    {
        _base = new ObjectIdBase(hashValue);
    }

    public T WithHashValue<T>(byte[] hashValue) where T : IObjectId, new()
    {
        if (typeof(T) == typeof(ViewId))
        {
            return (T)(object)new ViewId(hashValue);
        }
        throw new NotSupportedException($"Cannot create {typeof(T).Name} from ViewId");
    }

    public string ToHexString() => _base.ToHexString();
    public string ToShortHexString(int length = 12) => _base.ToShortHexString(length);

    // Fix: Implement proper equality that delegates to ObjectIdBase
    public bool Equals(ViewId other) => _base.Equals(other._base);
    public override int GetHashCode() => _base.GetHashCode();

    public static ViewId FromHexString(string hexString) => 
        ObjectIdBase.FromHexString<ViewId>(hexString);

    public override string ToString() => ToShortHexString();
}

/// <summary>
/// Operation identifier - uniquely identifies an operation (repo modification) object
/// </summary>
public readonly record struct OperationId : IObjectId
{
    private readonly ObjectIdBase _base;

    public ReadOnlyMemory<byte> HashValue => _base.HashValue;

    public OperationId(byte[] hashValue)
    {
        _base = new ObjectIdBase(hashValue);
    }

    public T WithHashValue<T>(byte[] hashValue) where T : IObjectId, new()
    {
        if (typeof(T) == typeof(OperationId))
        {
            return (T)(object)new OperationId(hashValue);
        }
        throw new NotSupportedException($"Cannot create {typeof(T).Name} from OperationId");
    }

    public string ToHexString() => _base.ToHexString();
    public string ToShortHexString(int length = 12) => _base.ToShortHexString(length);

    // Fix: Implement proper equality that delegates to ObjectIdBase
    public bool Equals(OperationId other) => _base.Equals(other._base);
    public override int GetHashCode() => _base.GetHashCode();

    public static OperationId FromHexString(string hexString) => 
        ObjectIdBase.FromHexString<OperationId>(hexString);

    public override string ToString() => ToShortHexString();
}

/// <summary>
/// Change identifier - uniquely identifies a change across commit rewrites
/// </summary>
public readonly record struct ChangeId : IObjectId
{
    private readonly ObjectIdBase _base;

    public ReadOnlyMemory<byte> HashValue => _base.HashValue;

    public ChangeId(byte[] hashValue)
    {
        _base = new ObjectIdBase(hashValue);
    }

    public T WithHashValue<T>(byte[] hashValue) where T : IObjectId, new()
    {
        if (typeof(T) == typeof(ChangeId))
        {
            return (T)(object)new ChangeId(hashValue);
        }
        throw new InvalidOperationException($"Cannot create {typeof(T).Name} from ChangeId");
    }

    public string ToHexString() => _base.ToHexString();
    public string ToShortHexString(int length = 12) => _base.ToShortHexString(length);

    // Fix: Implement proper equality that delegates to ObjectIdBase
    public bool Equals(ChangeId other) => _base.Equals(other._base);
    public override int GetHashCode() => _base.GetHashCode();

    public static ChangeId FromHexString(string hexString) => 
        ObjectIdBase.FromHexString<ChangeId>(hexString);    public override string ToString()
    {
        return $"ChangeId({_base.ToShortHexString()})";
    }
}

/// <summary>
/// Conflict identifier - uniquely identifies a conflict object
/// </summary>
public readonly record struct ConflictId : IObjectId
{
    private readonly ObjectIdBase _base;

    public ReadOnlyMemory<byte> HashValue => _base.HashValue;

    public ConflictId(byte[] hashValue)
    {
        _base = new ObjectIdBase(hashValue);
    }

    public T WithHashValue<T>(byte[] hashValue) where T : IObjectId, new()
    {
        if (typeof(T) == typeof(ConflictId))
        {
            return (T)(object)new ConflictId(hashValue);
        }
        throw new InvalidOperationException($"Cannot create {typeof(T).Name} from ConflictId");
    }

    public string ToHexString() => _base.ToHexString();
    public string ToShortHexString(int length = 12) => _base.ToShortHexString(length);

    public bool Equals(ConflictId other) => _base.Equals(other._base);
    public override int GetHashCode() => _base.GetHashCode();

    public override string ToString()
    {
        return $"ConflictId({_base.ToShortHexString()})";
    }
}

/// <summary>
/// Utility class for generating object IDs from content
/// </summary>
public static class ObjectIdFactory
{
    /// <summary>
    /// Generates a hash from content bytes using SHA256
    /// </summary>
    /// <param name="content">Content to hash</param>
    /// <returns>SHA256 hash of the content</returns>
    public static byte[] HashContent(byte[] content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return SHA256.HashData(content);
    }

    /// <summary>
    /// Generates a hash from content string using SHA256
    /// </summary>
    /// <param name="content">Content string to hash</param>
    /// <returns>SHA256 hash of the UTF-8 encoded content</returns>
    public static byte[] HashContent(string content)
    {
        ArgumentNullException.ThrowIfNull(content);
        return SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content));
    }

    /// <summary>
    /// Creates a CommitId from content
    /// </summary>
    public static CommitId CreateCommitId(byte[] content) => new(HashContent(content));

    /// <summary>
    /// Creates a TreeId from content
    /// </summary>
    public static TreeId CreateTreeId(byte[] content) => new(HashContent(content));

    /// <summary>
    /// Creates a FileContentId from content
    /// </summary>
    public static FileContentId CreateFileContentId(byte[] content) => new(HashContent(content));

    /// <summary>
    /// Creates a ViewId from content  
    /// </summary>
    public static ViewId CreateViewId(byte[] content) => new(HashContent(content));

    /// <summary>
    /// Creates an OperationId from content
    /// </summary>
    public static OperationId CreateOperationId(byte[] content) => new(HashContent(content));    /// <summary>
    /// Creates a ChangeId from content
    /// </summary>
    public static ChangeId CreateChangeId(byte[] content) => new(HashContent(content));

    /// <summary>
    /// Creates a ConflictId from content
    /// </summary>
    public static ConflictId CreateConflictId(byte[] content) => new(HashContent(content));
}