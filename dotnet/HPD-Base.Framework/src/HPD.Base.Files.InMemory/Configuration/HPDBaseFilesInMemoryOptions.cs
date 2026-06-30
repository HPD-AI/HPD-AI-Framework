using HPD.Base.Files.Objects;

namespace HPD.Base.Files.InMemory.Configuration;

public sealed class HPDBaseFilesInMemoryOptions
{
    public FileProviderRef ProviderRef { get; set; } = new("inmemory");
    public long MaxBufferedObjectBytes { get; set; } = 104_857_600;
    public TimeProvider? TimeProvider { get; set; }
}
