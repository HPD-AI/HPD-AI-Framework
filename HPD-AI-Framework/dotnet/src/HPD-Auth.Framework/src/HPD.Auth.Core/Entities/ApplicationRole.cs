using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;

namespace HPD.Auth.Core.Entities;

/// <summary>
/// HPD role entity extending ASP.NET Core Identity.
/// v2.2: Added InstanceId for multi-tenancy.
/// </summary>
public class ApplicationRole : IdentityRole<Guid>
{
    /// <summary>
    /// Multi-tenancy discriminator. Defaults to Guid.Empty for single-tenant apps.
    /// </summary>
    public Guid InstanceId { get; set; } = Guid.Empty;

    [MaxLength(500)]
    public string? Description { get; set; }

    public DateTime Created { get; set; } = DateTime.UtcNow;

    public ApplicationRole() : base() { }

    public ApplicationRole(string roleName) : base(roleName) { }
}
