using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace HPD.VCS.Core;

/// <summary>
/// Represents an operation in the VCS system - a record of a repository-modifying action.
/// Operations form a DAG where each operation points to its parent operations and 
/// the resulting repository state (view).
/// Based on jj's Operation but simplified for initial implementation.
/// </summary>
public readonly record struct OperationData : IContentHashable
{
    /// <summary>
    /// The ID of the view that represents the repository state after this operation
    /// </summary>
    public ViewId AssociatedViewId { get; init; }
    
    /// <summary>
    /// List of parent operation IDs that this operation builds upon
    /// </summary>
    public IReadOnlyList<OperationId> ParentOperationIds { get; init; }
    
    /// <summary>
    /// Metadata about when, how, and by whom this operation was performed
    /// </summary>
    public OperationMetadata Metadata { get; init; }

    /// <summary>
    /// Creates a new OperationData with the specified properties
    /// </summary>
    public OperationData(
        ViewId associatedViewId,
        IReadOnlyList<OperationId> parentOperationIds,
        OperationMetadata metadata)
    {
        AssociatedViewId = associatedViewId;
        ParentOperationIds = parentOperationIds ?? throw new ArgumentNullException(nameof(parentOperationIds));
        Metadata = metadata;
    }
    
    /// <summary>
    /// True if this is a root operation (no parents)
    /// </summary>
    public bool IsRootOperation => ParentOperationIds.Count == 0;
    
    /// <summary>
    /// True if this is a merge operation (multiple parents)
    /// </summary>
    public bool IsMergeOperation => ParentOperationIds.Count > 1;

    /// <summary>
    /// Gets the canonical byte representation for content hashing.
    /// Format:
    /// associated_view_id_hex\n
    /// parent_count\n
    /// parent1_operation_id_hex\n
    /// ...
    /// metadata_bytes
    /// </summary>
    public byte[] GetBytesForHashing()
    {
        var builder = new StringBuilder();
        
        // Associated view ID
        builder.AppendLine(AssociatedViewId.ToHexString());
        
        // Sort parent operation IDs by hex string for deterministic output
        var sortedParents = ParentOperationIds
            .OrderBy(opId => opId.ToHexString(), StringComparer.Ordinal)
            .ToList();
        
        builder.AppendLine(sortedParents.Count.ToString());
        
        foreach (var parentId in sortedParents)
        {
            builder.AppendLine(parentId.ToHexString());
        }
        
        // Metadata (already has deterministic serialization)
        var metadataBytes = Metadata.GetBytesForHashing();
        var metadataString = Encoding.UTF8.GetString(metadataBytes);
        builder.Append(metadataString);
        
        return Encoding.UTF8.GetBytes(builder.ToString());
    }
    
    /// <summary>
    /// Parses OperationData from canonical byte representation
    /// </summary>
    public static OperationData ParseFromCanonicalBytes(byte[] contentBytes)
    {
        ArgumentNullException.ThrowIfNull(contentBytes);
        
        // Handle cross-platform newlines by normalizing to \n
        var content = Encoding.UTF8.GetString(contentBytes).Replace("\r\n", "\n");
        var lines = content.Split('\n');
        var lineIndex = 0;
        
        try
        {
            // Parse associated view ID
            var viewIdHex = lines[lineIndex++];
            var associatedViewId = ViewId.FromHexString(viewIdHex);
            
            // Parse parent operation IDs
            var parentCount = int.Parse(lines[lineIndex++]);
            var parentOperationIds = new List<OperationId>();
            
            for (int i = 0; i < parentCount; i++)
            {
                var parentIdHex = lines[lineIndex++];
                var parentId = OperationId.FromHexString(parentIdHex);
                parentOperationIds.Add(parentId);
            }
            
            // Parse metadata (remaining content)
            var remainingLines = lines.Skip(lineIndex).ToArray();
            var metadataContent = string.Join("\n", remainingLines);
            var metadataBytes = Encoding.UTF8.GetBytes(metadataContent);
            var metadata = OperationMetadata.ParseFromCanonicalBytes(metadataBytes);
            
            return new OperationData(associatedViewId, parentOperationIds, metadata);
        }
        catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException or ArgumentOutOfRangeException)
        {
            throw new ArgumentException("Invalid OperationData byte format", nameof(contentBytes), ex);
        }
    }
    
    /// <summary>
    /// Creates a root operation (no parents) with the specified view and metadata
    /// </summary>
    public static OperationData CreateRoot(ViewId viewId, OperationMetadata metadata)
    {
        return new OperationData(viewId, new List<OperationId>(), metadata);
    }
    
    /// <summary>
    /// Creates a new operation that builds on a single parent
    /// </summary>
    public static OperationData CreateFromParent(
        ViewId viewId, 
        OperationId parentId, 
        OperationMetadata metadata)
    {
        return new OperationData(viewId, new List<OperationId> { parentId }, metadata);
    }
    
    /// <summary>
    /// Creates a merge operation that builds on multiple parents
    /// </summary>
    public static OperationData CreateMerge(
        ViewId viewId, 
        IReadOnlyList<OperationId> parentIds, 
        OperationMetadata metadata)
    {
        if (parentIds.Count < 2)
        {
            throw new ArgumentException("Merge operation must have at least 2 parents", nameof(parentIds));
        }
        
        return new OperationData(viewId, parentIds, metadata);
    }
}
