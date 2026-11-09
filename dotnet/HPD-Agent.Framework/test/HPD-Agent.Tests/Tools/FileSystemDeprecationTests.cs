using System.Reflection;

#pragma warning disable CS0618 // This test verifies the deliberate deprecation contract.
using HPD.Agent.ToolHarness.FileSystem;

namespace HPD.Agent.Tests.Tools;

public sealed class FileSystemDeprecationTests
{
    [Fact]
    public void PublicFileSystemTypes_EmitMigrationWarning()
    {
        AssertDeprecated(typeof(FileSystemTools));
        AssertDeprecated(typeof(FileSystemContext));
    }

    private static void AssertDeprecated(Type type)
    {
        var obsolete = Assert.Single(type.GetCustomAttributes<ObsoleteAttribute>());
        Assert.False(obsolete.IsError);
        Assert.Contains("deprecated", obsolete.Message);
        Assert.Contains("not a supported", obsolete.Message);
        Assert.Contains("DEPRECATION.md", obsolete.Message);
    }
}
#pragma warning restore CS0618
