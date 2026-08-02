namespace HPD.Base;

/// <summary>Defines the ibase descriptor registry contract.</summary>
public interface IBaseDescriptorRegistry
{
    /// <summary>Gets the current.</summary>
    BaseDescriptorSnapshot Current { get; }
    /// <summary>Executes the rebuild async operation.</summary>
    ValueTask<BaseDescriptorSnapshot> RebuildAsync(CancellationToken cancellationToken = default);
}
