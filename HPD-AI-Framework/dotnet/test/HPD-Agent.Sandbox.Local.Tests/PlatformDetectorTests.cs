using FluentAssertions;
using HPD.Sandbox.Local.Platforms;
using System.Runtime.InteropServices;
using Xunit;

namespace HPD.Sandbox.Local.Tests;

public class PlatformDetectorTests
{
    [Fact]
    public void Current_ReturnsValidPlatform()
    {
        var platform = PlatformDetector.Current;

        platform.Should().BeOneOf(PlatformType.Linux, PlatformType.MacOS, PlatformType.Windows);
    }

    [Fact]
    public void Current_IsCached()
    {
        var first = PlatformDetector.Current;
        var second = PlatformDetector.Current;

        first.Should().Be(second);
    }

    [Fact]
    public void Current_MatchesRuntimeInformation()
    {
        var platform = PlatformDetector.Current;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            platform.Should().Be(PlatformType.Linux);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            platform.Should().Be(PlatformType.MacOS);
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            platform.Should().Be(PlatformType.Windows);
    }

    [Fact]
    public void PlatformType_HasExpectedValues()
    {
        Enum.GetValues<PlatformType>().Should().HaveCount(3);
        Enum.IsDefined(PlatformType.Linux).Should().BeTrue();
        Enum.IsDefined(PlatformType.MacOS).Should().BeTrue();
        Enum.IsDefined(PlatformType.Windows).Should().BeTrue();
    }
}
