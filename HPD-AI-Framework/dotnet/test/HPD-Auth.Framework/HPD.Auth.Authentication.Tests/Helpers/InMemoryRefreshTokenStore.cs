using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;

namespace HPD.Auth.Authentication.Tests.Helpers;

/// <summary>
/// Simple in-memory implementation of IRefreshTokenStore used in unit tests.
/// Not thread-safe — only use within a single test.
/// </summary>
internal sealed class InMemoryRefreshTokenStore : IRefreshTokenStore
{
    private readonly List<RefreshToken> _tokens = new();

    public Task<RefreshToken?> GetByTokenAsync(string token, CancellationToken ct = default)
        => Task.FromResult(_tokens.FirstOrDefault(t => t.Token == token));

    public Task CreateAsync(RefreshToken token, CancellationToken ct = default)
    {
        _tokens.Add(token);
        return Task.CompletedTask;
    }

    public Task UpdateAsync(RefreshToken token, CancellationToken ct = default)
        // Entity is updated in-place since we hold a reference — nothing to do.
        => Task.CompletedTask;

    public Task RevokeAllForUserAsync(Guid userId, CancellationToken ct = default)
    {
        var now = DateTime.UtcNow;
        foreach (var t in _tokens.Where(t => t.UserId == userId && !t.IsRevoked))
        {
            t.IsRevoked = true;
            t.RevokedAt = now;
        }
        return Task.CompletedTask;
    }

    // Test helpers
    public IReadOnlyList<RefreshToken> All => _tokens.AsReadOnly();
    public IReadOnlyList<RefreshToken> ForUser(Guid userId) => _tokens.Where(t => t.UserId == userId).ToList();
}
