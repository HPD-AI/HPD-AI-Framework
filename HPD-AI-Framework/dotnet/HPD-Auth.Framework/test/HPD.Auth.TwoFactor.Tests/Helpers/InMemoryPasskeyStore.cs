using HPD.Auth.Core.Entities;
using Microsoft.AspNetCore.Identity;
using System.Collections.Concurrent;

namespace HPD.Auth.TwoFactor.Tests.Helpers;

/// <summary>
/// In-memory passkey store for tests.
///
/// EF Core's in-memory provider does not properly materialise IdentityUserPasskey.Data
/// (a ComplexProperty configured with .ToJson() by IdentityDbContext Version3).
/// Registering this store after AddHPDAuth() overrides the EF-backed IUserPasskeyStore
/// so passkey CRUD in tests works correctly without touching EF Core complex type mappings.
/// </summary>
public sealed class InMemoryPasskeyStore : IUserPasskeyStore<ApplicationUser>
{
    // Key: userId → list of passkeys
    private readonly ConcurrentDictionary<Guid, List<PasskeyEntry>> _store = new();

    private record PasskeyEntry(
        byte[] CredentialId,
        byte[] PublicKey,
        string? Name,
        DateTimeOffset CreatedAt,
        uint SignCount,
        string[]? Transports,
        bool IsUserVerified,
        bool IsBackupEligible,
        bool IsBackedUp,
        byte[] AttestationObject,
        byte[] ClientDataJson)
    {
        public string? Name { get; set; } = Name;
        public uint SignCount { get; set; } = SignCount;
        public bool IsBackedUp { get; set; } = IsBackedUp;
        public bool IsUserVerified { get; set; } = IsUserVerified;
    }

    public Task AddOrUpdatePasskeyAsync(ApplicationUser user, UserPasskeyInfo passkey, CancellationToken ct)
    {
        var list = _store.GetOrAdd(user.Id, _ => new List<PasskeyEntry>());
        lock (list)
        {
            var existing = list.FirstOrDefault(p => p.CredentialId.SequenceEqual(passkey.CredentialId));
            if (existing != null)
            {
                existing.Name = passkey.Name;
                existing.SignCount = passkey.SignCount;
                existing.IsBackedUp = passkey.IsBackedUp;
                existing.IsUserVerified = passkey.IsUserVerified;
            }
            else
            {
                list.Add(new PasskeyEntry(
                    passkey.CredentialId,
                    passkey.PublicKey,
                    passkey.Name,
                    passkey.CreatedAt,
                    passkey.SignCount,
                    passkey.Transports,
                    passkey.IsUserVerified,
                    passkey.IsBackupEligible,
                    passkey.IsBackedUp,
                    passkey.AttestationObject,
                    passkey.ClientDataJson));
            }
        }
        return Task.CompletedTask;
    }

    public Task<IList<UserPasskeyInfo>> GetPasskeysAsync(ApplicationUser user, CancellationToken ct)
    {
        var list = _store.TryGetValue(user.Id, out var entries) ? entries : [];
        IList<UserPasskeyInfo> result;
        lock (list)
        {
            result = list.Select(ToInfo).ToList();
        }
        return Task.FromResult(result);
    }

    public Task<UserPasskeyInfo?> FindPasskeyAsync(ApplicationUser user, byte[] credentialId, CancellationToken ct)
    {
        if (!_store.TryGetValue(user.Id, out var list))
            return Task.FromResult<UserPasskeyInfo?>(null);
        lock (list)
        {
            return Task.FromResult(list.FirstOrDefault(p => p.CredentialId.SequenceEqual(credentialId)) is { } e ? ToInfo(e) : null);
        }
    }

    public Task<ApplicationUser?> FindByPasskeyIdAsync(byte[] credentialId, CancellationToken ct)
    {
        // Not needed by the endpoints under test — return null.
        return Task.FromResult<ApplicationUser?>(null);
    }

    public Task RemovePasskeyAsync(ApplicationUser user, byte[] credentialId, CancellationToken ct)
    {
        if (_store.TryGetValue(user.Id, out var list))
        {
            lock (list)
            {
                list.RemoveAll(p => p.CredentialId.SequenceEqual(credentialId));
            }
        }
        return Task.CompletedTask;
    }

    // ── IUserStore<ApplicationUser> passthrough (UserManager delegates here) ──

    public Task<string> GetUserIdAsync(ApplicationUser user, CancellationToken ct)
        => Task.FromResult(user.Id.ToString());

    public Task<string?> GetUserNameAsync(ApplicationUser user, CancellationToken ct)
        => Task.FromResult(user.UserName);

    public Task SetUserNameAsync(ApplicationUser user, string? userName, CancellationToken ct)
    {
        user.UserName = userName;
        return Task.CompletedTask;
    }

    public Task<string?> GetNormalizedUserNameAsync(ApplicationUser user, CancellationToken ct)
        => Task.FromResult(user.NormalizedUserName);

    public Task SetNormalizedUserNameAsync(ApplicationUser user, string? normalizedName, CancellationToken ct)
    {
        user.NormalizedUserName = normalizedName;
        return Task.CompletedTask;
    }

    public Task<IdentityResult> CreateAsync(ApplicationUser user, CancellationToken ct)
        => Task.FromResult(IdentityResult.Success);

    public Task<IdentityResult> UpdateAsync(ApplicationUser user, CancellationToken ct)
        => Task.FromResult(IdentityResult.Success);

    public Task<IdentityResult> DeleteAsync(ApplicationUser user, CancellationToken ct)
        => Task.FromResult(IdentityResult.Success);

    public Task<ApplicationUser?> FindByIdAsync(string userId, CancellationToken ct)
        => Task.FromResult<ApplicationUser?>(null);

    public Task<ApplicationUser?> FindByNameAsync(string normalizedUserName, CancellationToken ct)
        => Task.FromResult<ApplicationUser?>(null);

    public void Dispose() { }

    private static UserPasskeyInfo ToInfo(PasskeyEntry e) =>
        new(e.CredentialId, e.PublicKey, e.CreatedAt, e.SignCount,
            e.Transports, e.IsUserVerified, e.IsBackupEligible,
            e.IsBackedUp, e.AttestationObject, e.ClientDataJson)
        { Name = e.Name };
}
