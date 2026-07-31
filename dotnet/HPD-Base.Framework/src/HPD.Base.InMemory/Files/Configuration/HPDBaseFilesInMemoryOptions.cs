using HPD.Base;

namespace HPD.Base.InMemory;

public sealed class HPDBaseFilesInMemoryOptions
{
    public FileProviderRef ProviderRef { get; set; } = new("inmemory");
    public long MaxBufferedObjectBytes { get; set; } = 104_857_600;
    public TimeProvider? TimeProvider { get; set; }
}
