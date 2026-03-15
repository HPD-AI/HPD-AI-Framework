using System;
using System.IO;

namespace HPD.VCS.Storage;

/// <summary>
/// Base exception for all object store related errors.
/// Provides a common base for more specific object store exceptions.
/// </summary>
public abstract class ObjectStoreException : IOException
{
    /// <summary>
    /// Gets the hex string representation of the object ID that caused the error.
    /// </summary>
    public string ObjectIdHex { get; }
    
    /// <summary>
    /// Gets the type of object that caused the error.
    /// </summary>
    public Type ObjectType { get; }

    /// <summary>
    /// Initializes a new instance of the ObjectStoreException class.
    /// </summary>
    /// <param name="objectIdHex">The hex string representation of the object ID</param>
    /// <param name="objectType">The type of object that caused the error</param>
    /// <param name="message">The error message</param>
    /// <param name="innerException">The inner exception</param>
    protected ObjectStoreException(string objectIdHex, Type objectType, string? message = null, Exception? innerException = null)
        : base(message, innerException)
    {
        ObjectIdHex = objectIdHex ?? throw new ArgumentNullException(nameof(objectIdHex));
        ObjectType = objectType ?? throw new ArgumentNullException(nameof(objectType));
    }
}

/// <summary>
/// Exception thrown when an object is not found in the object store.
/// This typically indicates that the requested object ID does not exist in storage.
/// </summary>
public class ObjectNotFoundException : ObjectStoreException
{
    /// <summary>
    /// Initializes a new instance of the ObjectNotFoundException class.
    /// </summary>
    /// <param name="objectIdHex">The hex string representation of the object ID that was not found</param>
    /// <param name="objectType">The type of object that was not found</param>
    /// <param name="message">Optional custom error message</param>
    /// <param name="innerException">The inner exception that caused this error</param>
    public ObjectNotFoundException(string objectIdHex, Type objectType, string? message = null, Exception? innerException = null)
        : base(objectIdHex ?? throw new ArgumentNullException(nameof(objectIdHex)), 
               objectType ?? throw new ArgumentNullException(nameof(objectType)), 
               message ?? $"Object {objectIdHex} of type {objectType?.Name} not found in store", 
               innerException)
    {
    }
}

/// <summary>
/// Exception thrown when an object exists but is corrupted or cannot be deserialized.
/// This indicates that the stored data is invalid or has been corrupted.
/// </summary>
public class CorruptObjectException : ObjectStoreException
{
    /// <summary>
    /// Gets the reason why the object is considered corrupt.
    /// </summary>
    public string Reason { get; }

    /// <summary>
    /// Initializes a new instance of the CorruptObjectException class.
    /// </summary>
    /// <param name="objectIdHex">The hex string representation of the corrupt object ID</param>
    /// <param name="objectType">The type of object that is corrupt</param>
    /// <param name="reason">The reason why the object is considered corrupt</param>
    /// <param name="innerException">The inner exception that caused this error</param>
    public CorruptObjectException(string objectIdHex, Type objectType, string reason, Exception? innerException = null)
        : base(objectIdHex ?? throw new ArgumentNullException(nameof(objectIdHex)), 
               objectType ?? throw new ArgumentNullException(nameof(objectType)), 
               $"Object {objectIdHex} of type {objectType?.Name} is corrupt: {reason ?? throw new ArgumentNullException(nameof(reason))}", 
               innerException)
    {
        Reason = reason ?? throw new ArgumentNullException(nameof(reason));
    }
}

/// <summary>
/// Exception thrown when an object is found but has a different type than expected.
/// This can occur when object IDs are mixed up or when storage corruption affects type metadata.
/// </summary>
public class ObjectTypeMismatchException : ObjectStoreException
{
    /// <summary>
    /// Gets the expected object type.
    /// </summary>
    public Type ExpectedType { get; }
    
    /// <summary>
    /// Gets the actual object type that was found.
    /// </summary>
    public Type ActualType { get; }

    /// <summary>
    /// Initializes a new instance of the ObjectTypeMismatchException class.
    /// </summary>
    /// <param name="objectIdHex">The hex string representation of the object ID with type mismatch</param>
    /// <param name="expectedType">The expected object type</param>
    /// <param name="actualType">The actual object type that was found</param>
    /// <param name="innerException">The inner exception that caused this error</param>
    public ObjectTypeMismatchException(string objectIdHex, Type expectedType, Type actualType, Exception? innerException = null)
        : base(objectIdHex ?? throw new ArgumentNullException(nameof(objectIdHex)), 
               expectedType ?? throw new ArgumentNullException(nameof(expectedType)), 
               $"Object {objectIdHex} expected to be {expectedType?.Name} but was {actualType?.Name}", 
               innerException)
    {
        ExpectedType = expectedType ?? throw new ArgumentNullException(nameof(expectedType));
        ActualType = actualType ?? throw new ArgumentNullException(nameof(actualType));
    }
}
