using System;

namespace HPD.VCS.Core;

/// <summary>
/// Represents the value part of a tree entry, corresponding to the actual object being referenced.
/// This is used in conflicts to track what specific object IDs are conflicting.
/// </summary>
public readonly record struct TreeValue
{
    /// <summary>
    /// The type of the tree value.
    /// </summary>
    public TreeEntryType Type { get; }

    /// <summary>
    /// The object ID of the value.
    /// </summary>
    public ObjectIdBase ObjectId { get; }

    /// <summary>
    /// Creates a new TreeValue with the specified type and object ID.
    /// </summary>
    public TreeValue(TreeEntryType type, ObjectIdBase objectId)
    {
        Type = type;
        ObjectId = objectId;
    }    /// <summary>
    /// Creates a TreeValue for a file.
    /// </summary>
    public static TreeValue File(FileContentId fileContentId) => 
        new(TreeEntryType.File, new ObjectIdBase(fileContentId.HashValue.ToArray()));

    /// <summary>
    /// Creates a TreeValue for a directory.
    /// </summary>
    public static TreeValue Directory(TreeId treeId) => 
        new(TreeEntryType.Directory, new ObjectIdBase(treeId.HashValue.ToArray()));

    /// <summary>
    /// Creates a TreeValue for a symlink.
    /// </summary>
    public static TreeValue Symlink(FileContentId targetContentId) => 
        new(TreeEntryType.Symlink, new ObjectIdBase(targetContentId.HashValue.ToArray()));

    /// <summary>
    /// Creates a TreeValue for a conflict.
    /// </summary>
    public static TreeValue Conflict(ConflictId conflictId) => 
        new(TreeEntryType.Conflict, new ObjectIdBase(conflictId.HashValue.ToArray()));

    /// <summary>
    /// Gets the object ID as a specific type.
    /// </summary>
    public T GetObjectId<T>() where T : IObjectId, new()
    {
        var objectId = new T();
        return objectId.WithHashValue<T>(ObjectId.HashValue.ToArray());
    }
}
