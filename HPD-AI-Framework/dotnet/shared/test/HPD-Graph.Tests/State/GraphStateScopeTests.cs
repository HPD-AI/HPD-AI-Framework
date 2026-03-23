using FluentAssertions;
using HPDAgent.Graph.Core.Channels;
using HPDAgent.Graph.Core.State;
using Xunit;

namespace HPD.Graph.Tests.State;

/// <summary>
/// Tests for GraphStateScope namespaced state management.
/// </summary>
public class GraphStateScopeTests
{
    #region Basic Operations - Root Scope

    [Fact]
    public void RootScope_SetAndGet_WorksCorrectly()
    {
        // Arrange
        var channelSet = new GraphChannelSet();
        var scope = new GraphStateScope(channelSet); // Root scope (null name)

        // Act
        scope.Set("name", "Alice");
        var result = scope.Get<string>("name");

        // Assert
        result.Should().Be("Alice");
        scope.ScopeName.Should().BeNull();
    }

    [Fact]
    public void RootScope_GetNonExistent_ReturnsDefault()
    {
        // Arrange
        var channelSet = new GraphChannelSet();
        var scope = new GraphStateScope(channelSet);

        // Act
        var result = scope.Get<string>("nonexistent");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void RootScope_TryGet_ExistingKey_ReturnsTrue()
    {
        // Arrange
        var channelSet = new GraphChannelSet();
        var scope = new GraphStateScope(channelSet);
        scope.Set("age", 30);

        // Act
        var success = scope.TryGet<int>("age", out var value);

        // Assert
        success.Should().BeTrue();
        value.Should().Be(30);
    }

    [Fact]
    public void RootScope_TryGet_NonExistentKey_ReturnsFalse()
    {
        // Arrange
        var channelSet = new GraphChannelSet();
        var scope = new GraphStateScope(channelSet);

        // Act
        var success = scope.TryGet<string>("missing", out var value);

        // Assert
        success.Should().BeFalse();
        value.Should().BeNull();
    }

    #endregion

    #region Basic Operations - Named Scope

    [Fact]
    public void NamedScope_SetAndGet_WorksCorrectly()
    {
        // Arrange
        var channelSet = new GraphChannelSet();
        var scope = new GraphStateScope(channelSet, "user");

        // Act
        scope.Set("name", "Bob");
        var result = scope.Get<string>("name");

        // Assert
        result.Should().Be("Bob");
        scope.ScopeName.Should().Be("user");
    }

    [Fact]
    public void NamedScope_GetNonExistent_ReturnsDefault()
    {
        // Arrange
        var channelSet = new GraphChannelSet();
        var scope = new GraphStateScope(channelSet, "config");

        // Act
        var result = scope.Get<int>("missing");

        // Assert
        result.Should().Be(0);
    }

    #endregion

    #region Remove Operations

    [Fact]
    public void Remove_ExistingKey_ReturnsTrueAndRemoves()
    {
        // Arrange
        var channelSet = new GraphChannelSet();
        var scope = new GraphStateScope(channelSet);
        scope.Set("temp", "value");

        // Act
        var removed = scope.Remove("temp");

        // Assert
        removed.Should().BeTrue();
        scope.Get<string>("temp").Should().BeNull();
    }

    [Fact]
    public void Remove_NonExistentKey_ReturnsFalse()
    {
        // Arrange
        var channelSet = new GraphChannelSet();
        var scope = new GraphStateScope(channelSet);

        // Act
        var removed = scope.Remove("nonexistent");

        // Assert
        removed.Should().BeFalse();
    }

    [Fact]
    public void Remove_WithNullKey_ReturnsFalse()
    {
        // Arrange
        var channelSet = new GraphChannelSet();
        var scope = new GraphStateScope(channelSet);

        // Act
        var removed = scope.Remove(null!);

        // Assert
        removed.Should().BeFalse();
    }

    #endregion

    #region Keys and AsDictionary

    [Fact]
    public void Keys_ReturnsAllKeysInScope()
    {
        // Arrange
        var channelSet = new GraphChannelSet();
        var scope = new GraphStateScope(channelSet, "data");
        scope.Set("key1", "value1");
        scope.Set("key2", 42);
        scope.Set("key3", true);

        // Act
        var keys = scope.Keys;

        // Assert
        keys.Should().HaveCount(3);
        keys.Should().Contain("key1");
        keys.Should().Contain("key2");
        keys.Should().Contain("key3");
    }

    [Fact]
    public void Keys_EmptyScope_ReturnsEmptyList()
    {
        // Arrange
        var channelSet = new GraphChannelSet();
        var scope = new GraphStateScope(channelSet, "empty");

        // Act
        var keys = scope.Keys;

        // Assert
        keys.Should().BeEmpty();
    }

    [Fact]
    public void AsDictionary_ReturnsSnapshotOfScopeData()
    {
        // Arrange
        var channelSet = new GraphChannelSet();
        var scope = new GraphStateScope(channelSet);
        scope.Set("name", "Alice");
        scope.Set("age", 30);
        scope.Set("active", true);

        // Act
        var dict = scope.AsDictionary();

        // Assert
        dict.Should().HaveCount(3);
        dict["name"].Should().Be("Alice");
        dict["age"].Should().Be(30);
        dict["active"].Should().Be(true);
    }

    [Fact]
    public void AsDictionary_EmptyScope_ReturnsEmptyDictionary()
    {
        // Arrange
        var channelSet = new GraphChannelSet();
        var scope = new GraphStateScope(channelSet, "empty");

        // Act
        var dict = scope.AsDictionary();

        // Assert
        dict.Should().BeEmpty();
    }

    #endregion

    #region Scope Isolation

    [Fact]
    public void RootScope_DoesNotSeeNamedScopeData()
    {
        // Arrange
        var channelSet = new GraphChannelSet();
        var rootScope = new GraphStateScope(channelSet);
        var namedScope = new GraphStateScope(channelSet, "user");

        // Act
        namedScope.Set("field", "value");

        // Assert
        rootScope.Get<string>("field").Should().BeNull();
    }

    [Fact]
    public void NamedScope_DoesNotSeeRootScopeData()
    {
        // Arrange
        var channelSet = new GraphChannelSet();
        var rootScope = new GraphStateScope(channelSet);
        var namedScope = new GraphStateScope(channelSet, "config");

        // Act
        rootScope.Set("global", "value");

        // Assert
        namedScope.Get<string>("global").Should().BeNull();
    }

    [Fact]
    public void DifferentNamedScopes_AreIsolated()
    {
        // Arrange
        var channelSet = new GraphChannelSet();
        var scope1 = new GraphStateScope(channelSet, "scope1");
        var scope2 = new GraphStateScope(channelSet, "scope2");

        // Act
        scope1.Set("data", "from-scope1");
        scope2.Set("data", "from-scope2");

        // Assert
        scope1.Get<string>("data").Should().Be("from-scope1");
        scope2.Get<string>("data").Should().Be("from-scope2");
    }

    [Fact]
    public void Clear_OnlyAffectsCurrentScope()
    {
        // Arrange
        var channelSet = new GraphChannelSet();
        var scope1 = new GraphStateScope(channelSet, "scope1");
        var scope2 = new GraphStateScope(channelSet, "scope2");

        scope1.Set("key1", "value1");
        scope1.Set("key2", "value2");
        scope2.Set("key1", "value1-scope2");

        // Act
        scope1.Clear();

        // Assert
        scope1.Keys.Should().BeEmpty();
        scope2.Keys.Should().HaveCount(1);
        scope2.Get<string>("key1").Should().Be("value1-scope2");
    }

    #endregion

    #region Channel Name Formatting

    [Fact]
    public void NamedScope_ChannelKeys_FormattedCorrectly()
    {
        // Arrange
        var channelSet = new GraphChannelSet();
        var scope = new GraphStateScope(channelSet, "myScope");

        // Act
        scope.Set("field", "value");

        // Assert
        var channelNames = channelSet.ChannelNames;
        channelNames.Should().Contain("scope:myScope:field");
    }

    [Fact]
    public void RootScope_ChannelKeys_NoPrefix()
    {
        // Arrange
        var channelSet = new GraphChannelSet();
        var scope = new GraphStateScope(channelSet);

        // Act
        scope.Set("field", "value");

        // Assert
        var channelNames = channelSet.ChannelNames;
        channelNames.Should().Contain("field");
        channelNames.Should().NotContain(name => name.StartsWith("scope:"));
    }

    #endregion

    #region Multiple Scopes with Same Key

    [Fact]
    public void MultipleScopes_SameKey_NoConflict()
    {
        // Arrange
        var channelSet = new GraphChannelSet();
        var scope1 = new GraphStateScope(channelSet, "user");
        var scope2 = new GraphStateScope(channelSet, "admin");
        var scope3 = new GraphStateScope(channelSet, "guest");

        // Act
        scope1.Set("role", "user-role");
        scope2.Set("role", "admin-role");
        scope3.Set("role", "guest-role");

        // Assert
        scope1.Get<string>("role").Should().Be("user-role");
        scope2.Get<string>("role").Should().Be("admin-role");
        scope3.Get<string>("role").Should().Be("guest-role");
    }

    [Fact]
    public void Clear_RemovesAllNamespacedChannels()
    {
        // Arrange
        var channelSet = new GraphChannelSet();
        var scope = new GraphStateScope(channelSet, "test");
        scope.Set("a", 1);
        scope.Set("b", 2);
        scope.Set("c", 3);

        var initialCount = channelSet.ChannelNames.Count(n => n.StartsWith("scope:test:"));

        // Act
        scope.Clear();

        // Assert
        initialCount.Should().Be(3);
        channelSet.ChannelNames.Should().NotContain(n => n.StartsWith("scope:test:"));
    }

    #endregion

    #region Validation

    [Fact]
    public void Set_WithNullKey_ThrowsArgumentException()
    {
        // Arrange
        var channelSet = new GraphChannelSet();
        var scope = new GraphStateScope(channelSet);

        // Act
        var act = () => scope.Set<string>(null!, "value");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Key cannot be null or whitespace*");
    }

    [Fact]
    public void Set_WithEmptyKey_ThrowsArgumentException()
    {
        // Arrange
        var channelSet = new GraphChannelSet();
        var scope = new GraphStateScope(channelSet);

        // Act
        var act = () => scope.Set("", "value");

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Key cannot be null or whitespace*");
    }

    [Fact]
    public void Get_WithNullKey_ThrowsArgumentException()
    {
        // Arrange
        var channelSet = new GraphChannelSet();
        var scope = new GraphStateScope(channelSet);

        // Act
        var act = () => scope.Get<string>(null!);

        // Assert
        act.Should().Throw<ArgumentException>()
            .WithMessage("*Key cannot be null or whitespace*");
    }

    [Fact]
    public void TryGet_WithNullKey_ReturnsFalse()
    {
        // Arrange
        var channelSet = new GraphChannelSet();
        var scope = new GraphStateScope(channelSet);

        // Act
        var success = scope.TryGet<string>(null!, out var value);

        // Assert
        success.Should().BeFalse();
        value.Should().BeNull();
    }

    #endregion
}
