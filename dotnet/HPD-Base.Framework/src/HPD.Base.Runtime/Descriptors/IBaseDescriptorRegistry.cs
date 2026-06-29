namespace HPD.Base.Runtime.Descriptors;

public interface IBaseDescriptorRegistry
{
    BaseDescriptorSnapshot Current { get; }
    ValueTask<BaseDescriptorSnapshot> RebuildAsync(CancellationToken cancellationToken = default);
}
