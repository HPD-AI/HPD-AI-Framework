
namespace HPD.Base;

internal sealed class HPDBaseVolatileFileStoreOptions
{
    public FileProviderRef ProviderRef { get; set; } = new("volatile");
    public long MaxBufferedObjectBytes { get; set; } = 104_857_600;
    public TimeProvider? TimeProvider { get; set; }
}
