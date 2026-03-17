using System.Text.Json.Serialization;

namespace HPDOS.Apps.AppRecorder.Project;

// ── Base ──────────────────────────────────────────────────────────────────────

/// <summary>
/// Immutable command in the project command log.
/// Each command carries an optional TransactionId — commands sharing the same
/// TransactionId are undone/redone as one atomic unit (e.g. all SmartEdit edits
/// in a single call undo with one Cmd+Z).
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "$type")]
[JsonDerivedType(typeof(AddZoomRegion),      "add_zoom_region")]
[JsonDerivedType(typeof(RemoveZoomRegion),   "remove_zoom_region")]
[JsonDerivedType(typeof(AddTrimRegion),      "add_trim_region")]
[JsonDerivedType(typeof(RemoveTrimRegion),   "remove_trim_region")]
[JsonDerivedType(typeof(SetSpeed),           "set_speed")]
[JsonDerivedType(typeof(SetBackground),      "set_background")]
[JsonDerivedType(typeof(SetVisualOptions),   "set_visual_options")]
[JsonDerivedType(typeof(AddAnnotation),      "add_annotation")]
[JsonDerivedType(typeof(RemoveAnnotation),   "remove_annotation")]
[JsonDerivedType(typeof(UpdateAnnotation),   "update_annotation")]
[JsonDerivedType(typeof(AddCrop),            "add_crop")]
[JsonDerivedType(typeof(RemoveCrop),         "remove_crop")]
[JsonDerivedType(typeof(AddKeyframe),        "add_keyframe")]
[JsonDerivedType(typeof(RemoveKeyframe),     "remove_keyframe")]
[JsonDerivedType(typeof(AddSplitPoint),      "add_split_point")]
[JsonDerivedType(typeof(RemoveSplitPoint),   "remove_split_point")]
[JsonDerivedType(typeof(AddTransition),      "add_transition")]
[JsonDerivedType(typeof(RemoveTransition),   "remove_transition")]
[JsonDerivedType(typeof(UpdateTransition),   "update_transition")]
public abstract record ProjectCommand(string? TransactionId = null);

// ── Zoom ──────────────────────────────────────────────────────────────────────

/// <param name="StartMs">Region start in milliseconds.</param>
/// <param name="EndMs">Region end in milliseconds.</param>
/// <param name="Depth">Zoom scale factor, e.g. 1.5 = 150%.</param>
/// <param name="Cx">Normalised horizontal focus point (0.0–1.0).</param>
/// <param name="Cy">Normalised vertical focus point (0.0–1.0).</param>
public sealed record AddZoomRegion(
    long StartMs, long EndMs,
    double Depth, double Cx, double Cy,
    string? TransactionId = null
) : ProjectCommand(TransactionId);

public sealed record RemoveZoomRegion(long StartMs, string? TransactionId = null)
    : ProjectCommand(TransactionId);

// ── Trim ──────────────────────────────────────────────────────────────────────

public sealed record AddTrimRegion(long StartMs, long EndMs, string? TransactionId = null)
    : ProjectCommand(TransactionId);

public sealed record RemoveTrimRegion(long StartMs, string? TransactionId = null)
    : ProjectCommand(TransactionId);

// ── Speed ─────────────────────────────────────────────────────────────────────

/// <param name="Multiplier">Speed multiplier: 0.5 = half speed, 2.0 = double speed.</param>
public sealed record SetSpeed(long StartMs, long EndMs, double Multiplier, string? TransactionId = null)
    : ProjectCommand(TransactionId);

// ── Background ────────────────────────────────────────────────────────────────

public enum BackgroundKind { SolidColor, Gradient, Image, Preset }

public sealed record BackgroundOptions(
    BackgroundKind Kind,
    string? Color = null,       // hex for SolidColor
    string? GradientCss = null, // CSS gradient string
    string? ImagePath = null,   // absolute path for Image
    string? PresetId = null     // preset identifier
);

public sealed record SetBackground(BackgroundOptions Options, string? TransactionId = null)
    : ProjectCommand(TransactionId);

// ── Visual options ────────────────────────────────────────────────────────────

public sealed record VisualOptions(
    double BorderRadius = 0,
    double Padding = 0,
    bool DropShadow = false,
    double ShadowOpacity = 0.5,
    bool BackgroundBlur = false,
    double BlurRadius = 0,
    bool MotionBlur = false
);

public sealed record SetVisualOptions(VisualOptions Options, string? TransactionId = null)
    : ProjectCommand(TransactionId);

// ── Annotations ───────────────────────────────────────────────────────────────

public enum AnnotationKind { Text, Arrow, Image }

public sealed record AnnotationPayload(
    AnnotationKind Kind,
    double X, double Y,             // normalised 0.0–1.0
    double Width, double Height,    // normalised
    string? Text = null,
    string? FontFamily = null,
    double FontSize = 16,
    string? Color = null,
    string? ImagePath = null
);

public sealed record AddAnnotation(
    string AnnotationId,
    long StartMs, long EndMs,
    AnnotationPayload Payload,
    string? TransactionId = null
) : ProjectCommand(TransactionId);

public sealed record RemoveAnnotation(string AnnotationId, string? TransactionId = null)
    : ProjectCommand(TransactionId);

public sealed record UpdateAnnotation(
    string AnnotationId,
    AnnotationPayload Payload,
    string? TransactionId = null
) : ProjectCommand(TransactionId);

// ── Crop ──────────────────────────────────────────────────────────────────────

public sealed record CropOptions(
    double X, double Y,         // normalised top-left
    double Width, double Height // normalised size
);

public sealed record AddCrop(CropOptions Options, string? TransactionId = null)
    : ProjectCommand(TransactionId);

public sealed record RemoveCrop(string? TransactionId = null)
    : ProjectCommand(TransactionId);

// ── Keyframes ─────────────────────────────────────────────────────────────────

public sealed record AddKeyframe(string KeyframeId, long TimeMs, string? TransactionId = null)
    : ProjectCommand(TransactionId);

public sealed record RemoveKeyframe(string KeyframeId, string? TransactionId = null)
    : ProjectCommand(TransactionId);

// ── Split points ──────────────────────────────────────────────────────────────

public sealed record AddSplitPoint(long TimeMs, string? TransactionId = null)
    : ProjectCommand(TransactionId);

public sealed record RemoveSplitPoint(long TimeMs, string? TransactionId = null)
    : ProjectCommand(TransactionId);

// ── Transitions ───────────────────────────────────────────────────────────────

/// <param name="TransitionId">Stable ID for this transition placement.</param>
/// <param name="TimeMs">Timestamp of the cut point this transition is placed at.</param>
/// <param name="Type">Transition name, e.g. "fade", "wipe-right", "circle-in". Matches bundled asset names.</param>
/// <param name="DurationMs">Length of the transition in milliseconds.</param>
public sealed record AddTransition(
    string TransitionId,
    long TimeMs,
    string Type,
    int DurationMs,
    string? TransactionId = null
) : ProjectCommand(TransactionId);

public sealed record RemoveTransition(string TransitionId, string? TransactionId = null)
    : ProjectCommand(TransactionId);

public sealed record UpdateTransition(
    string TransitionId,
    string Type,
    int DurationMs,
    string? TransactionId = null
) : ProjectCommand(TransactionId);
