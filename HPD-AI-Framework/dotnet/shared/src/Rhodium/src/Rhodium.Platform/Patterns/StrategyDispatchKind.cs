namespace Rhodium.Platform.Patterns;

internal enum StrategyDispatchKind
{
    Tick,
    Quote,
    Trade,
    Book,
    BookLevelDelta,
    BookLevelDeltas,
    Bar
}
