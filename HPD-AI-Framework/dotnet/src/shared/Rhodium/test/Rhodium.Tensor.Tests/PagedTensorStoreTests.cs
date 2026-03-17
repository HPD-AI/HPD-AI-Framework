using Rhodium.Tensor;

namespace Rhodium.Tensor.Tests;

public class PagedTensorStoreTests
{
    [Fact]
    public void PagedTensorStore_PageSizeIs1024()
    {
        using var store = new PagedTensorStore();
        Assert.Equal(1024, store.PageSize);
    }

    [Fact]
    public void PagedTensorStore_GrowIncreasesVirtualCount()
    {
        using var store = new PagedTensorStore();

        var idx1 = store.Grow();
        var idx2 = store.Grow();
        var idx3 = store.Grow();

        Assert.Equal(0, idx1);
        Assert.Equal(1, idx2);
        Assert.Equal(2, idx3);
    }

    [Fact]
    public void PagedTensorStore_GetScalarReadWrite()
    {
        using var store = new PagedTensorStore();
        var field = new VectorField<PriceF64>("TestPrice");

        store.Grow();
        store.Grow();

        ref var value0 = ref store.GetScalar(field, 0);
        ref var value1 = ref store.GetScalar(field, 1);

        value0 = new PriceF64(100.0);
        value1 = new PriceF64(200.0);

        Assert.Equal(100.0, store.GetScalar(field, 0).Value);
        Assert.Equal(200.0, store.GetScalar(field, 1).Value);
    }

    [Fact]
    public void PagedTensorStore_GetPageReturnsCorrectSize()
    {
        using var store = new PagedTensorStore();
        var field = new VectorField<PriceF64>("TestPrice");

        // Grow to create first page
        for (int i = 0; i < 10; i++)
            store.Grow();

        var page = store.GetPage(field, 0);
        Assert.Equal(1024, page.Length);
    }

    [Fact]
    public void PagedTensorStore_BroadcastFillsRange()
    {
        using var store = new PagedTensorStore();
        var field = new VectorField<FactorF64>("Factor");

        // Grow to create multiple indices
        for (int i = 0; i < 100; i++)
            store.Grow();

        // Broadcast 1.5 to indices 10-19
        store.Broadcast(field, new FactorF64(1.5), 10, 10);

        // Verify the range
        for (int i = 0; i < 100; i++)
        {
            var value = store.GetScalar(field, i).Value;
            if (i >= 10 && i < 20)
                Assert.Equal(1.5, value);
            else
                Assert.Equal(0.0, value); // Default value
        }
    }

    [Fact]
    public void PagedTensorStore_BroadcastAcrossPageBoundary()
    {
        using var store = new PagedTensorStore();
        var field = new VectorField<PriceF64>("Price");

        // Grow to span multiple pages
        for (int i = 0; i < 2000; i++)
            store.Grow();

        // Broadcast across page boundary (page size is 1024)
        store.Broadcast(field, new PriceF64(99.99), 1000, 100);

        // Verify values on both sides of boundary
        Assert.Equal(99.99, store.GetScalar(field, 1000).Value);
        Assert.Equal(99.99, store.GetScalar(field, 1023).Value); // Last of page 0
        Assert.Equal(99.99, store.GetScalar(field, 1024).Value); // First of page 1
        Assert.Equal(99.99, store.GetScalar(field, 1099).Value);
        Assert.Equal(0.0, store.GetScalar(field, 1100).Value);   // Outside range
    }

    [Fact]
    public void PagedTensorStore_MultipleFieldsIndependent()
    {
        using var store = new PagedTensorStore();
        var priceField = new VectorField<PriceF64>("Price");
        var sizeField = new VectorField<SizeF64>("Size");

        store.Grow();

        store.GetScalar(priceField, 0) = new PriceF64(100.0);
        store.GetScalar(sizeField, 0) = new SizeF64(500.0);

        Assert.Equal(100.0, store.GetScalar(priceField, 0).Value);
        Assert.Equal(500.0, store.GetScalar(sizeField, 0).Value);
    }

    [Fact]
    public void PagedTensorStore_ForEachPageExecutesKernel()
    {
        using var store = new PagedTensorStore();
        var field = new VectorField<PriceF64>("Price");

        // Grow to span 3 pages
        for (int i = 0; i < 2500; i++)
            store.Grow();

        // Set all prices to 100.0
        store.Broadcast(field, new PriceF64(100.0), 0, 2500);

        // Kernel that doubles all values
        var kernel = new DoublingKernel(field);
        store.ForEachPage(kernel);

        // Verify values were doubled
        Assert.Equal(200.0, store.GetScalar(field, 0).Value);
        Assert.Equal(200.0, store.GetScalar(field, 1500).Value);
        Assert.Equal(200.0, store.GetScalar(field, 2499).Value);
    }

    [Fact]
    public void PagedTensorStore_GrowCreatesNewPageAt1024Boundary()
    {
        using var store = new PagedTensorStore();
        var field = new VectorField<PriceF64>("Price");

        // Grow to exactly 1024 (one full page)
        for (int i = 0; i < 1024; i++)
            store.Grow();

        // Set value in first page
        store.GetScalar(field, 1023) = new PriceF64(111.0);
        Assert.Equal(111.0, store.GetScalar(field, 1023).Value);

        // Grow one more - should create second page
        store.Grow();
        store.GetScalar(field, 1024) = new PriceF64(222.0);

        Assert.Equal(222.0, store.GetScalar(field, 1024).Value);
        Assert.Equal(111.0, store.GetScalar(field, 1023).Value);
    }

    [Fact]
    public void PagedTensorStore_LazyFieldAllocation()
    {
        using var store = new PagedTensorStore();
        var field1 = new VectorField<PriceF64>("Field1");
        var field2 = new VectorField<PriceF64>("Field2");

        // Grow universe
        for (int i = 0; i < 100; i++)
            store.Grow();

        // Access field1 - should allocate
        store.GetScalar(field1, 50) = new PriceF64(100.0);
        Assert.Equal(100.0, store.GetScalar(field1, 50).Value);

        // Access field2 - should also allocate and catch up to current size
        store.GetScalar(field2, 75) = new PriceF64(200.0);
        Assert.Equal(200.0, store.GetScalar(field2, 75).Value);
    }

    [Fact]
    public void PagedTensorStore_DisposeCleansUpResources()
    {
        var store = new PagedTensorStore();
        var field = new VectorField<PriceF64>("Price");

        for (int i = 0; i < 1000; i++)
            store.Grow();

        store.Broadcast(field, new PriceF64(100.0), 0, 1000);

        // Should not throw
        store.Dispose();
    }

    // Helper kernel for testing
    private readonly struct DoublingKernel : IComputeKernel
    {
        private readonly VectorField<PriceF64> _field;

        public DoublingKernel(VectorField<PriceF64> field)
        {
            _field = field;
        }

        public void Execute(ITensorStore store, int pageIndex)
        {
            var page = store.GetPage(_field, pageIndex);
            for (int i = 0; i < page.Length; i++)
            {
                page[i] = new PriceF64(page[i].Value * 2.0);
            }
        }
    }
}
