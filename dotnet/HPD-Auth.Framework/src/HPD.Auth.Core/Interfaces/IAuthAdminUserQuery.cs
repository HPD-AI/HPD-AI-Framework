using HPD.Auth.Core.Entities;

namespace HPD.Auth.Core.Interfaces;

/// <summary>Executes the closed bounded administrative user-query contract.</summary>
public interface IAuthAdminUserQuery
{
    /// <summary>Returns one authorized page and its same-snapshot total.</summary>
    Task<AuthAdminUserQueryResult> ExecuteAsync(
        AuthAdminUserQuery request,
        CancellationToken cancellationToken = default);
}

/// <summary>Identifies the closed administrative user sort key.</summary>
public enum AuthAdminUserSort
{
    /// <summary>Sorts by creation instant.</summary>
    CreatedAt,
    /// <summary>Sorts by email.</summary>
    Email,
    /// <summary>Sorts by last successful login instant.</summary>
    LastLoginAt,
}

/// <summary>Identifies the closed administrative sort direction.</summary>
public enum AuthAdminSortDirection
{
    /// <summary>Ascending order.</summary>
    Ascending,
    /// <summary>Descending order.</summary>
    Descending,
}

/// <summary>Contains one bounded administrative user-query request.</summary>
public sealed record AuthAdminUserQuery
{
    /// <summary>Gets the optional ordinal substring searched across identity display fields.</summary>
    public string? Search { get; init; }
    /// <summary>Gets the optional ordinal email substring.</summary>
    public string? Email { get; init; }
    /// <summary>Gets the optional email-confirmation filter.</summary>
    public bool? EmailVerified { get; init; }
    /// <summary>Gets the optional enabled-state filter.</summary>
    public bool? Enabled { get; init; }
    /// <summary>Gets the optional role name filter.</summary>
    public string? Role { get; init; }
    /// <summary>Gets the zero-based bounded result offset.</summary>
    public required int Offset { get; init; }
    /// <summary>Gets the requested page size.</summary>
    public required int Limit { get; init; }
    /// <summary>Gets the closed sort key.</summary>
    public required AuthAdminUserSort Sort { get; init; }
    /// <summary>Gets the closed sort direction.</summary>
    public required AuthAdminSortDirection Direction { get; init; }
}

/// <summary>Contains one administrative user page captured from one Base snapshot.</summary>
public sealed record AuthAdminUserQueryResult
{
    /// <summary>Gets freshly owned detached users.</summary>
    public required IReadOnlyList<ApplicationUser> Users { get; init; }
    /// <summary>Gets the total number of matching users.</summary>
    public required long Total { get; init; }
}
