using System.Text.Json;
using System.Text.Json.Serialization;

namespace HPDOS.Apps.AppRecorder.Project;

[JsonSourceGenerationOptions(
    WriteIndented = true,
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
)]
[JsonSerializable(typeof(ProjectModel))]
[JsonSerializable(typeof(ProjectCommand))]
[JsonSerializable(typeof(AddZoomRegion))]
[JsonSerializable(typeof(RemoveZoomRegion))]
[JsonSerializable(typeof(AddTrimRegion))]
[JsonSerializable(typeof(RemoveTrimRegion))]
[JsonSerializable(typeof(SetSpeed))]
[JsonSerializable(typeof(SetBackground))]
[JsonSerializable(typeof(BackgroundOptions))]
[JsonSerializable(typeof(SetVisualOptions))]
[JsonSerializable(typeof(VisualOptions))]
[JsonSerializable(typeof(AddAnnotation))]
[JsonSerializable(typeof(RemoveAnnotation))]
[JsonSerializable(typeof(UpdateAnnotation))]
[JsonSerializable(typeof(AnnotationPayload))]
[JsonSerializable(typeof(AddCrop))]
[JsonSerializable(typeof(RemoveCrop))]
[JsonSerializable(typeof(CropOptions))]
[JsonSerializable(typeof(AddKeyframe))]
[JsonSerializable(typeof(RemoveKeyframe))]
[JsonSerializable(typeof(AddSplitPoint))]
[JsonSerializable(typeof(RemoveSplitPoint))]
[JsonSerializable(typeof(AddTransition))]
[JsonSerializable(typeof(RemoveTransition))]
[JsonSerializable(typeof(UpdateTransition))]
[JsonSerializable(typeof(ScreenSourceMetadata))]
[JsonSerializable(typeof(CameraSourceMetadata))]
[JsonSerializable(typeof(ImportSourceMetadata))]
public partial class ProjectJsonContext : JsonSerializerContext { }
