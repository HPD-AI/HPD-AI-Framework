using FluentAssertions;
using HPD.Auth.Infrastructure.Data;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;

namespace HPD.Auth.Infrastructure.Tests.DbContext;

/// <summary>
/// Section 4: DbContext — DataProtection Keys Table
/// </summary>
public class DataProtectionKeysTests
{
    // ── 4.1 DataProtectionKeys table is accessible ───────────────────────────

    [Fact]
    public void DataProtectionKeys_DbSet_IsNotNull()
    {
        using var ctx = HPDAuthDbContextFactory.CreateIsolated();
        ctx.DataProtectionKeys.Should().NotBeNull();
    }

    // ── 4.2 DbContext implements IDataProtectionKeyContext ────────────────────

    [Fact]
    public void HPDAuthDbContext_ImplementsIDataProtectionKeyContext()
    {
        using var ctx = HPDAuthDbContextFactory.CreateIsolated();
        ctx.Should().BeAssignableTo<IDataProtectionKeyContext>();
    }

    // ── 4.3 DataProtectionKey can be written and read ─────────────────────────

    [Fact]
    public async Task DataProtectionKey_WriteAndRead_RoundTrips()
    {
        await using var ctx = HPDAuthDbContextFactory.CreateIsolated();

        var key = new DataProtectionKey
        {
            FriendlyName = "test-key-2024",
            Xml = "<key id=\"1\"><descriptor /><encryptedSecret /></key>",
        };

        ctx.DataProtectionKeys.Add(key);
        await ctx.SaveChangesAsync();

        var loaded = ctx.DataProtectionKeys.FirstOrDefault(k => k.FriendlyName == "test-key-2024");
        loaded.Should().NotBeNull();
        loaded!.Xml.Should().Be(key.Xml);
    }
}
