using HPD.AI.Platform.Studio;
using Xunit;

namespace HPD.AI.Platform.Tests;

public sealed class BaseStudioAuthorityContractTests
{
    [Fact]
    public void Response_authority_is_ordered_deeply_owned_and_deterministic()
    {
        BaseStudioStoreAuthority first = BaseStudioStoreAuthority.Create("primary", 2, 1, 3, Digest(1));
        DateTimeOffset issued = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);
        DateTimeOffset sessionExpiry = issued.AddMinutes(10);
        BaseStudioResponseAuthority left = BaseStudioResponseAuthority.Create(
            1, Digest(2), Digest(3), 4, Digest(4), 5, Digest(5), 6, Digest(6), [first], issued, sessionExpiry, []);
        BaseStudioResponseAuthority right = BaseStudioResponseAuthority.Create(
            1, Digest(2), Digest(3), 4, Digest(4), 5, Digest(5), 6, Digest(6), [first], issued, sessionExpiry, []);

        Assert.True(BaseStudioSha256.FixedTimeEquals(left.Checksum, right.Checksum));
        Assert.Equal("2026-08-22T12:00:30.0000000Z", BaseStudioResponseAuthority.CanonicalUtc(left.AuthorizedThroughUtc));
    }

    [Fact]
    public void Response_authority_rejects_noncanonical_store_order_and_non_utc_expiry()
    {
        BaseStudioStoreAuthority a = BaseStudioStoreAuthority.Create("a", 1, 0, 0, Digest(1));
        BaseStudioStoreAuthority z = BaseStudioStoreAuthority.Create("z", 1, 0, 0, Digest(1));
        Assert.Throws<ArgumentException>(() => BaseStudioResponseAuthority.Create(
            1, Digest(2), Digest(3), 4, Digest(4), 5, Digest(5), 6, Digest(6), [z, a],
            new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 8, 22, 13, 0, 0, TimeSpan.Zero), []));
        Assert.Throws<ArgumentException>(() => BaseStudioResponseAuthority.Create(
            1, Digest(2), Digest(3), 4, Digest(4), 5, Digest(5), 6, Digest(6), [a],
            new DateTimeOffset(2026, 8, 22, 12, 0, 0, TimeSpan.FromHours(1)),
            new DateTimeOffset(2026, 8, 22, 13, 0, 0, TimeSpan.Zero), []));
    }

    [Fact]
    public void Bootstrap_request_rejects_substituted_or_noncanonical_client_shape()
    {
        BaseStudioBootstrapRequest request = BaseStudioBootstrapRequest.Create(
            Digest(1), Digest(2), Digest(3), "en-US",
            [BaseStudioBrowserCapability.History, BaseStudioBrowserCapability.ModuleScripts]);
        Assert.Equal("en-US", request.Locale);
        Assert.Throws<ArgumentException>(() => BaseStudioBootstrapRequest.Create(
            Digest(1), Digest(2), Digest(3), "en-US",
            [BaseStudioBrowserCapability.ModuleScripts, BaseStudioBrowserCapability.History]));
        Assert.Throws<ArgumentException>(() => BaseStudioBootstrapRequest.Create(
            Digest(1), Digest(2), Digest(3), "../../locale", [BaseStudioBrowserCapability.History]));
    }

    [Fact]
    public void Authorization_lease_intersects_session_grant_and_thirty_second_ceiling()
    {
        DateTimeOffset issued = new(2026, 8, 22, 12, 0, 0, TimeSpan.Zero);
        BaseStudioResponseAuthority value = BaseStudioResponseAuthority.Create(
            1, Digest(2), Digest(3), 4, Digest(4), 5, Digest(5), 6, Digest(6), [],
            issued, issued.AddSeconds(20), [issued.AddSeconds(10)]);
        Assert.Equal(issued.AddSeconds(10), value.AuthorizedThroughUtc);
    }

    private static BaseStudioSha256 Digest(byte value) => BaseStudioSha256.Compute(Enumerable.Repeat(value, 32).ToArray());
}
