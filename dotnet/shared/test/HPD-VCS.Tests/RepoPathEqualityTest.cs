using System.Collections.Generic;
using System.IO;
using System.Linq;
using Xunit;
using HPD.VCS.Core;

namespace HPD.VCS.Tests;

public class RepoPathEqualityTest
{
    [Fact]
    public void RepoPath_DictionaryLookup_ShouldWork()
    {
        // Create paths using different methods
        var path1 = RepoPath.FromInternalString("README.md");
        var path2 = RepoPath.FromInternalString("README.md");
        var path3 = new RepoPath("README.md");
        
        // Test basic equality first
        Assert.True(path1.Equals(path2));
        Assert.True(path1 == path2);
        
        // Debug: test each piece separately
        var areEqual = path1.Equals(path3);
        var hashCodesEqual = path1.GetHashCode() == path3.GetHashCode();
        var componentsEqual = path1.Components.Length == path3.Components.Length;
        var componentsSequenceEqual = path1.Components.SequenceEqual(path3.Components);
        var immutableArraysEqual = path1.Components.Equals(path3.Components);
        
        if (!areEqual)
        {
            // Write debug info to a temp file to capture details
            var debugPath = Path.GetTempFileName();
            var debugInfo = $@"
path1: {path1} (Components: {path1.Components.Length})
path2: {path2} (Components: {path2.Components.Length})
path3: {path3} (Components: {path3.Components.Length})
path1[0]: '{path1.Components[0].Value}'
path3[0]: '{path3.Components[0].Value}'
path1.Components type: {path1.Components.GetType()}
path3.Components type: {path3.Components.GetType()}
path1.Components.IsDefault: {path1.Components.IsDefault}
path3.Components.IsDefault: {path3.Components.IsDefault}
path1.Equals(path3): {areEqual}
path1 == path3: {path1 == path3}
path1.GetHashCode(): {path1.GetHashCode()}
path3.GetHashCode(): {path3.GetHashCode()}
hashCodesEqual: {hashCodesEqual}
componentsEqual: {componentsEqual}
componentsSequenceEqual: {componentsSequenceEqual}
immutableArraysEqual: {immutableArraysEqual}
Component equality: {path1.Components[0].Equals(path3.Components[0])}
Component ==: {path1.Components[0] == path3.Components[0]}
Component value equality: '{path1.Components[0].Value}' == '{path3.Components[0].Value}': {path1.Components[0].Value == path3.Components[0].Value}
";
            File.WriteAllText(debugPath, debugInfo);
            throw new Exception($"Debug info written to: {debugPath}");
        }
        
        Assert.True(path1.Equals(path3));
        Assert.True(path1 == path3);
        
        // Test hash codes
        Assert.Equal(path1.GetHashCode(), path2.GetHashCode());
        Assert.Equal(path1.GetHashCode(), path3.GetHashCode());
        
        // Test dictionary behavior
        var dict = new Dictionary<RepoPath, string>();
        dict[path1] = "value1";
        
        Assert.True(dict.ContainsKey(path2));
        Assert.True(dict.ContainsKey(path3));
        Assert.Equal("value1", dict[path2]);
        Assert.Equal("value1", dict[path3]);
    }
    
    [Fact]
    public void RepoPath_ComplexPath_DictionaryLookup_ShouldWork()
    {
        // Create paths using different methods
        var path1 = RepoPath.FromInternalString("src/main.cs");
        var path2 = RepoPath.FromInternalString("src/main.cs");
        var path3 = new RepoPath("src", "main.cs");
        
        // Test equality
        Assert.True(path1.Equals(path2));
        Assert.True(path1 == path2);
        Assert.True(path1.Equals(path3));
        Assert.True(path1 == path3);
        
        // Test hash codes
        Assert.Equal(path1.GetHashCode(), path2.GetHashCode());
        Assert.Equal(path1.GetHashCode(), path3.GetHashCode());
        
        // Test dictionary behavior
        var dict = new Dictionary<RepoPath, string>();
        dict[path1] = "value1";
        
        Assert.True(dict.ContainsKey(path2));
        Assert.True(dict.ContainsKey(path3));
        Assert.Equal("value1", dict[path2]);
        Assert.Equal("value1", dict[path3]);
    }
}
