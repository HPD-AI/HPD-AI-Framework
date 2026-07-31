namespace HPD.Base;

public interface IBaseDescriptorRegistry
{
    BaseDescriptorSnapshot Current { get; }
    ValueTask<BaseDescriptorSnapshot> RebuildAsync(CancellationToken cancellationToken = default);
}
