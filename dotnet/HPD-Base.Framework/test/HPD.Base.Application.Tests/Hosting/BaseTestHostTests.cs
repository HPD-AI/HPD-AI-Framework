using FluentAssertions;
using HPD.Base.Application.Tests.Generation;
using HPD.Base.Testing;
using Xunit;

namespace HPD.Base.Application.Tests.Hosting;

public sealed class BaseTestHostTests
{
    [Fact]
    public async Task TestHostOwnsDeterministicTimeAndTypedSessions()
    {
        DateTimeOffset initial = new(2030, 4, 5, 6, 7, 8, TimeSpan.Zero);
        await using BaseTestHost host = await BaseTestHost.CreateAsync(
            builder => builder
                .UseInMemory()
                .AddCollection(GeneratedProject.Collection),
            initial);

        host.Time.GetUtcNow().Should().Be(initial);
        host.Time.Advance(TimeSpan.FromMinutes(2));
        host.Time.GetUtcNow().Should().Be(initial.AddMinutes(2));

        host.Session(BaseTestPrincipal.System("application-test"))
            .Collection(GeneratedProject.Collection)
            .Should().NotBeNull();
    }
}
