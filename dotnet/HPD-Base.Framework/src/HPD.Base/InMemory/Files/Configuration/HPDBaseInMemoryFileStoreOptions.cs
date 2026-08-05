
namespace HPD.Base;

internal sealed class HPDBaseInMemoryFileStoreOptions
{
    /// <summary>Gets or sets the provider ref.</summary>
    public FileProviderRef ProviderRef { get; set; } = new("inmemory");
    /// <summary>Gets or sets the max buffered object bytes.</summary>
    public long MaxBufferedObjectBytes { get; set; } = 104_857_600;
    /// <summary>Gets or sets the time provider.</summary>
    public TimeProvider? TimeProvider { get; set; }
}
