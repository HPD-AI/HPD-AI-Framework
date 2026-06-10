namespace Rhodium.Tensor;

/// <summary>
/// Paged columnar tensor storage interface.
/// Provides type-safe access to unmanaged tensor fields.
/// </summary>
public interface ITensorStore
{
    /// <summary>
    /// Fixed number of elements per page (e.g., 1024).
    /// </summary>
    int PageSize { get; }

    /// <summary>
    /// Expands the virtual universe by one index.
    /// Returns the new virtual index.
    /// </summary>
    int Grow();

    /// <summary>
    /// Type-safe page access.
    /// </summary>
    /// <typeparam name="T">The unmanaged type of the field.</typeparam>
    /// <param name="field">The field to access.</param>
    /// <param name="pageIndex">The page index.</param>
    /// <returns>A span over the page.</returns>
    Span<T> GetPage<T>(VectorField<T> field, int pageIndex) where T : unmanaged;

    /// <summary>
    /// Scalar random access (O(1)).
    /// </summary>
    /// <typeparam name="T">The unmanaged type of the field.</typeparam>
    /// <param name="field">The field to access.</param>
    /// <param name="virtualIndex">The virtual index.</param>
    /// <returns>A reference to the scalar value.</returns>
    ref T GetScalar<T>(VectorField<T> field, int virtualIndex) where T : unmanaged;

    /// <summary>
    /// Broadcast a value to a range (used for factor initialization).
    /// </summary>
    /// <typeparam name="T">The unmanaged type of the field.</typeparam>
    /// <param name="field">The field to write to.</param>
    /// <param name="value">The value to broadcast.</param>
    /// <param name="start">The starting virtual index.</param>
    /// <param name="length">The number of elements to write.</param>
    void Broadcast<T>(VectorField<T> field, T value, int start, int length) where T : unmanaged;

    /// <summary>
    /// Access read-only parameters (hyper-batching support).
    /// </summary>
    /// <typeparam name="T">The parameter type.</typeparam>
    /// <param name="name">The parameter name.</param>
    /// <returns>A read-only span over the parameter values.</returns>
    ReadOnlySpan<T> GetParameter<T>(string name);

    /// <summary>
    /// Execute a kernel across all active pages.
    /// Prefer struct kernels for performance.
    /// </summary>
    /// <typeparam name="TKernel">The kernel type.</typeparam>
    /// <param name="kernel">The kernel instance.</param>
    void ForEachPage<TKernel>(TKernel kernel) where TKernel : IComputeKernel;
}

/// <summary>
/// Compute kernel interface for page-wise tensor operations.
/// </summary>
public interface IComputeKernel
{
    /// <summary>
    /// Execute the kernel on a single page.
    /// </summary>
    /// <param name="store">The tensor store.</param>
    /// <param name="pageIndex">The page index to process.</param>
    void Execute(ITensorStore store, int pageIndex);
}
