using FluentAssertions;
using HPDAgent.Graph.Abstractions.Handlers;
using Xunit;

namespace HPD.Graph.Tests.Handlers;

/// <summary>
/// Tests for HandlerInputs type-safe input handling.
/// </summary>
public class HandlerInputsTests
{
    #region Get Tests

    [Fact]
    public void Get_ExistingValue_ReturnsCorrectValue()
    {
        // Arrange
        var inputs = new HandlerInputs();
        inputs.Add("name", "Alice");

        // Act
        var result = inputs.Get<string>("name");

        // Assert
        result.Should().Be("Alice");
    }

    [Fact]
    public void Get_MissingValue_ThrowsInvalidOperationException()
    {
        // Arrange
        var inputs = new HandlerInputs();

        // Act
        var act = () => inputs.Get<string>("missing");

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*Required input 'missing' not found*");
    }

    [Fact]
    public void Get_WrongType_ThrowsInvalidOperationException()
    {
        // Arrange
        var inputs = new HandlerInputs();
        inputs.Add("value", "string");

        // Act
        var act = () => inputs.Get<int>("value");

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*has type String, but Int32 was requested*");
    }

    [Fact]
    public void Get_NullValueWithNullableType_ReturnsNull()
    {
        // Arrange
        var inputs = new HandlerInputs();
        inputs.Add("nullable", null!);

        // Act
        var result = inputs.Get<string?>("nullable");

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public void Get_NullValueWithNonNullableType_ThrowsInvalidOperationException()
    {
        // Arrange
        var inputs = new HandlerInputs();
        inputs.Add("nullable", null!);

        // Act
        var act = () => inputs.Get<int>("nullable");

        // Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*is null, but type Int32 is not nullable*");
    }

    [Fact]
    public void Get_NullOrWhitespaceName_ThrowsArgumentException()
    {
        // Arrange
        var inputs = new HandlerInputs();

        // Act & Assert
        var actNull = () => inputs.Get<string>(null!);
        var actEmpty = () => inputs.Get<string>("");
        var actWhitespace = () => inputs.Get<string>("  ");

        actNull.Should().Throw<ArgumentException>();
        actEmpty.Should().Throw<ArgumentException>();
        actWhitespace.Should().Throw<ArgumentException>();
    }

    #endregion

    #region GetOrDefault Tests

    [Fact]
    public void GetOrDefault_ExistingValue_ReturnsValue()
    {
        // Arrange
        var inputs = new HandlerInputs();
        inputs.Add("count", 42);

        // Act
        var result = inputs.GetOrDefault("count", 0);

        // Assert
        result.Should().Be(42);
    }

    [Fact]
    public void GetOrDefault_MissingValue_ReturnsDefault()
    {
        // Arrange
        var inputs = new HandlerInputs();

        // Act
        var result = inputs.GetOrDefault("missing", 100);

        // Assert
        result.Should().Be(100);
    }

    [Fact]
    public void GetOrDefault_WrongType_ReturnsDefault()
    {
        // Arrange
        var inputs = new HandlerInputs();
        inputs.Add("value", "string");

        // Act
        var result = inputs.GetOrDefault<int>("value", 999);

        // Assert
        result.Should().Be(999);
    }

    [Fact]
    public void GetOrDefault_NullValue_ReturnsDefault()
    {
        // Arrange
        var inputs = new HandlerInputs();
        inputs.Add("nullable", null!);

        // Act
        var result = inputs.GetOrDefault("nullable", "default");

        // Assert
        result.Should().Be("default");
    }

    [Fact]
    public void GetOrDefault_NullOrWhitespaceName_ReturnsDefault()
    {
        // Arrange
        var inputs = new HandlerInputs();

        // Act
        var resultNull = inputs.GetOrDefault<string>(null!, "default");
        var resultEmpty = inputs.GetOrDefault<string>("", "default");
        var resultWhitespace = inputs.GetOrDefault<string>("  ", "default");

        // Assert
        resultNull.Should().Be("default");
        resultEmpty.Should().Be("default");
        resultWhitespace.Should().Be("default");
    }

    #endregion

    #region TryGet Tests

    [Fact]
    public void TryGet_ExistingValue_ReturnsTrueAndSetsValue()
    {
        // Arrange
        var inputs = new HandlerInputs();
        inputs.Add("status", "success");

        // Act
        var result = inputs.TryGet<string>("status", out var value);

        // Assert
        result.Should().BeTrue();
        value.Should().Be("success");
    }

    [Fact]
    public void TryGet_MissingValue_ReturnsFalse()
    {
        // Arrange
        var inputs = new HandlerInputs();

        // Act
        var result = inputs.TryGet<string>("missing", out var value);

        // Assert
        result.Should().BeFalse();
        value.Should().BeNull();
    }

    [Fact]
    public void TryGet_WrongType_ReturnsFalse()
    {
        // Arrange
        var inputs = new HandlerInputs();
        inputs.Add("value", "string");

        // Act
        var result = inputs.TryGet<int>("value", out var value);

        // Assert
        result.Should().BeFalse();
        value.Should().Be(0); // default(int)
    }

    [Fact]
    public void TryGet_NullValue_ReturnsTrueWithDefault()
    {
        // Arrange
        var inputs = new HandlerInputs();
        inputs.Add("nullable", null!);

        // Act
        var result = inputs.TryGet<string?>("nullable", out var value);

        // Assert
        result.Should().BeTrue();
        value.Should().BeNull();
    }

    [Fact]
    public void TryGet_NullOrWhitespaceName_ReturnsFalse()
    {
        // Arrange
        var inputs = new HandlerInputs();

        // Act
        var resultNull = inputs.TryGet<string>(null!, out var valueNull);
        var resultEmpty = inputs.TryGet<string>("", out var valueEmpty);
        var resultWhitespace = inputs.TryGet<string>("  ", out var valueWhitespace);

        // Assert
        resultNull.Should().BeFalse();
        resultEmpty.Should().BeFalse();
        resultWhitespace.Should().BeFalse();
    }

    #endregion

    #region Add Tests

    [Fact]
    public void Add_ValidInput_StoresValue()
    {
        // Arrange
        var inputs = new HandlerInputs();

        // Act
        inputs.Add("key", "value");

        // Assert
        inputs.Get<string>("key").Should().Be("value");
    }

    [Fact]
    public void Add_OverwritesExistingValue()
    {
        // Arrange
        var inputs = new HandlerInputs();
        inputs.Add("key", "first");

        // Act
        inputs.Add("key", "second");

        // Assert
        inputs.Get<string>("key").Should().Be("second");
    }

    [Fact]
    public void Add_NullOrWhitespaceName_ThrowsArgumentException()
    {
        // Arrange
        var inputs = new HandlerInputs();

        // Act & Assert
        var actNull = () => inputs.Add(null!, "value");
        var actEmpty = () => inputs.Add("", "value");
        var actWhitespace = () => inputs.Add("  ", "value");

        actNull.Should().Throw<ArgumentException>();
        actEmpty.Should().Throw<ArgumentException>();
        actWhitespace.Should().Throw<ArgumentException>();
    }

    #endregion

    #region GetAll Tests

    [Fact]
    public void GetAll_ReturnsAllInputs()
    {
        // Arrange
        var inputs = new HandlerInputs();
        inputs.Add("key1", "value1");
        inputs.Add("key2", 42);
        inputs.Add("key3", true);

        // Act
        var all = inputs.GetAll();

        // Assert
        all.Should().HaveCount(3);
        all["key1"].Should().Be("value1");
        all["key2"].Should().Be(42);
        all["key3"].Should().Be(true);
    }

    [Fact]
    public void GetAll_EmptyInputs_ReturnsEmptyDictionary()
    {
        // Arrange
        var inputs = new HandlerInputs();

        // Act
        var all = inputs.GetAll();

        // Assert
        all.Should().BeEmpty();
    }

    [Fact]
    public void GetAll_ReturnsReadOnlyDictionary()
    {
        // Arrange
        var inputs = new HandlerInputs();
        inputs.Add("key", "value");

        // Act
        var all = inputs.GetAll();

        // Assert
        all.Should().BeAssignableTo<IReadOnlyDictionary<string, object>>();
    }

    #endregion

    #region Contains Tests

    [Fact]
    public void Contains_ExistingKey_ReturnsTrue()
    {
        // Arrange
        var inputs = new HandlerInputs();
        inputs.Add("exists", "value");

        // Act
        var result = inputs.Contains("exists");

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void Contains_MissingKey_ReturnsFalse()
    {
        // Arrange
        var inputs = new HandlerInputs();

        // Act
        var result = inputs.Contains("missing");

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void Contains_NullOrWhitespaceName_ReturnsFalse()
    {
        // Arrange
        var inputs = new HandlerInputs();

        // Act
        var resultNull = inputs.Contains(null!);
        var resultEmpty = inputs.Contains("");
        var resultWhitespace = inputs.Contains("  ");

        // Assert
        resultNull.Should().BeFalse();
        resultEmpty.Should().BeFalse();
        resultWhitespace.Should().BeFalse();
    }

    #endregion

    #region GetAllMatching Tests (Phase 3)

    [Fact]
    public void GetAllMatching_WildcardSuffix_CollectsMatchingValues()
    {
        // Arrange
        var inputs = new HandlerInputs();
        inputs.Add("solver1.answer", "42");
        inputs.Add("solver2.answer", "42");
        inputs.Add("solver3.answer", "41");
        inputs.Add("classifier.question", "What is 6*7?");

        // Act
        var answers = inputs.GetAllMatching<string>("*.answer");

        // Assert
        answers.Should().HaveCount(3);
        answers.Should().Contain("42");
        answers.Should().Contain("41");
    }

    [Fact]
    public void GetAllMatching_WildcardPrefix_CollectsMatchingValues()
    {
        // Arrange
        var inputs = new HandlerInputs();
        inputs.Add("solver1.result", "A");
        inputs.Add("solver2.result", "B");
        inputs.Add("verifier.result", "C");

        // Act
        var solverResults = inputs.GetAllMatching<string>("solver*");

        // Assert
        solverResults.Should().HaveCount(2);
        solverResults.Should().Contain("A");
        solverResults.Should().Contain("B");
    }

    [Fact]
    public void GetAllMatching_NoMatches_ReturnsEmptyList()
    {
        // Arrange
        var inputs = new HandlerInputs();
        inputs.Add("key1", "value1");
        inputs.Add("key2", "value2");

        // Act
        var matches = inputs.GetAllMatching<string>("*.answer");

        // Assert
        matches.Should().BeEmpty();
    }

    [Fact]
    public void GetAllMatching_TypeMismatch_FiltersOutWrongTypes()
    {
        // Arrange
        var inputs = new HandlerInputs();
        inputs.Add("solver1.answer", "string answer");
        inputs.Add("solver2.answer", 42); // Integer, not string

        // Act
        var stringAnswers = inputs.GetAllMatching<string>("*.answer");

        // Assert
        stringAnswers.Should().HaveCount(1);
        stringAnswers.Should().Contain("string answer");
    }

    [Fact]
    public void GetAllMatching_SpecialCharactersInNodeId_WorksCorrectly()
    {
        // Arrange
        var inputs = new HandlerInputs();
        inputs.Add("node-with-dashes.answer", "A");
        inputs.Add("node_with_underscores.answer", "B");
        inputs.Add("node123.answer", "C");

        // Act
        var answers = inputs.GetAllMatching<string>("*.answer");

        // Assert
        answers.Should().HaveCount(3);
    }

    #endregion

    #region GetAllMatchingWithKeys Tests (Phase 3)

    [Fact]
    public void GetAllMatchingWithKeys_PreservesSourceAttribution()
    {
        // Arrange
        var inputs = new HandlerInputs();
        inputs.Add("solver1.answer", "42");
        inputs.Add("solver2.answer", "84");

        // Act
        var results = inputs.GetAllMatchingWithKeys<string>("solver*.answer");

        // Assert
        results.Should().HaveCount(2);
        results["solver1.answer"].Should().Be("42");
        results["solver2.answer"].Should().Be("84");
    }

    [Fact]
    public void GetAllMatchingWithKeys_NoMatches_ReturnsEmptyDictionary()
    {
        // Arrange
        var inputs = new HandlerInputs();
        inputs.Add("key1", "value1");

        // Act
        var results = inputs.GetAllMatchingWithKeys<string>("*.answer");

        // Assert
        results.Should().BeEmpty();
    }

    #endregion

    #region GetNamespace Tests (Phase 3)

    [Fact]
    public void GetNamespace_ReturnsAllOutputsFromSource()
    {
        // Arrange
        var inputs = new HandlerInputs();
        inputs.Add("solver1.answer", "42");
        inputs.Add("solver1.confidence", 0.95);
        inputs.Add("solver2.answer", "84");

        // Act
        var solver1Outputs = inputs.GetNamespace<object>("solver1");

        // Assert
        solver1Outputs.Should().HaveCount(2);
        solver1Outputs["answer"].Should().Be("42");
        solver1Outputs["confidence"].Should().Be(0.95);
    }

    [Fact]
    public void GetNamespace_NonExistentSource_ReturnsEmptyDictionary()
    {
        // Arrange
        var inputs = new HandlerInputs();
        inputs.Add("solver1.answer", "42");

        // Act
        var solver2Outputs = inputs.GetNamespace<object>("solver2");

        // Assert
        solver2Outputs.Should().BeEmpty();
    }

    [Fact]
    public void GetNamespace_StripsPrefix()
    {
        // Arrange
        var inputs = new HandlerInputs();
        inputs.Add("solver1.answer", "42");

        // Act
        var outputs = inputs.GetNamespace<string>("solver1");

        // Assert
        outputs.Should().ContainKey("answer"); // Not "solver1.answer"
        outputs.Should().NotContainKey("solver1.answer");
    }

    #endregion

    #region TryGetNamespace Tests (Phase 3)

    [Fact]
    public void TryGetNamespace_ExistingNamespace_ReturnsTrueAndSetsResult()
    {
        // Arrange
        var inputs = new HandlerInputs();
        inputs.Add("solver1.answer", "42");
        inputs.Add("solver1.confidence", 0.95);

        // Act
        var success = inputs.TryGetNamespace<object>("solver1", out var result);

        // Assert
        success.Should().BeTrue();
        result.Should().HaveCount(2);
        result["answer"].Should().Be("42");
    }

    [Fact]
    public void TryGetNamespace_NonExistentNamespace_ReturnsFalseAndEmptyResult()
    {
        // Arrange
        var inputs = new HandlerInputs();
        inputs.Add("solver1.answer", "42");

        // Act
        var success = inputs.TryGetNamespace<object>("solver2", out var result);

        // Assert
        success.Should().BeFalse();
        result.Should().BeEmpty();
    }

    #endregion

    #region GetAllFromSources Tests (Phase 3)

    [Fact]
    public void GetAllFromSources_CollectsFromExplicitSources()
    {
        // Arrange
        var inputs = new HandlerInputs();
        inputs.Add("solver1.answer", "A");
        inputs.Add("solver2.answer", "B");
        inputs.Add("solver3.answer", "C");

        // Act
        var answers = inputs.GetAllFromSources<string>("answer", "solver1", "solver3");

        // Assert
        answers.Should().HaveCount(2);
        answers.Should().Contain("A");
        answers.Should().Contain("C");
        answers.Should().NotContain("B");
    }

    [Fact]
    public void GetAllFromSources_MissingSourcesAreSkipped()
    {
        // Arrange
        var inputs = new HandlerInputs();
        inputs.Add("solver1.answer", "A");
        // solver2 doesn't have an answer

        // Act
        var answers = inputs.GetAllFromSources<string>("answer", "solver1", "solver2");

        // Assert
        answers.Should().HaveCount(1);
        answers.Should().Contain("A");
    }

    [Fact]
    public void GetAllFromSources_TypeMismatchFiltersOut()
    {
        // Arrange
        var inputs = new HandlerInputs();
        inputs.Add("solver1.answer", "string");
        inputs.Add("solver2.answer", 42); // Integer

        // Act
        var stringAnswers = inputs.GetAllFromSources<string>("answer", "solver1", "solver2");

        // Assert
        stringAnswers.Should().HaveCount(1);
        stringAnswers.Should().Contain("string");
    }

    #endregion

    #region GetSourceNodeIds Tests (Phase 3)

    [Fact]
    public void GetSourceNodeIds_ReturnsDistinctSourceIds()
    {
        // Arrange
        var inputs = new HandlerInputs();
        inputs.Add("solver1.answer", "A");
        inputs.Add("solver1.confidence", 0.9);
        inputs.Add("solver2.answer", "B");
        inputs.Add("classifier.question", "Q");

        // Act
        var sourceIds = inputs.GetSourceNodeIds();

        // Assert
        sourceIds.Should().HaveCount(3);
        sourceIds.Should().Contain("solver1");
        sourceIds.Should().Contain("solver2");
        sourceIds.Should().Contain("classifier");
    }

    [Fact]
    public void GetSourceNodeIds_NoNamespacedKeys_ReturnsEmpty()
    {
        // Arrange
        var inputs = new HandlerInputs();
        inputs.Add("simple_key", "value");
        inputs.Add("another_key", "value2");

        // Act
        var sourceIds = inputs.GetSourceNodeIds();

        // Assert
        sourceIds.Should().BeEmpty();
    }

    [Fact]
    public void GetSourceNodeIds_MultipleDotsInKey_UsesFirstSegment()
    {
        // Arrange
        var inputs = new HandlerInputs();
        inputs.Add("solver1.nested.value", "A");
        inputs.Add("solver2.answer", "B");

        // Act
        var sourceIds = inputs.GetSourceNodeIds();

        // Assert
        sourceIds.Should().HaveCount(2);
        sourceIds.Should().Contain("solver1");
        sourceIds.Should().Contain("solver2");
        sourceIds.Should().NotContain("solver1.nested");
    }

    #endregion
}
