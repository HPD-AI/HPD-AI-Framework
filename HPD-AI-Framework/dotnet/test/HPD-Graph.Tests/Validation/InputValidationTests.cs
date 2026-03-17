using FluentAssertions;
using HPDAgent.Graph.Abstractions.Validation;
using HPDAgent.Graph.Core.Validation;
using Xunit;

namespace HPDAgent.Graph.Tests.Validation;

public class InputValidationTests
{
    [Fact]
    public void RangeValidator_ValueWithinRange_ReturnsSuccess()
    {
        var validator = InputValidators.Range(1, 100);
        var result = validator.Validate("count", 50);

        result.IsValid.Should().BeTrue();
        result.Errors.Should().BeEmpty();
    }

    [Fact]
    public void RangeValidator_ValueBelowMin_ReturnsError()
    {
        var validator = InputValidators.Range(1, 100);
        var result = validator.Validate("count", 0);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("between 1 and 100"));
    }

    [Fact]
    public void RangeValidator_ValueAboveMax_ReturnsError()
    {
        var validator = InputValidators.Range(1, 100);
        var result = validator.Validate("count", 150);

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("between 1 and 100"));
    }

    [Fact]
    public void RangeValidator_NonIntegerValue_ReturnsError()
    {
        var validator = InputValidators.Range(1, 100);
        var result = validator.Validate("count", "not a number");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("must be an integer"));
    }

    [Fact]
    public void UrlValidator_ValidHttpUrl_ReturnsSuccess()
    {
        var validator = InputValidators.Url();
        var result = validator.Validate("apiUrl", "https://api.example.com");

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void UrlValidator_ValidHttpsUrl_ReturnsSuccess()
    {
        var validator = InputValidators.Url();
        var result = validator.Validate("apiUrl", "http://api.example.com");

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void UrlValidator_InvalidUrl_ReturnsError()
    {
        var validator = InputValidators.Url();
        var result = validator.Validate("apiUrl", "not-a-url");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("valid URL"));
    }

    [Fact]
    public void UrlValidator_NonHttpScheme_ReturnsError()
    {
        var validator = InputValidators.Url();
        var result = validator.Validate("apiUrl", "ftp://example.com");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("HTTP or HTTPS"));
    }

    [Fact]
    public void EmailValidator_ValidEmail_ReturnsSuccess()
    {
        var validator = InputValidators.Email();
        var result = validator.Validate("email", "user@example.com");

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void EmailValidator_InvalidEmail_ReturnsError()
    {
        var validator = InputValidators.Email();
        var result = validator.Validate("email", "not-an-email");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("valid email"));
    }

    [Fact]
    public void RegexValidator_MatchingPattern_ReturnsSuccess()
    {
        var validator = InputValidators.Regex(@"^\d{3}-\d{3}-\d{4}$");
        var result = validator.Validate("phone", "555-123-4567");

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void RegexValidator_NonMatchingPattern_ReturnsError()
    {
        var validator = InputValidators.Regex(@"^\d{3}-\d{3}-\d{4}$");
        var result = validator.Validate("phone", "invalid-phone");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("match pattern"));
    }

    [Fact]
    public void EnumValidator_ValidEnumValue_ReturnsSuccess()
    {
        var validator = InputValidators.Enum<TestEnum>();
        var result = validator.Validate("status", "Active");

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void EnumValidator_InvalidEnumValue_ReturnsError()
    {
        var validator = InputValidators.Enum<TestEnum>();
        var result = validator.Validate("status", "InvalidStatus");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("Active, Inactive, Pending"));
    }

    [Fact]
    public void StringLengthValidator_ValidLength_ReturnsSuccess()
    {
        var validator = InputValidators.StringLength(1, 10);
        var result = validator.Validate("name", "John");

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void StringLengthValidator_TooShort_ReturnsError()
    {
        var validator = InputValidators.StringLength(5, 10);
        var result = validator.Validate("name", "Joe");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("between 5 and 10"));
    }

    [Fact]
    public void StringLengthValidator_TooLong_ReturnsError()
    {
        var validator = InputValidators.StringLength(1, 5);
        var result = validator.Validate("name", "Alexander");

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("between 1 and 5"));
    }

    [Fact]
    public void CollectionCountValidator_ValidCount_ReturnsSuccess()
    {
        var validator = InputValidators.CollectionCount(1, 5);
        var result = validator.Validate("items", new[] { 1, 2, 3 });

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void CollectionCountValidator_TooFew_ReturnsError()
    {
        var validator = InputValidators.CollectionCount(3, 5);
        var result = validator.Validate("items", new[] { 1, 2 });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("between 3 and 5"));
    }

    [Fact]
    public void CollectionCountValidator_TooMany_ReturnsError()
    {
        var validator = InputValidators.CollectionCount(1, 3);
        var result = validator.Validate("items", new[] { 1, 2, 3, 4, 5 });

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(e => e.Contains("between 1 and 3"));
    }

    private enum TestEnum
    {
        Active,
        Inactive,
        Pending
    }
}
