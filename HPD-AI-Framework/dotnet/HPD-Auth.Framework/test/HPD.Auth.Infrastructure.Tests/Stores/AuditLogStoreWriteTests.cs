using FluentAssertions;
using HPD.Auth.Core.Entities;
using HPD.Auth.Core.Interfaces;
using HPD.Auth.Infrastructure.Data;
using HPD.Auth.Infrastructure.Stores;
using HPD.Auth.Infrastructure.Tests.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace HPD.Auth.Infrastructure.Tests.Stores;

/// <summary>
/// Section 6: AuditLogStore — Write Behavior
/// </summary>
public class AuditLogStoreWriteTests
{
    private static AuditLogStore CreateStore(HPDAuthDbContext ctx)
        => new(ctx, NullLogger<AuditLogStore>.Instance);

    // ── 6.1 Log entry is persisted after LogAsync ────────────────────────────

    [Fact]
    public async Task LogAsync_PersistsEntry()
    {
        await using var ctx = HPDAuthDbContextFactory.CreateIsolated();
        var store = CreateStore(ctx);
        var userId = Guid.NewGuid();

        await store.LogAsync(new AuditLogEntry("user.login", "authentication", Success: true, UserId: userId));

        var logs = await ctx.AuditLogs.IgnoreQueryFilters().ToListAsync();
        logs.Should().HaveCount(1);
        logs[0].Action.Should().Be("user.login");
        logs[0].Category.Should().Be("authentication");
        logs[0].Success.Should().BeTrue();
        logs[0].UserId.Should().Be(userId);
    }

    // ── 6.2 LogAsync never throws on exception ───────────────────────────────

    [Fact]
    public async Task LogAsync_DisposedContext_DoesNotThrow()
    {
        var ctx = HPDAuthDbContextFactory.CreateIsolated();
        var store = CreateStore(ctx);
        await ctx.DisposeAsync();

        var act = async () => await store.LogAsync(new AuditLogEntry("test", "test"));
        await act.Should().NotThrowAsync();
    }

    // ── 6.3 LogAsync serializes object Metadata to JSON ──────────────────────

    [Fact]
    public async Task LogAsync_ObjectMetadata_SerializedToJson()
    {
        await using var ctx = HPDAuthDbContextFactory.CreateIsolated();
        var store = CreateStore(ctx);

        await store.LogAsync(new AuditLogEntry(
            "user.login",
            "authentication",
            Metadata: new { IpCountry = "US", DeviceId = "abc123" }));

        var log = await ctx.AuditLogs.IgnoreQueryFilters().FirstAsync();
        log.Metadata.Should().Contain("IpCountry");
        log.Metadata.Should().Contain("US");
        log.Metadata.Should().Contain("DeviceId");
        log.Metadata.Should().Contain("abc123");
    }

    // ── 6.4 LogAsync handles null Metadata gracefully ─────────────────────────

    [Fact]
    public async Task LogAsync_NullMetadata_StoresEmptyJsonObject()
    {
        await using var ctx = HPDAuthDbContextFactory.CreateIsolated();
        var store = CreateStore(ctx);

        await store.LogAsync(new AuditLogEntry("test.action", "test", Metadata: null));

        var log = await ctx.AuditLogs.IgnoreQueryFilters().FirstAsync();
        log.Metadata.Should().Be("{}");
    }

    // ── 6.5 LogAsync handles string Metadata pass-through ────────────────────

    [Fact]
    public async Task LogAsync_StringMetadata_NotDoubleSerialised()
    {
        await using var ctx = HPDAuthDbContextFactory.CreateIsolated();
        var store = CreateStore(ctx);

        var json = "{\"key\":\"value\"}";
        await store.LogAsync(new AuditLogEntry("test.action", "test", Metadata: json));

        var log = await ctx.AuditLogs.IgnoreQueryFilters().FirstAsync();
        log.Metadata.Should().Be(json);
    }

    // ── 6.6 AuditLog entries survive context disposal ────────────────────────

    [Fact]
    public async Task LogAsync_EntryPersists_AfterContextDisposal()
    {
        var dbName = Guid.NewGuid().ToString();

        await using (var ctx = HPDAuthDbContextFactory.CreateInMemory(dbName))
        {
            var store = CreateStore(ctx);
            await store.LogAsync(new AuditLogEntry("user.login", "authentication"));
        }

        await using var ctx2 = HPDAuthDbContextFactory.CreateInMemory(dbName);
        var logs = await ctx2.AuditLogs.IgnoreQueryFilters().ToListAsync();
        logs.Should().HaveCount(1);
        logs[0].Action.Should().Be("user.login");
    }
}
