namespace HPD.Auth.Admin.Models;

/// <summary>
/// Paginated list of users returned by the admin list endpoint.
/// </summary>
public record AdminUserListResponse(
    IReadOnlyList<AdminUserResponse> Users,
    int Total,
    int Page,
    int PerPage,
    int TotalPages
);
