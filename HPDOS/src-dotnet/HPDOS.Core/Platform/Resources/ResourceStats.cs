namespace HPDOS.Core.Platform.Resources;

public sealed record ResourceStats
{
    public int TotalFiles     { get; init; }
    public int TotalResources { get; init; }
}

public sealed record ResourceLimits
{
    public int MaxFilesPerApp  { get; init; }
    public int MaxTotalFiles   { get; init; }

    public static ResourceLimits Unlimited    => new() { MaxFilesPerApp = 0,   MaxTotalFiles = 0 };
    public static ResourceLimits Conservative => new() { MaxFilesPerApp = 50,  MaxTotalFiles = 500 };
    public static ResourceLimits Production   => new() { MaxFilesPerApp = 100, MaxTotalFiles = 1000 };
}
