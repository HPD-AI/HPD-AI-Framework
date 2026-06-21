namespace HPD.ML.Serialization.Zip.Tests;

using HPD.ML.Abstractions;

public class ManifestTests
{
    [Fact]
    public void Defaults_AreCorrect()
    {
        var manifest = new Manifest();
        Assert.Equal("hpd-ml-zip-v1", manifest.FormatId);
        Assert.Equal(1, manifest.SchemaVersion);
        Assert.Null(manifest.ParameterType);
        Assert.Null(manifest.Pipeline);
        Assert.False(manifest.HasInferenceState);
    }

    [Fact]
    public void InitProperties_SetCorrectly()
    {
        var manifest = new Manifest
        {
            Content = SaveContent.All,
            SavedAtUtc = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc),
            ParameterType = "TestParams",
            HasInferenceState = true,
            Pipeline = [new TransformEntry { TypeName = "Foo" }]
        };

        Assert.Equal(SaveContent.All, manifest.Content);
        Assert.Equal("TestParams", manifest.ParameterType);
        Assert.True(manifest.HasInferenceState);
        Assert.Single(manifest.Pipeline!);
    }
}
