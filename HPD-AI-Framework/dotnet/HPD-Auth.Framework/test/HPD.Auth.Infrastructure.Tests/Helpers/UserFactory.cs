using HPD.Auth.Core.Entities;
using HPD.Auth.Infrastructure.Data;

namespace HPD.Auth.Infrastructure.Tests.Helpers;

/// <summary>
/// Convenience factory for creating test entities.
/// </summary>
public static class UserFactory
{
    /// <summary>
    /// Seeds a user row into <paramref name="ctx"/> with the given <paramref name="id"/> and saves.
    /// Required when FK enforcement is enabled so sessions/tokens referencing this user don't fail.
    /// </summary>
    public static async Task SeedUserAsync(HPDAuthDbContext ctx, Guid id, Guid? instanceId = null)
    {
        var user = CreateUser(instanceId);
        user.Id = id;
        ctx.Users.Add(user);
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
    }

    public static ApplicationUser CreateUser(Guid? instanceId = null, string? email = null)
    {
        var id = Guid.NewGuid();
        var e = email ?? $"user-{id:N}@test.com";
        return new ApplicationUser
        {
            Id = id,
            InstanceId = instanceId ?? Guid.Empty,
            UserName = e,
            NormalizedUserName = e.ToUpperInvariant(),
            Email = e,
            NormalizedEmail = e.ToUpperInvariant(),
        };
    }

    public static RefreshToken CreateRefreshToken(Guid userId, Guid? instanceId = null, string? token = null)
    {
        return new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            InstanceId = instanceId ?? Guid.Empty,
            Token = token ?? Guid.NewGuid().ToString("N"),
            JwtId = Guid.NewGuid().ToString("N"),
            ExpiresAt = DateTime.UtcNow.AddDays(7),
            CreatedAt = DateTime.UtcNow,
        };
    }
}
