using System.Collections.Immutable;
using Xunit;
using HPD.VCS.Core;

namespace HPD.VCS.Tests;

public class ImmutableArrayEqualityTest
{
    [Fact]
    public void ImmutableArray_Equality_ShouldWork()
    {
        var comp1 = new RepoPathComponent("test");
        var comp2 = new RepoPathComponent("test");
        
        Assert.True(comp1.Equals(comp2));
        Assert.True(comp1 == comp2);
        
        var arr1 = ImmutableArray.Create(comp1);
        var arr2 = ImmutableArray.Create(comp2);
        
        Assert.True(arr1.SequenceEqual(arr2));
        Assert.False(arr1.Equals(arr2)); // This is the issue!
    }
    
    [Fact]
    public void RepoPath_Manual_Equality_Test()
    {
        var path1 = new RepoPath(ImmutableArray.Create(new RepoPathComponent("test")));
        var path2 = new RepoPath(ImmutableArray.Create(new RepoPathComponent("test")));
        
        // This should be true but probably isn't due to ImmutableArray equality
        Assert.True(path1.Equals(path2));
    }
}
