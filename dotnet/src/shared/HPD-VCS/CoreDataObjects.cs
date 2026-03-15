using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HPD.VCS.Storage;

namespace HPD.VCS.Core;

/// <summary>
/// Represents immutable file content data with a secure hash.
/// </summary>
public readonly record struct FileContentData : IContentHashable
{
    /// <summary>
    /// Gets the content bytes as a read-only list.
    /// </summary>
    public IReadOnlyList<byte> Content { get; }

    /// <summary>
    /// Creates a new FileContentData with defensive copy of the content.
    /// </summary>
    public FileContentData(IEnumerable<byte> content)
    {
        ArgumentNullException.ThrowIfNull(content);
        Content = content.ToList().AsReadOnly();
    }/// <summary>
    /// Gets the canonical byte representation for content hashing.
    /// File content is hashed as-is without any prefixes.
    /// </summary>
    public byte[] GetBytesForHashing()
    {
        return Content.ToArray();
    }

    /// <summary>
    /// Gets the size of the file content in bytes.
    /// </summary>
    public int Size => Content.Count;

    /// <summary>
    /// Checks if the file content is empty.
    /// </summary>
    public bool IsEmpty => Content.Count == 0;
}

/// <summary>
/// Represents a single entry in a directory tree.
/// </summary>
public readonly record struct TreeEntry(
    RepoPathComponent Name,
    TreeEntryType Type,
    ObjectIdBase ObjectId) : IComparable<TreeEntry>
{
    /// <summary>
    /// Compares tree entries by name for consistent ordering.
    /// </summary>
    public int CompareTo(TreeEntry other)
    {
        return string.Compare(Name.Value, other.Name.Value, StringComparison.Ordinal);
    }

    /// <summary>
    /// Gets the canonical byte representation for content hashing.
    /// Internal method used only by TreeData for hash computation.
    /// </summary>
    internal byte[] GetBytesForHashing()
    {
        var typeString = Type switch
        {
            TreeEntryType.File => "file",
            TreeEntryType.Directory => "directory", // Fixed: Use consistent naming with enum
            TreeEntryType.Symlink => "symlink",
            TreeEntryType.Conflict => "conflict",
            _ => throw new InvalidOperationException($"Unknown tree entry type: {Type}")
        };

        var name = Name.Value;
        var hash = ObjectId.ToHexString();
        var content = $"{typeString} {name} {hash}";
        return Encoding.UTF8.GetBytes(content);
    }
}

/// <summary>
/// Defines the type of entries that can exist in a tree.
/// </summary>
public enum TreeEntryType
{
    File,
    Directory,
    Symlink,
    Conflict
}

/// <summary>
/// Represents an immutable directory tree structure.
/// </summary>
public readonly record struct TreeData : IContentHashable
{
    /// <summary>
    /// Gets the entries in this tree, sorted by name.
    /// </summary>
    public IReadOnlyList<TreeEntry> Entries { get; }

    /// <summary>
    /// Creates a new TreeData with defensive copy and sorting of entries.
    /// </summary>
    public TreeData(IEnumerable<TreeEntry> entries)
    {
        ArgumentNullException.ThrowIfNull(entries);
        
        var sortedEntries = entries.ToList();
        sortedEntries.Sort();
        Entries = sortedEntries.AsReadOnly();
        
        // Validate no duplicate names
        for (int i = 1; i < sortedEntries.Count; i++)
        {
            if (sortedEntries[i - 1].Name.Value == sortedEntries[i].Name.Value)
            {
                throw new ArgumentException($"Duplicate entry name: {sortedEntries[i].Name.Value}");
            }
        }
    }    /// <summary>
    /// Gets the canonical byte representation for content hashing.
    /// Tree content includes all entries in sorted order.
    /// </summary>
    public byte[] GetBytesForHashing()
    {
        if (Entries.Count == 0)
        {
            return Array.Empty<byte>();
        }

        var content = new List<byte>();
        foreach (var entry in Entries)
        {
            content.AddRange(entry.GetBytesForHashing());
            content.Add((byte)'\n'); // Separator between entries
        }
        
        // Remove the last newline
        if (content.Count > 0)
        {
            content.RemoveAt(content.Count - 1);
        }
        
        return content.ToArray();
    }

    /// <summary>
    /// Checks if the tree is empty (contains no entries).
    /// </summary>
    public bool IsEmpty => Entries.Count == 0;

    /// <summary>
    /// Parses a TreeData instance from canonical byte representation.
    /// </summary>
    /// <param name="canonicalBytes">The canonical bytes to parse</param>
    /// <returns>The parsed TreeData instance</returns>
    /// <exception cref="CorruptObjectException">Thrown when the bytes cannot be parsed</exception>
    public static TreeData ParseFromCanonicalBytes(byte[] canonicalBytes)
    {
        try
        {
            if (canonicalBytes.Length == 0)
            {
                return new TreeData(Array.Empty<TreeEntry>());
            }

            var content = Encoding.UTF8.GetString(canonicalBytes);
            var lines = content.Split('\n');
            var entries = new List<TreeEntry>();

            foreach (var line in lines)
            {
                var parts = line.Split(' ', 3);
                if (parts.Length != 3)
                {
                    throw new FormatException($"Invalid tree entry format: {line}");
                }

                var typeString = parts[0];
                var name = parts[1];
                var hashHex = parts[2];                var entryType = typeString switch
                {
                    "file" => TreeEntryType.File,
                    "directory" => TreeEntryType.Directory,
                    "symlink" => TreeEntryType.Symlink,
                    "conflict" => TreeEntryType.Conflict,
                    _ => throw new FormatException($"Unknown tree entry type: {typeString}")
                };

                // Parse hex string to bytes and create ObjectIdBase
                var hashBytes = Convert.FromHexString(hashHex.ToUpperInvariant());
                var objectId = new ObjectIdBase(hashBytes);
                var pathComponent = new RepoPathComponent(name);
                
                entries.Add(new TreeEntry(pathComponent, entryType, objectId));
            }

            return new TreeData(entries);
        }
        catch (Exception ex) when (!(ex is CorruptObjectException))
        {
            throw new ArgumentException("Failed to parse tree data from canonical bytes", nameof(canonicalBytes), ex);
        }
    }

    /// <summary>
    /// Gets the number of entries in this tree.
    /// </summary>
    public int Count => Entries.Count;
}

/// <summary>
/// Represents author or committer signature information.
/// </summary>
public readonly record struct Signature(
    string Name,
    string Email,
    DateTimeOffset Timestamp)
{
    /// <summary>
    /// Gets the canonical byte representation for content hashing.
    /// Format: "Name <email> timestamp timezone"
    /// </summary>
    public byte[] GetBytesForHashing()
    {
        var unixTimestamp = Timestamp.ToUnixTimeMilliseconds();
        var offsetMinutes = (int)Timestamp.Offset.TotalMinutes;
        var offsetHours = offsetMinutes / 60;
        var offsetMins = Math.Abs(offsetMinutes % 60);
        var offsetSign = offsetMinutes >= 0 ? "+" : "-";
        var offsetString = $"{offsetSign}{Math.Abs(offsetHours):D2}{offsetMins:D2}";
        
        var content = $"{Name} <{Email}> {unixTimestamp} {offsetString}";
        return Encoding.UTF8.GetBytes(content);
    }

    /// <summary>
    /// Returns a string representation of the signature.
    /// </summary>
    public override string ToString()
    {
        return $"{Name} <{Email}> at {Timestamp:yyyy-MM-dd HH:mm:ss zzz}";
    }
}

/// <summary>
/// Represents immutable commit data in the version control system.
/// </summary>
public readonly record struct CommitData : IContentHashable
{
    /// <summary>
    /// Gets the parent commit IDs (empty for root commits).
    /// </summary>
    public IReadOnlyList<CommitId> ParentIds { get; }

    /// <summary>
    /// Gets the root tree ID for this commit.
    /// </summary>
    public TreeId RootTreeId { get; }

    /// <summary>
    /// Gets the associated change ID for this commit.
    /// </summary>
    public ChangeId AssociatedChangeId { get; }

    /// <summary>
    /// Gets the commit message/description.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the author signature.
    /// </summary>
    public Signature Author { get; }

    /// <summary>
    /// Gets the committer signature.
    /// </summary>
    public Signature Committer { get; }

    /// <summary>
    /// Creates a new CommitData with defensive copying of mutable collections.
    /// </summary>
    public CommitData(
        IEnumerable<CommitId> parentIds,
        TreeId rootTreeId,
        ChangeId associatedChangeId,
        string description,
        Signature author,
        Signature committer)
    {
        ArgumentNullException.ThrowIfNull(parentIds);
        ArgumentNullException.ThrowIfNull(description);
        
        ParentIds = parentIds.ToList().AsReadOnly();
        RootTreeId = rootTreeId;
        AssociatedChangeId = associatedChangeId;
        Description = description;
        Author = author;
        Committer = committer;
    }    /// <summary>
    /// Gets the canonical byte representation for content hashing.
    /// Uses Git-compatible commit format for consistency.
    /// Parent ordering is deterministic (sorted by hex string) to ensure reproducible hashes.
    /// </summary>
    public byte[] GetBytesForHashing()
    {
        var content = new StringBuilder();
        
        // Tree reference
        content.AppendLine($"tree {RootTreeId.ToHexString()}");
        
        // Parent references (sorted for deterministic output)
        // This ensures that merges with the same parents always hash identically
        var sortedParents = ParentIds.Select(p => p.ToHexString()).OrderBy(h => h);
        foreach (var parentHex in sortedParents)
        {
            content.AppendLine($"parent {parentHex}");
        }
        
        // Change ID (custom field)
        content.AppendLine($"change {AssociatedChangeId.ToHexString()}");
        
        // Author and committer (directly using signature byte representation)
        content.Append("author ");
        content.AppendLine(Encoding.UTF8.GetString(Author.GetBytesForHashing()));
        content.Append("committer ");
        content.AppendLine(Encoding.UTF8.GetString(Committer.GetBytesForHashing()));
        
        // Empty line before message
        content.AppendLine();
        
        // Commit message (no trailing newline)
        content.Append(Description);
        
        return Encoding.UTF8.GetBytes(content.ToString());
    }

    /// <summary>
    /// Checks if this is a root commit (no parents).
    /// </summary>
    public bool IsRootCommit => ParentIds.Count == 0;

    /// <summary>
    /// Checks if this is a merge commit (multiple parents).
    /// </summary>
    public bool IsMergeCommit => ParentIds.Count > 1;

    /// <summary>
    /// Returns a string representation of the commit.
    /// </summary>
    public override string ToString()
    {
        var parentInfo = ParentIds.Count switch
        {
            0 => "root commit",
            1 => $"parent: {ParentIds[0].ToShortHexString()}",
            _ => $"{ParentIds.Count} parents"
        };
        
        return $"Commit({AssociatedChangeId.ToShortHexString()}) {parentInfo}: {Description}";
    }

    /// <summary>
    /// Parses a CommitData instance from canonical byte representation.
    /// </summary>
    /// <param name="canonicalBytes">The canonical bytes to parse</param>
    /// <returns>The parsed CommitData instance</returns>
    /// <exception cref="ArgumentException">Thrown when the bytes cannot be parsed</exception>
    public static CommitData ParseFromCanonicalBytes(byte[] canonicalBytes)
    {
        try
        {
            var content = Encoding.UTF8.GetString(canonicalBytes);
            
            // Normalize line endings to \n
            content = content.Replace("\r\n", "\n").Replace("\r", "\n");
            
            var lines = content.Split('\n');
            var parentIds = new List<CommitId>();
            TreeId? rootTreeId = null;
            ChangeId? associatedChangeId = null;
            Signature? author = null;
            Signature? committer = null;
            string? description = null;
            
            int i = 0;
            
            // Parse headers
            while (i < lines.Length && !string.IsNullOrEmpty(lines[i]))
            {
                var line = lines[i];
                
                if (line.StartsWith("tree "))
                {
                    var treeHex = line.Substring(5);
                    var treeBytes = Convert.FromHexString(treeHex.ToUpperInvariant());
                    rootTreeId = new TreeId(treeBytes);
                }
                else if (line.StartsWith("parent "))
                {
                    var parentHex = line.Substring(7);
                    var parentBytes = Convert.FromHexString(parentHex.ToUpperInvariant());
                    parentIds.Add(new CommitId(parentBytes));
                }
                else if (line.StartsWith("change "))
                {
                    var changeHex = line.Substring(7);
                    var changeBytes = Convert.FromHexString(changeHex.ToUpperInvariant());
                    associatedChangeId = new ChangeId(changeBytes);
                }
                else if (line.StartsWith("author "))
                {
                    var authorInfo = line.Substring(7);
                    author = ParseSignature(authorInfo);
                }
                else if (line.StartsWith("committer "))
                {
                    var committerInfo = line.Substring(10);
                    committer = ParseSignature(committerInfo);
                }
                
                i++;
            }
            
            // Skip empty line
            if (i < lines.Length && string.IsNullOrEmpty(lines[i]))
            {
                i++;
            }
            
            // Rest is the commit message
            if (i < lines.Length)
            {
                description = string.Join("\n", lines.Skip(i));
            }
            
            // Validate required fields
            if (rootTreeId == null)
                throw new FormatException("Missing tree reference");
            if (associatedChangeId == null)
                throw new FormatException("Missing change reference");
            if (author == null)
                throw new FormatException("Missing author");
            if (committer == null)
                throw new FormatException("Missing committer");
            if (description == null)
                throw new FormatException("Missing description");
            
            return new CommitData(
                parentIds,
                rootTreeId.Value,
                associatedChangeId.Value,
                description,
                author.Value,
                committer.Value);
        }
        catch (Exception ex) when (!(ex is CorruptObjectException))
        {
            throw new ArgumentException("Failed to parse commit data from canonical bytes", nameof(canonicalBytes), ex);
        }
    }

    /// <summary>
    /// Parses a signature from the canonical string format.
    /// </summary>
    /// <param name="signatureText">The signature text to parse</param>
    /// <returns>The parsed signature</returns>
    private static Signature ParseSignature(string signatureText)
    {
        // Format: "Name <email> timestamp offset"
        // Example: "John Doe <john@example.com> 1234567890 +0200"
        
        var lastSpaceIndex = signatureText.LastIndexOf(' ');
        if (lastSpaceIndex == -1)
            throw new FormatException($"Invalid signature format: {signatureText}");
        
        var offsetString = signatureText.Substring(lastSpaceIndex + 1);
        var remaining = signatureText.Substring(0, lastSpaceIndex);
        
        lastSpaceIndex = remaining.LastIndexOf(' ');
        if (lastSpaceIndex == -1)
            throw new FormatException($"Invalid signature format: {signatureText}");
        
        var timestampString = remaining.Substring(lastSpaceIndex + 1);
        var nameAndEmail = remaining.Substring(0, lastSpaceIndex);
        
        // Parse name and email: "Name <email>"
        var emailStart = nameAndEmail.LastIndexOf('<');
        var emailEnd = nameAndEmail.LastIndexOf('>');
        
        if (emailStart == -1 || emailEnd == -1 || emailEnd <= emailStart)
            throw new FormatException($"Invalid signature format: {signatureText}");
        
        var name = nameAndEmail.Substring(0, emailStart).Trim();
        var email = nameAndEmail.Substring(emailStart + 1, emailEnd - emailStart - 1);
        
        // Parse timestamp
        if (!long.TryParse(timestampString, out var unixTimestamp))
            throw new FormatException($"Invalid timestamp: {timestampString}");
        
        // Parse offset
        if (offsetString.Length != 5 || (offsetString[0] != '+' && offsetString[0] != '-'))
            throw new FormatException($"Invalid timezone offset: {offsetString}");
        
        var offsetSign = offsetString[0] == '+' ? 1 : -1;
        if (!int.TryParse(offsetString.Substring(1, 2), out var offsetHours) ||
            !int.TryParse(offsetString.Substring(3, 2), out var offsetMinutes))
            throw new FormatException($"Invalid timezone offset: {offsetString}");
        
        var totalOffsetMinutes = offsetSign * (offsetHours * 60 + offsetMinutes);
        var offset = TimeSpan.FromMinutes(totalOffsetMinutes);
        
        var timestamp = DateTimeOffset.FromUnixTimeMilliseconds(unixTimestamp).ToOffset(offset);
        
        return new Signature(name, email, timestamp);
    }
}

/// <summary>
/// Represents conflict data for tree-level conflicts.
/// Contains the conflicted merge state that can be materialized into working copy.
/// </summary>
public readonly record struct ConflictData : IContentHashable
{
    /// <summary>
    /// The conflicted merge containing the ancestor and conflicting sides.
    /// </summary>
    public Merge<TreeValue?> ConflictedMerge { get; }

    /// <summary>
    /// Creates a new ConflictData with the specified conflicted merge.
    /// </summary>
    public ConflictData(Merge<TreeValue?> conflictedMerge)
    {
        if (!conflictedMerge.IsConflicted)
        {
            throw new ArgumentException("ConflictData requires a conflicted merge", nameof(conflictedMerge));
        }
        ConflictedMerge = conflictedMerge;
    }

    /// <summary>
    /// Gets the canonical byte representation for content hashing.
    /// Conflict data is hashed with a "conflict:" prefix followed by the merge data.
    /// </summary>
    public byte[] GetBytesForHashing()
    {
        var builder = new StringBuilder();
        builder.Append("conflict:");
        
        // Serialize removes
        builder.Append("removes:");
        foreach (var remove in ConflictedMerge.Removes)
        {
            if (remove.HasValue)
            {
                var value = remove.Value;
                builder.Append($"{value.Type}:{value.ObjectId.ToHexString()}");
            }
            else
            {
                builder.Append("null");
            }
            builder.Append(",");
        }
        
        // Serialize adds
        builder.Append("adds:");
        foreach (var add in ConflictedMerge.Adds)
        {
            if (add.HasValue)
            {
                var value = add.Value;
                builder.Append($"{value.Type}:{value.ObjectId.ToHexString()}");
            }
            else
            {
                builder.Append("null");
            }
            builder.Append(",");
        }
        
        return Encoding.UTF8.GetBytes(builder.ToString());
    }
}
