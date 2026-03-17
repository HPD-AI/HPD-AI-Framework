namespace HPD.Agent.Adapters;

/// <summary>
/// Marks a partial class as a platform card renderer.
/// The source generator emits a <c>Render(CardElement card)</c> dispatcher that
/// calls the partial methods declared on the class.
/// </summary>
/// <remarks>
/// Each <c>partial</c> method handles one <c>CardElement</c> variant.
/// The generator produces the <c>switch</c> entry point; each platform writes only
/// the platform-specific output format.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CardRendererAttribute : Attribute { }
