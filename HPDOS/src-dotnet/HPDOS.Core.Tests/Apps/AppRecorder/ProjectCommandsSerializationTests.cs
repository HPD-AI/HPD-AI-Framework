using System.Text.Json;
using HPDOS.Apps.AppRecorder.Project;
using Xunit;

namespace HPDOS.Core.Tests.Apps.AppRecorder;

/// <summary>
/// Tests for polymorphic JSON serialization of all 19 ProjectCommand subtypes.
/// Uses ProjectJsonContext (source-generated) which sets camelCase + WriteIndented + WhenWritingNull.
/// </summary>
public class ProjectCommandsSerializationTests
{
    private static readonly JsonSerializerOptions Options = ProjectJsonContext.Default.Options;

    private static string Serialize(ProjectCommand cmd) =>
        JsonSerializer.Serialize(cmd, typeof(ProjectCommand), Options);

    private static ProjectCommand Deserialize(string json) =>
        JsonSerializer.Deserialize<ProjectCommand>(json, Options)!;

    private static T Roundtrip<T>(T cmd) where T : ProjectCommand =>
        (T)Deserialize(Serialize(cmd));

    // ── $type discriminator tests (#27) ──────────────────────────────────────

    [Theory]
    [InlineData(typeof(AddZoomRegion),      "add_zoom_region")]
    [InlineData(typeof(RemoveZoomRegion),   "remove_zoom_region")]
    [InlineData(typeof(AddTrimRegion),      "add_trim_region")]
    [InlineData(typeof(RemoveTrimRegion),   "remove_trim_region")]
    [InlineData(typeof(SetSpeed),           "set_speed")]
    [InlineData(typeof(SetBackground),      "set_background")]
    [InlineData(typeof(SetVisualOptions),   "set_visual_options")]
    [InlineData(typeof(AddAnnotation),      "add_annotation")]
    [InlineData(typeof(RemoveAnnotation),   "remove_annotation")]
    [InlineData(typeof(UpdateAnnotation),   "update_annotation")]
    [InlineData(typeof(AddCrop),            "add_crop")]
    [InlineData(typeof(RemoveCrop),         "remove_crop")]
    [InlineData(typeof(AddKeyframe),        "add_keyframe")]
    [InlineData(typeof(RemoveKeyframe),     "remove_keyframe")]
    [InlineData(typeof(AddSplitPoint),      "add_split_point")]
    [InlineData(typeof(RemoveSplitPoint),   "remove_split_point")]
    [InlineData(typeof(AddTransition),      "add_transition")]
    [InlineData(typeof(RemoveTransition),   "remove_transition")]
    [InlineData(typeof(UpdateTransition),   "update_transition")]
    public void Serialize_HasCorrectTypeDiscriminator(Type commandType, string expectedType)
    {
        var cmd = MakeInstance(commandType);
        var json = Serialize(cmd);
        Assert.Contains($"\"$type\": \"{expectedType}\"", json);
    }

    // ── Roundtrip tests (#28) ─────────────────────────────────────────────────

    [Fact]
    public void Roundtrip_AddZoomRegion()
    {
        var cmd = new AddZoomRegion(100, 2000, 1.5, 0.3, 0.7);
        var rt = Roundtrip(cmd);
        Assert.Equal(cmd, rt);
    }

    [Fact]
    public void Roundtrip_RemoveZoomRegion()
    {
        var cmd = new RemoveZoomRegion(100);
        Assert.Equal(cmd, Roundtrip(cmd));
    }

    [Fact]
    public void Roundtrip_AddTrimRegion()
    {
        var cmd = new AddTrimRegion(500, 1500);
        Assert.Equal(cmd, Roundtrip(cmd));
    }

    [Fact]
    public void Roundtrip_RemoveTrimRegion()
    {
        var cmd = new RemoveTrimRegion(500);
        Assert.Equal(cmd, Roundtrip(cmd));
    }

    [Fact]
    public void Roundtrip_SetSpeed()
    {
        var cmd = new SetSpeed(0, 3000, 2.0);
        Assert.Equal(cmd, Roundtrip(cmd));
    }

    [Fact]
    public void Roundtrip_SetBackground()
    {
        var cmd = new SetBackground(new BackgroundOptions(BackgroundKind.SolidColor, Color: "#ff0000"));
        Assert.Equal(cmd, Roundtrip(cmd));
    }

    [Fact]
    public void Roundtrip_SetVisualOptions()
    {
        var cmd = new SetVisualOptions(new VisualOptions(BorderRadius: 12, DropShadow: true));
        Assert.Equal(cmd, Roundtrip(cmd));
    }

    [Fact]
    public void Roundtrip_AddAnnotation()
    {
        var payload = new AnnotationPayload(AnnotationKind.Text, 0.1, 0.2, 0.3, 0.1, Text: "Hello");
        var cmd = new AddAnnotation("ann-1", 0, 5000, payload);
        Assert.Equal(cmd, Roundtrip(cmd));
    }

    [Fact]
    public void Roundtrip_RemoveAnnotation()
    {
        var cmd = new RemoveAnnotation("ann-1");
        Assert.Equal(cmd, Roundtrip(cmd));
    }

    [Fact]
    public void Roundtrip_UpdateAnnotation()
    {
        var payload = new AnnotationPayload(AnnotationKind.Arrow, 0.5, 0.5, 0.1, 0.1);
        var cmd = new UpdateAnnotation("ann-1", payload);
        Assert.Equal(cmd, Roundtrip(cmd));
    }

    [Fact]
    public void Roundtrip_AddCrop()
    {
        var cmd = new AddCrop(new CropOptions(0.1, 0.2, 0.8, 0.6));
        Assert.Equal(cmd, Roundtrip(cmd));
    }

    [Fact]
    public void Roundtrip_RemoveCrop()
    {
        var cmd = new RemoveCrop();
        Assert.Equal(cmd, Roundtrip(cmd));
    }

    [Fact]
    public void Roundtrip_AddKeyframe()
    {
        var cmd = new AddKeyframe("kf-1", 1234);
        Assert.Equal(cmd, Roundtrip(cmd));
    }

    [Fact]
    public void Roundtrip_RemoveKeyframe()
    {
        var cmd = new RemoveKeyframe("kf-1");
        Assert.Equal(cmd, Roundtrip(cmd));
    }

    [Fact]
    public void Roundtrip_AddSplitPoint()
    {
        var cmd = new AddSplitPoint(3000);
        Assert.Equal(cmd, Roundtrip(cmd));
    }

    [Fact]
    public void Roundtrip_RemoveSplitPoint()
    {
        var cmd = new RemoveSplitPoint(3000);
        Assert.Equal(cmd, Roundtrip(cmd));
    }

    [Fact]
    public void Roundtrip_AddTransition()
    {
        var cmd = new AddTransition("tr-1", 3000, "wipe-right", 500);
        Assert.Equal(cmd, Roundtrip(cmd));
    }

    [Fact]
    public void Roundtrip_RemoveTransition()
    {
        var cmd = new RemoveTransition("tr-1");
        Assert.Equal(cmd, Roundtrip(cmd));
    }

    [Fact]
    public void Roundtrip_UpdateTransition()
    {
        var cmd = new UpdateTransition("tr-1", "fade", 300);
        Assert.Equal(cmd, Roundtrip(cmd));
    }

    // ── TransactionId null omitted (#29) ──────────────────────────────────────

    [Fact]
    public void TransactionId_Null_IsOmittedFromJson()
    {
        var cmd = new AddZoomRegion(0, 1000, 1.5, 0.5, 0.5, null);
        var json = Serialize(cmd);
        Assert.DoesNotContain("transactionId", json);
    }

    // ── TransactionId present when set (#30) ──────────────────────────────────

    [Fact]
    public void TransactionId_WhenSet_AppearsInJson()
    {
        var cmd = new AddZoomRegion(0, 1000, 1.5, 0.5, 0.5, "abc");
        var json = Serialize(cmd);
        Assert.Contains("\"transactionId\": \"abc\"", json);
    }

    // ── AddZoomRegion fields preserved (#31) ──────────────────────────────────

    [Fact]
    public void AddZoomRegion_FieldsPreservedAfterRoundtrip()
    {
        var cmd = new AddZoomRegion(50, 3000, 1.5, 0.3, 0.7);
        var rt = Roundtrip(cmd);
        Assert.Equal(1.5, rt.Depth);
        Assert.Equal(0.3, rt.Cx);
        Assert.Equal(0.7, rt.Cy);
    }

    // ── AddTransition type string preserved (#32) ─────────────────────────────

    [Fact]
    public void AddTransition_TypeStringPreserved()
    {
        var cmd = new AddTransition("tr-x", 0, "wipe-right", 500);
        var json = Serialize(cmd);
        Assert.Contains("\"type\": \"wipe-right\"", json);
    }

    // ── BackgroundOptions with null optional fields (#33) ─────────────────────

    [Fact]
    public void BackgroundOptions_NullOptionalFieldsOmitted()
    {
        var cmd = new SetBackground(new BackgroundOptions(BackgroundKind.SolidColor, Color: "#fff"));
        var json = Serialize(cmd);
        // GradientCss, ImagePath, PresetId should not appear
        Assert.DoesNotContain("gradientCss", json);
        Assert.DoesNotContain("imagePath", json);
        Assert.DoesNotContain("presetId", json);
    }

    // ── AnnotationPayload with all kinds (#34) ────────────────────────────────

    [Fact]
    public void AnnotationPayload_TextKind_Serialized()
    {
        var payload = new AnnotationPayload(AnnotationKind.Text, 0.1, 0.2, 0.3, 0.1, Text: "Hi");
        var cmd = new AddAnnotation("a", 0, 1000, payload);
        var rt = Roundtrip(cmd);
        Assert.Equal(AnnotationKind.Text, rt.Payload.Kind);
        Assert.Equal("Hi", rt.Payload.Text);
    }

    [Fact]
    public void AnnotationPayload_ArrowKind_Serialized()
    {
        var payload = new AnnotationPayload(AnnotationKind.Arrow, 0.5, 0.5, 0.05, 0.05);
        var cmd = new AddAnnotation("a", 0, 1000, payload);
        var rt = Roundtrip(cmd);
        Assert.Equal(AnnotationKind.Arrow, rt.Payload.Kind);
    }

    [Fact]
    public void AnnotationPayload_ImageKind_Serialized()
    {
        var payload = new AnnotationPayload(AnnotationKind.Image, 0.0, 0.0, 0.2, 0.2, ImagePath: "/img.png");
        var cmd = new AddAnnotation("a", 0, 1000, payload);
        var rt = Roundtrip(cmd);
        Assert.Equal(AnnotationKind.Image, rt.Payload.Kind);
        Assert.Equal("/img.png", rt.Payload.ImagePath);
    }

    // ── CropOptions normalization (#35) ───────────────────────────────────────

    [Fact]
    public void CropOptions_ValuesUnchangedAfterRoundtrip()
    {
        var cmd = new AddCrop(new CropOptions(0.1, 0.2, 0.8, 0.6));
        var rt = Roundtrip(cmd);
        Assert.Equal(0.1, rt.Options.X);
        Assert.Equal(0.2, rt.Options.Y);
        Assert.Equal(0.8, rt.Options.Width);
        Assert.Equal(0.6, rt.Options.Height);
    }

    // ── Full ProjectModel roundtrip (#36) ─────────────────────────────────────

    [Fact]
    public void FullProjectModel_RoundtripWithMixedCommandLog()
    {
        var model = new ProjectModel
        {
            ProjectId = "p1",
            SourceType = SourceType.Screen,
            ScreenMetadata = new ScreenSourceMetadata("display:0", 1920, 1080),
            VideoPath = "/tmp/v.mp4",
            TelemetryPath = "/tmp/v.cursor.json",
            CreatedAt = DateTimeOffset.UtcNow,
        }
        .Append(new AddZoomRegion(0, 1000, 1.5, 0.5, 0.5))
        .Append(new AddTrimRegion(2000, 3000))
        .Append(new SetSpeed(0, 5000, 2.0))
        .Append(new AddSplitPoint(4000))
        .AppendTransaction([
            new AddZoomRegion(5000, 6000, 1.2, 0.3, 0.4),
            new AddTrimRegion(7000, 8000),
        ], "tx1");

        var json = JsonSerializer.Serialize(model, typeof(ProjectModel), Options);
        var restored = JsonSerializer.Deserialize<ProjectModel>(json, Options)!;

        Assert.Equal(model.ProjectId, restored.ProjectId);
        Assert.Equal(model.Commands.Count, restored.Commands.Count);
        Assert.Equal(model.UndoIndex, restored.UndoIndex);
        Assert.Equal(model.VideoPath, restored.VideoPath);
        Assert.Equal(model.TelemetryPath, restored.TelemetryPath);
    }

    // ── Camel case naming (#37) ───────────────────────────────────────────────

    [Fact]
    public void Serialize_UsesCamelCase()
    {
        var cmd = new AddZoomRegion(100, 2000, 1.5, 0.3, 0.7);
        var json = Serialize(cmd);
        Assert.Contains("\"startMs\"", json);
        Assert.Contains("\"endMs\"", json);
    }

    // ── Indented JSON output (#38) ─────────────────────────────────────────────

    [Fact]
    public void Serialize_ProducesIndentedOutput()
    {
        var cmd = new AddZoomRegion(100, 2000, 1.5, 0.3, 0.7);
        var json = Serialize(cmd);
        // Indented output has newlines
        Assert.Contains('\n', json);
    }

    // ── Helper: construct a minimal valid instance for each type ──────────────

    private static ProjectCommand MakeInstance(Type type) => type.Name switch
    {
        nameof(AddZoomRegion)    => new AddZoomRegion(0, 1000, 1.5, 0.5, 0.5),
        nameof(RemoveZoomRegion) => new RemoveZoomRegion(0),
        nameof(AddTrimRegion)    => new AddTrimRegion(0, 1000),
        nameof(RemoveTrimRegion) => new RemoveTrimRegion(0),
        nameof(SetSpeed)         => new SetSpeed(0, 1000, 1.0),
        nameof(SetBackground)    => new SetBackground(new BackgroundOptions(BackgroundKind.SolidColor)),
        nameof(SetVisualOptions) => new SetVisualOptions(new VisualOptions()),
        nameof(AddAnnotation)    => new AddAnnotation("a", 0, 1000, new AnnotationPayload(AnnotationKind.Text, 0, 0, 0.1, 0.1)),
        nameof(RemoveAnnotation) => new RemoveAnnotation("a"),
        nameof(UpdateAnnotation) => new UpdateAnnotation("a", new AnnotationPayload(AnnotationKind.Text, 0, 0, 0.1, 0.1)),
        nameof(AddCrop)          => new AddCrop(new CropOptions(0, 0, 1, 1)),
        nameof(RemoveCrop)       => new RemoveCrop(),
        nameof(AddKeyframe)      => new AddKeyframe("k", 0),
        nameof(RemoveKeyframe)   => new RemoveKeyframe("k"),
        nameof(AddSplitPoint)    => new AddSplitPoint(0),
        nameof(RemoveSplitPoint) => new RemoveSplitPoint(0),
        nameof(AddTransition)    => new AddTransition("t", 0, "fade", 500),
        nameof(RemoveTransition) => new RemoveTransition("t"),
        nameof(UpdateTransition) => new UpdateTransition("t", "fade", 300),
        _ => throw new ArgumentException($"Unknown type: {type.Name}")
    };
}
