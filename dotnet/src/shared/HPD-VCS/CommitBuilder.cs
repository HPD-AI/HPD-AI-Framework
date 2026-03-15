using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HPD.VCS.Core;
using HPD.VCS.Storage;

namespace HPD.VCS;

/// <summary>
/// Builder pattern for creating or rewriting commits within a transaction.
/// Provides a fluent interface for configuring commit properties.
/// </summary>
public class CommitBuilder
{
    private readonly Transaction _transaction;
    private readonly CommitData? _baseCommit;
    private TreeId? _treeId;
    private List<CommitId>? _parentIds;
    private ChangeId? _changeId;
    private Signature? _author;
    private Signature? _committer;
    private string? _description;

    /// <summary>
    /// Initializes a new CommitBuilder for rewriting an existing commit.
    /// </summary>
    /// <param name="transaction">The transaction this builder belongs to</param>
    /// <param name="baseCommit">The commit to rewrite</param>
    internal CommitBuilder(Transaction transaction, CommitData baseCommit)
    {
        _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
        _baseCommit = baseCommit;

        // Initialize with values from the base commit
        _treeId = baseCommit.RootTreeId;
        _parentIds = new List<CommitId>(baseCommit.ParentIds);
        _changeId = baseCommit.AssociatedChangeId;
        _author = baseCommit.Author;
        _committer = baseCommit.Committer;
        _description = baseCommit.Description;
    }

    /// <summary>
    /// Initializes a new CommitBuilder for creating a new commit.
    /// </summary>
    /// <param name="transaction">The transaction this builder belongs to</param>
    internal CommitBuilder(Transaction transaction)
    {
        _transaction = transaction ?? throw new ArgumentNullException(nameof(transaction));
        _baseCommit = null;

        // Initialize with defaults for new commit
        _parentIds = new List<CommitId>();
        var signature = transaction.Repository.UserSettings?.GetSignature() ?? 
                       new Signature("Unknown", "unknown@example.com", DateTimeOffset.UtcNow);
        _author = signature;
        _committer = signature;
        _description = "";
    }

    /// <summary>
    /// Sets the tree ID for the commit.
    /// </summary>
    /// <param name="treeId">The tree ID to set</param>
    /// <returns>This CommitBuilder for method chaining</returns>
    public CommitBuilder SetTreeId(TreeId treeId)
    {
        _treeId = treeId;
        return this;
    }

    /// <summary>
    /// Sets the parent commit IDs.
    /// </summary>
    /// <param name="parentIds">The parent commit IDs</param>
    /// <returns>This CommitBuilder for method chaining</returns>
    public CommitBuilder SetParents(IEnumerable<CommitId> parentIds)
    {
        ArgumentNullException.ThrowIfNull(parentIds);
        _parentIds = new List<CommitId>(parentIds);
        return this;
    }

    /// <summary>
    /// Sets the commit description (message).
    /// </summary>
    /// <param name="description">The commit description</param>
    /// <returns>This CommitBuilder for method chaining</returns>
    public CommitBuilder SetDescription(string description)
    {
        _description = description ?? throw new ArgumentNullException(nameof(description));
        return this;
    }    /// <summary>
    /// Sets the author signature.
    /// </summary>
    /// <param name="author">The author signature</param>
    /// <returns>This CommitBuilder for method chaining</returns>
    public CommitBuilder SetAuthor(Signature author)
    {
        _author = author;
        return this;
    }

    /// <summary>
    /// Sets the committer signature.
    /// </summary>
    /// <param name="committer">The committer signature</param>
    /// <returns>This CommitBuilder for method chaining</returns>
    public CommitBuilder SetCommitter(Signature committer)
    {
        _committer = committer;
        return this;
    }

    /// <summary>
    /// Sets the change ID for the commit.
    /// </summary>
    /// <param name="changeId">The change ID</param>
    /// <returns>This CommitBuilder for method chaining</returns>
    public CommitBuilder SetChangeId(ChangeId changeId)
    {
        _changeId = changeId;
        return this;
    }

    /// <summary>
    /// Gets the current tree ID.
    /// </summary>
    public TreeId? TreeId => _treeId;

    /// <summary>
    /// Gets the current parent IDs.
    /// </summary>
    public IReadOnlyList<CommitId> ParentIds => _parentIds?.AsReadOnly() ?? new List<CommitId>().AsReadOnly();

    /// <summary>
    /// Gets the current description.
    /// </summary>
    public string? Description => _description;

    /// <summary>
    /// Writes the commit to the object store and registers the rewrite mapping in the transaction.
    /// </summary>
    /// <returns>The commit data for the newly written commit</returns>
    /// <exception cref="InvalidOperationException">Thrown when required properties are not set</exception>
    public async Task<CommitData> WriteAsync()
    {        // Validate required properties
        if (_treeId == null)
        {
            throw new InvalidOperationException("Tree ID must be set before writing commit");
        }
        if (_parentIds == null)
        {
            throw new InvalidOperationException("Parent IDs must be set before writing commit");
        }
        if (!_author.HasValue)
        {
            throw new InvalidOperationException("Author must be set before writing commit");
        }
        if (!_committer.HasValue)
        {
            throw new InvalidOperationException("Committer must be set before writing commit");
        }
        if (_description == null)
        {
            throw new InvalidOperationException("Description must be set before writing commit");
        }

        // Generate change ID if not set
        if (_changeId == null)
        {
            var changeContent = $"{_description}\n{Guid.NewGuid()}\n{_author.Value.Name}";
            _changeId = SimpleContentHashable.CreateChangeId(changeContent);
        }

        // Create the commit data
        var commitData = new CommitData(
            rootTreeId: _treeId.Value,
            parentIds: _parentIds,
            associatedChangeId: _changeId.Value,
            author: _author.Value,
            committer: _committer.Value,
            description: _description
        );

        // Write to object store
        var newCommitId = await _transaction.Repository.ObjectStore.WriteCommitAsync(commitData);

        // Create the final commit data with the actual ID
        var finalCommitData = new CommitData(
            rootTreeId: _treeId.Value,
            parentIds: _parentIds,
            associatedChangeId: _changeId.Value,
            author: _author.Value,
            committer: _committer.Value,
            description: _description
        );

        // Register the rewrite mapping if this was a rewrite
        if (_baseCommit.HasValue)
        {
            var baseCommitId = ObjectHasher.ComputeCommitId(_baseCommit.Value);
            _transaction.RegisterRewrite(baseCommitId, newCommitId);
        }

        return finalCommitData;
    }
}
