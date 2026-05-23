namespace Rhodium.Platform.Patterns;

internal enum StrategyDispatchKind
{
    Tick,
    Quote,
    Trade,
    Book,
    BookDelta,
    BookDeltas,
    Bar
}
