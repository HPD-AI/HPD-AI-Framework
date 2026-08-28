using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Core.Models;
using System.Security.Cryptography;

namespace HPD.Auth.Authentication.Tests.Helpers;

/// <summary>
/// Simple in-memory implementation of IRefreshTokenStore used in unit tests.
/// Not thread-safe — only use within a single test.
/// </summary>
internal sealed class InMemoryRefreshTokenStore : IRefreshTokenStore
{
    private readonly List<RefreshToken> _tokens = new();

    public Task<RefreshTokenPersistenceResult> IssueAsync(RefreshTokenIssueRequest request, CancellationToken ct = default)
    {
        string tokenValue = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        string jwtId = Guid.NewGuid().ToString();
        _tokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(), Token = tokenValue, UserId = request.UserId, JwtId = jwtId,
            SecurityStamp = request.SecurityStamp, ExpiresAt = request.ExpiresAt.UtcDateTime,
            CreatedAt = DateTime.UtcNow,
        });
        return Task.FromResult(new RefreshTokenPersistenceResult
        {
            Token = tokenValue, UserId = request.UserId, JwtId = jwtId, ExpiresAt = request.ExpiresAt,
        });
    }

    public Task<RefreshTokenInspection?> InspectAsync(string token, CancellationToken ct = default)
    {
        RefreshToken? found = _tokens.FirstOrDefault(candidate => candidate.Token == token);
        return Task.FromResult(found is null || found.IsUsed || found.IsRevoked || found.ExpiresAt <= DateTime.UtcNow
            ? null
            : new RefreshTokenInspection { UserId = found.UserId, ExpiresAt = new DateTimeOffset(found.ExpiresAt, TimeSpan.Zero) });
    }

    public Task<RefreshTokenPersistenceResult?> RotateAsync(RefreshTokenRotateRequest request, CancellationToken ct = default)
    {
        RefreshToken? predecessor = _tokens.FirstOrDefault(candidate => candidate.Token == request.PredecessorToken);
        if (predecessor is null || predecessor.IsUsed || predecessor.IsRevoked
            || predecessor.ExpiresAt <= DateTime.UtcNow || predecessor.SecurityStamp != request.SecurityStamp)
            return Task.FromResult<RefreshTokenPersistenceResult?>(null);
        predecessor.IsUsed = true;
        string tokenValue = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64));
        string jwtId = Guid.NewGuid().ToString();
        _tokens.Add(new RefreshToken
        {
            Id = Guid.NewGuid(), Token = tokenValue, UserId = predecessor.UserId,
            InstanceId = predecessor.InstanceId, JwtId = jwtId, SecurityStamp = request.SecurityStamp,
            ExpiresAt = request.ExpiresAt.UtcDateTime, CreatedAt = DateTime.UtcNow,
        });
        return Task.FromResult<RefreshTokenPersistenceResult?>(new RefreshTokenPersistenceResult
        {
            Token = tokenValue, UserId = predecessor.UserId, JwtId = jwtId, ExpiresAt = request.ExpiresAt,
        });
    }

    public Task<bool> RevokeAsync(string token, CancellationToken ct = default)
    {
        RefreshToken? found = _tokens.FirstOrDefault(candidate => candidate.Token == token);
        if (found is null)
            return Task.FromResult(false);
        found.IsRevoked = true;
        found.RevokedAt = DateTime.UtcNow;
        return Task.FromResult(true);
    }

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
    internal RefreshToken? Find(string token) => _tokens.FirstOrDefault(candidate => candidate.Token == token);
}

internal static class RefreshTokenStoreTestInspectionExtensions
{
    internal static Task<RefreshToken?> GetByTokenAsync(
        this IRefreshTokenStore store,
        string token,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(((InMemoryRefreshTokenStore)store).Find(token));
    }

    internal static Task UpdateAsync(
        this IRefreshTokenStore store,
        RefreshToken token,
        CancellationToken ct = default)
    {
        ct.ThrowIfCancellationRequested();
        _ = (InMemoryRefreshTokenStore)store;
        _ = token;
        return Task.CompletedTask;
    }
}
