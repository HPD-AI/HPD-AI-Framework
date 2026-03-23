namespace HPDAgent.Graph.Abstractions.Routing;

/// <summary>
/// Router for heterogeneous Map nodes.
/// Routes items to different processor graphs based on item properties.
/// Pattern: Identical to IGraphNodeHandler (instance class, DI registration).
///
/// WHEN TO USE:
/// Use IMapRouter when processing collections with DIFFERENT ITEM TYPES that need DIFFERENT PROCESSING.
///
/// Examples:
/// - Mixed documents (PDF → extract text, Image → resize, Video → transcode)
/// - Payment methods (CreditCard → Stripe, PayPal → PayPal API, Crypto → blockchain)
/// - File uploads (different processing per file type)
///
/// DO NOT USE when all items are the same type and need the same processing.
/// For homogeneous collections, use MapProcessorGraph instead (no router needed).
///
/// LIFECYCLE: Registered as Singleton (routers should be stateless)
/// </summary>
public interface IMapRouter
{
    /// <summary>
    /// Unique router name (used in MapRouterName property).
    /// Must be unique within the application.
    /// </summary>
    string RouterName { get; }

    /// <summary>
    /// Routes an item to a processor graph key.
    /// </summary>
    /// <param name="item">Item to route</param>
    /// <returns>Key for MapProcessorGraphs dictionary lookup</returns>
    string Route(object item);
}
