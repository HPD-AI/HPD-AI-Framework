namespace HPD.Agent;

/// <summary>
/// Query filters for content search (Phase 1 - simplified).
/// All fields are optional - null = no filter on that dimension.
/// Multiple filters are combined with AND logic.
/// </summary>
/// <remarks>
/// <para><b>Phase 1 Implementation:</b></para>
/// <para>
/// Initial version supports basic filtering only. Advanced features (wildcards,
/// full-text search, pagination, sorting) will be added in future phases after
/// validating the core design.
/// </para>
///
/// <para><b>Example Usage:</b></para>
/// <code>
/// // All content
/// var all = await store.QueryAsync();
///
/// // Recent content (last 7 days)
/// var recent = await store.QueryAsync(new ContentQuery {
///     CreatedAfter = DateTime.UtcNow.AddDays(-7)
/// });
///
/// // Images only (exact match)
/// var images = await store.QueryAsync(new ContentQuery {
///     ContentType = "image/jpeg"
/// });
///
/// // Recent images (combined filters)
/// var recentImages = await store.QueryAsync(new ContentQuery {
///     ContentType = "image/jpeg",
///     CreatedAfter = DateTime.UtcNow.AddDays(-7)
/// });
///
/// // First 10 results
/// var top10 = await store.QueryAsync(new ContentQuery {
///     Limit = 10
/// });
/// </code>
/// </remarks>
public record ContentQuery
{
    /// <summary>
    /// Filter by content type (exact match only in Phase 1).
    /// Example: "image/jpeg", "text/plain", "application/pdf"
    /// </summary>
    /// <remarks>
    /// <b>Future Phase 2+:</b> May support wildcards like "image/*"
    /// </remarks>
    public string? ContentType { get; init; }

    /// <summary>
    /// Filter by created date (inclusive).
    /// Returns content created on or after this date.
    /// </summary>
    public DateTime? CreatedAfter { get; init; }

    /// <summary>
    /// Maximum number of results to return.
    /// Null = no limit.
    /// </summary>
    public int? Limit { get; init; }

    /// <summary>
    /// Filter by tags (must match ALL specified tags — AND logic).
    /// Used for folder-based filtering: {"folder": "/knowledge"}, {"folder": "/uploads"}.
    /// Example: {"folder": "/knowledge", "skill": "finance"} — only content in /knowledge tagged with skill=finance.
    /// </summary>
    /// <remarks>
    /// This is the primary mechanism for folder navigation in V3.
    /// All stored content is tagged with ["folder"] = "/foldername" to enable path-based queries.
    /// </remarks>
    public IReadOnlyDictionary<string, string>? Tags { get; init; }

    /// <summary>
    /// Filter by content name (exact match or prefix).
    /// Used to look up a specific file within a folder: Name = "api-docs.md".
    /// Null = return all content in scope (subject to other filters).
    /// </summary>
    public string? Name { get; init; }

    // ═══════════════════════════════════════════════════════════════════
    // Future Phase 2+ Fields (Commented Out For Now)
    // ═══════════════════════════════════════════════════════════════════
    //
    // These fields will be added in future phases after validating the core design:
    //
    // /// <summary>
    // /// Filter by content origin (User, Agent, System).
    // /// </summary>
    // public ContentSource? Origin { get; init; }
    //
    // /// <summary>
    // /// Full-text search across content and metadata.
    // /// Implementation varies by store (some may not support text search).
    // /// </summary>
    // public string? TextSearch { get; init; }
    //
    // /// <summary>
    // /// Filter by created date (inclusive).
    // /// Returns content created on or before this date.
    // /// </summary>
    // public DateTime? CreatedBefore { get; init; }
    //
    // /// <summary>
    // /// Filter by last accessed date (inclusive).
    // /// Returns content accessed on or after this date.
    // /// </summary>
    // public DateTime? AccessedAfter { get; init; }
    //
    // /// <summary>
    // /// Number of results to skip (for pagination).
    // /// </summary>
    // public int Offset { get; init; } = 0;
    //
    // /// <summary>
    // /// Sort order for results.
    // /// </summary>
    // public ContentSortOrder SortBy { get; init; } = ContentSortOrder.CreatedAtDescending;
}

// ═══════════════════════════════════════════════════════════════════
// Future Phase 2+ Enums (Commented Out For Now)
// ═══════════════════════════════════════════════════════════════════
//
// /// <summary>
// /// Sort order options for content queries.
// /// </summary>
// public enum ContentSortOrder
// {
//     /// <summary>Sort by creation date, oldest first</summary>
//     CreatedAtAscending,
//
//     /// <summary>Sort by creation date, newest first</summary>
//     CreatedAtDescending,
//
//     /// <summary>Sort by last accessed date, least recently accessed first</summary>
//     LastAccessedAscending,
//
//     /// <summary>Sort by last accessed date, most recently accessed first</summary>
//     LastAccessedDescending,
//
//     /// <summary>Sort by name alphabetically, A-Z</summary>
//     NameAscending,
//
//     /// <summary>Sort by name alphabetically, Z-A</summary>
//     NameDescending,
//
//     /// <summary>Sort by size, smallest first</summary>
//     SizeAscending,
//
//     /// <summary>Sort by size, largest first</summary>
//     SizeDescending
// }
