using HPD.Base.Query;
using HPD.Base.Relational.Abstractions.Tests.Serialization;
using HPD.Base.Relational.Planning;

namespace HPD.Base.Relational.Abstractions.Tests.Contracts;

public sealed class RelationalQueryPlanContractTests
{
    [Fact]
    public void SupportedPlanCanRepresentSafeExecutableRead()
    {
        var plan = RelationalSamples.SafePlan();

        Assert.True(plan.ExecutableForRequestedContext);
        Assert.True(plan.SafeForRequestedContext);
        Assert.Equal(RelationalResidualKind.None, plan.Residual!.Kind);
        Assert.True(plan.Count!.ExactCandidateSet);
        Assert.True(plan.Page!.PageAppliedAfterAllRequiredFilters);
    }

    [Fact]
    public void UnsupportedPlanFailsClosed()
    {
        var plan = new RelationalQueryPlanDescriptor
        {
            Id = "unsupported",
            StoreId = "store",
            CollectionId = "orders",
            Status = RelationalQueryPlanStatus.Unsupported,
            UnsupportedParts = ["include", "extension"]
        };

        Assert.False(plan.ExecutableForRequestedContext);
        Assert.False(plan.SafeForRequestedContext);
        Assert.Contains("include", plan.UnsupportedParts!);
    }

    [Fact]
    public void PartialPlanIsNotExecutableUnlessSafetyIsProven()
    {
        var plan = new RelationalQueryPlanDescriptor
        {
            Id = "partial",
            StoreId = "store",
            CollectionId = "orders",
            Status = RelationalQueryPlanStatus.PartiallySupported,
            Residual = new RelationalResidualDescriptor
            {
                Kind = RelationalResidualKind.AfterNativeFilterBeforePage,
                Required = true,
                RunsBeforePage = true,
                RunsBeforeCount = true,
                SafeForRequestedContext = true
            }
        };

        Assert.False(plan.ExecutableForRequestedContext);
        Assert.False(plan.SafeForRequestedContext);
        Assert.True(plan.Residual!.SafeForRequestedContext);
    }

    [Fact]
    public void CountAndPageUnsafeResidualsAreExplicit()
    {
        var plan = new RelationalQueryPlanDescriptor
        {
            Id = "unsafe-count-page",
            StoreId = "store",
            CollectionId = "orders",
            Status = RelationalQueryPlanStatus.Unsafe,
            Residual = new RelationalResidualDescriptor
            {
                Kind = RelationalResidualKind.AfterCountUnsafe,
                Required = true,
                AffectedParts = ["filter", "count"],
                UnsafeReasons = ["count would happen before residual policy"]
            },
            Count = new RelationalCountPlanDescriptor
            {
                Requested = true,
                Mode = QueryCountMode.Exact,
                UnsafeReasons = ["not scoped to exact accessible set"]
            },
            Page = new RelationalPagePlanDescriptor
            {
                Requested = true,
                UnsafeReasons = ["window would be selected from broad candidate set"]
            }
        };

        Assert.False(plan.ExecutableForRequestedContext);
        Assert.Equal(RelationalResidualKind.AfterCountUnsafe, plan.Residual!.Kind);
        Assert.Contains("not scoped", plan.Count!.UnsafeReasons![0]);
        Assert.Contains("broad candidate", plan.Page!.UnsafeReasons![0]);
    }
}
