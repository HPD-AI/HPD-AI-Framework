using HPD.AI.Platform.Studio;
using Xunit;

namespace HPD.AI.Platform.Tests;

public sealed class StudioAssetContractTests
{
    [Fact]
    public void Manifest_is_ordered_deeply_owned_and_deterministic()
    {
        byte[] entryBytes = Enumerable.Repeat((byte)2, 100).ToArray();
        byte[] cssBytes = Enumerable.Repeat((byte)3, 50).ToArray();
        StudioAssetSource entry = StudioAssetSource.Create(
            "assets/module.js", StudioAssetMediaType.JavaScriptModule, entryBytes);
        StudioAssetSource css = StudioAssetSource.Create(
            "assets/module.css", StudioAssetMediaType.Css, cssBytes);

        StudioAssetManifest first = StudioAssetManifest.Create(
            "assets/module.js", StudioModuleNecessity.Required, StudioShellContract.Current, [css, entry]);
        StudioAssetManifest second = StudioAssetManifest.Create(
            "assets/module.js", StudioModuleNecessity.Required, StudioShellContract.Current, [css, entry]);

        entryBytes[0] = 99;
        cssBytes[0] = 99;
        Assert.Equal(["assets/module.css", "assets/module.js"], first.Assets.Select(static asset => asset.Path));
        Assert.True(StudioSha256.FixedTimeEquals(first.AssetGraphChecksum, second.AssetGraphChecksum));
        Assert.True(StudioSha256.FixedTimeEquals(
            first.ShellContractChecksum,
            StudioShellContract.Current.Checksum));
        Assert.True(StudioSha256.FixedTimeEquals(
            first.Assets[1].Digest,
            StudioSha256.Compute(Enumerable.Repeat((byte)2, 100).ToArray())));
        Assert.Throws<ArgumentException>(() => StudioAssetManifest.Create(
            "assets/module.js", StudioModuleNecessity.Required, StudioShellContract.Current, [entry, css]));
    }

    [Theory]
    [InlineData("/absolute.js")]
    [InlineData("../escape.js")]
    [InlineData("assets//module.js")]
    [InlineData("assets\\module.js")]
    public void Asset_paths_fail_closed(string path)
    {
        Assert.Throws<ArgumentException>(() => StudioAssetSource.Create(
            path, StudioAssetMediaType.JavaScriptModule, new byte[] { 1 }));
    }

    [Fact]
    public void Entry_module_must_be_declared_as_javascript()
    {
        StudioAssetSource css = StudioAssetSource.Create(
            "assets/module.css", StudioAssetMediaType.Css, new byte[] { 1 });
        Assert.Throws<ArgumentException>(() => StudioAssetManifest.Create(
            "assets/module.js", StudioModuleNecessity.Optional, StudioShellContract.Current, [css]));
    }

    [Theory]
    [InlineData("assets/module.js?alternate")]
    [InlineData("assets/module.js#fragment")]
    [InlineData("assets/%2e%2e/module.js")]
    [InlineData("assets/module.js.map")]
    public void Browser_special_and_source_map_paths_fail_closed(string path)
        => Assert.Throws<ArgumentException>(() => StudioAssetSource.Create(
            path, StudioAssetMediaType.JavaScriptModule, new byte[] { 1 }));
}
