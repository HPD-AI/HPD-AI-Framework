namespace HPD.Agent.Bots;

/// <summary>
/// Marks a partial class as a platform card renderer.
/// This internal pre-1.0 marker is retained for planned source generation. Current
/// platform renderers are hand-written.
/// </summary>
/// <remarks>
/// Each <c>partial</c> method handles one <c>CardElement</c> variant.
/// Do not document this marker as a supported extension point until generator
/// dispatch is implemented and tested.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
internal sealed class CardRendererAttribute : Attribute { }
