using System;
using System.IO;
using Xunit;
using HPD.VCS.Core;
using HPD.VCS.Storage;

namespace HPD.VCS.Tests.Storage;

/// <summary>
/// Unit tests for object store exception classes
/// </summary>
public class ObjectStoreExceptionTests
{
    private const string TestObjectIdHex = "a1b2c3d4e5f6789012345678901234567890abcdef1234567890abcdef123456";
    private static readonly Type TestObjectType = typeof(CommitData);

    #region ObjectNotFoundException Tests

    [Fact]
    public void ObjectNotFoundException_Constructor_WithAllParameters_SetsPropertiesCorrectly()
    {
        // Arrange
        const string customMessage = "Custom not found message";
        var innerException = new IOException("Inner exception");

        // Act
        var exception = new ObjectNotFoundException(TestObjectIdHex, TestObjectType, customMessage, innerException);

        // Assert
        Assert.Equal(TestObjectIdHex, exception.ObjectIdHex);
        Assert.Equal(TestObjectType, exception.ObjectType);
        Assert.Equal(customMessage, exception.Message);
        Assert.Equal(innerException, exception.InnerException);
    }

    [Fact]
    public void ObjectNotFoundException_Constructor_WithDefaultMessage_GeneratesCorrectMessage()
    {
        // Act
        var exception = new ObjectNotFoundException(TestObjectIdHex, TestObjectType);

        // Assert
        Assert.Equal(TestObjectIdHex, exception.ObjectIdHex);
        Assert.Equal(TestObjectType, exception.ObjectType);
        Assert.Contains(TestObjectIdHex, exception.Message);
        Assert.Contains(TestObjectType.Name, exception.Message);
        Assert.Contains("not found", exception.Message);
    }

    [Fact]
    public void ObjectNotFoundException_Constructor_NullObjectIdHex_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new ObjectNotFoundException(null!, TestObjectType));
    }

    [Fact]
    public void ObjectNotFoundException_Constructor_NullObjectType_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new ObjectNotFoundException(TestObjectIdHex, null!));
    }

    #endregion

    #region CorruptObjectException Tests

    [Fact]
    public void CorruptObjectException_Constructor_WithAllParameters_SetsPropertiesCorrectly()
    {
        // Arrange
        const string reason = "Invalid JSON format";
        var innerException = new FormatException("Format error");

        // Act
        var exception = new CorruptObjectException(TestObjectIdHex, TestObjectType, reason, innerException);

        // Assert
        Assert.Equal(TestObjectIdHex, exception.ObjectIdHex);
        Assert.Equal(TestObjectType, exception.ObjectType);
        Assert.Equal(reason, exception.Reason);
        Assert.Contains(TestObjectIdHex, exception.Message);
        Assert.Contains(TestObjectType.Name, exception.Message);
        Assert.Contains(reason, exception.Message);
        Assert.Contains("corrupt", exception.Message);
        Assert.Equal(innerException, exception.InnerException);
    }

    [Fact]
    public void CorruptObjectException_Constructor_NullReason_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new CorruptObjectException(TestObjectIdHex, TestObjectType, null!));
    }

    [Fact]
    public void CorruptObjectException_Constructor_NullObjectIdHex_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new CorruptObjectException(null!, TestObjectType, "reason"));
    }

    [Fact]
    public void CorruptObjectException_Constructor_NullObjectType_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new CorruptObjectException(TestObjectIdHex, null!, "reason"));
    }

    #endregion

    #region ObjectTypeMismatchException Tests

    [Fact]
    public void ObjectTypeMismatchException_Constructor_WithAllParameters_SetsPropertiesCorrectly()
    {
        // Arrange
        var expectedType = typeof(CommitData);
        var actualType = typeof(TreeData);
        var innerException = new InvalidOperationException("Type error");

        // Act
        var exception = new ObjectTypeMismatchException(TestObjectIdHex, expectedType, actualType, innerException);

        // Assert
        Assert.Equal(TestObjectIdHex, exception.ObjectIdHex);
        Assert.Equal(expectedType, exception.ObjectType); // ObjectType should be the expected type
        Assert.Equal(expectedType, exception.ExpectedType);
        Assert.Equal(actualType, exception.ActualType);
        Assert.Contains(TestObjectIdHex, exception.Message);
        Assert.Contains(expectedType.Name, exception.Message);
        Assert.Contains(actualType.Name, exception.Message);
        Assert.Equal(innerException, exception.InnerException);
    }

    [Fact]
    public void ObjectTypeMismatchException_Constructor_NullObjectIdHex_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new ObjectTypeMismatchException(null!, typeof(CommitData), typeof(TreeData)));
    }

    [Fact]
    public void ObjectTypeMismatchException_Constructor_NullExpectedType_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new ObjectTypeMismatchException(TestObjectIdHex, null!, typeof(TreeData)));
    }

    [Fact]
    public void ObjectTypeMismatchException_Constructor_NullActualType_ThrowsArgumentNullException()
    {
        // Act & Assert
        Assert.Throws<ArgumentNullException>(() => 
            new ObjectTypeMismatchException(TestObjectIdHex, typeof(CommitData), null!));
    }

    #endregion

    #region Inheritance Tests

    [Fact]
    public void ObjectNotFoundException_InheritsFromObjectStoreException()
    {
        // Arrange & Act
        var exception = new ObjectNotFoundException(TestObjectIdHex, TestObjectType);

        // Assert
        Assert.IsAssignableFrom<ObjectStoreException>(exception);
        Assert.IsAssignableFrom<IOException>(exception);
    }

    [Fact]
    public void CorruptObjectException_InheritsFromObjectStoreException()
    {
        // Arrange & Act
        var exception = new CorruptObjectException(TestObjectIdHex, TestObjectType, "reason");

        // Assert
        Assert.IsAssignableFrom<ObjectStoreException>(exception);
        Assert.IsAssignableFrom<IOException>(exception);
    }

    [Fact]
    public void ObjectTypeMismatchException_InheritsFromObjectStoreException()
    {
        // Arrange & Act
        var exception = new ObjectTypeMismatchException(TestObjectIdHex, typeof(CommitData), typeof(TreeData));

        // Assert
        Assert.IsAssignableFrom<ObjectStoreException>(exception);
        Assert.IsAssignableFrom<IOException>(exception);
    }

    #endregion
}
