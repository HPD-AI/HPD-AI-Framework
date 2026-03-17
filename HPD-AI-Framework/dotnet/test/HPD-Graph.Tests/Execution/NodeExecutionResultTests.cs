using FluentAssertions;
using HPDAgent.Graph.Abstractions.Execution;
using Xunit;

namespace HPD.Graph.Tests.Execution;

/// <summary>
/// Tests for NodeExecutionResult discriminated union.
/// </summary>
public class NodeExecutionResultTests
{
    #region Success Tests

    [Fact]
    public void Success_WithOutputs_StoresCorrectly()
    {
        // Arrange
        var outputs = new Dictionary<string, object> { ["result"] = "success" };
        var duration = TimeSpan.FromMilliseconds(100);

        // Act
        var result = NodeExecutionResult.Success.Single(outputs, duration, new NodeExecutionMetadata());

        // Assert
        var port0 = result.PortOutputs[0];
        port0.Should().ContainKey("result");
        port0["result"].Should().Be("success");
        result.Duration.Should().Be(duration);
        result.Metadata.Should().NotBeNull();
    }

    [Fact]
    public void Success_WithMetadata_StoresCorrectly()
    {
        // Arrange
        var outputs = new Dictionary<string, object>();
        var duration = TimeSpan.FromMilliseconds(50);
        var metadata = new NodeExecutionMetadata { AttemptNumber = 1 };

        // Act
        var result = NodeExecutionResult.Success.Single(outputs, duration, metadata);

        // Assert
        result.Metadata.Should().NotBeNull();
        result.Metadata.AttemptNumber.Should().Be(1);
    }

    [Fact]
    public void Success_PatternMatching_Works()
    {
        // Arrange
        NodeExecutionResult result = NodeExecutionResult.Success.Single(
            new Dictionary<string, object>(),
            TimeSpan.Zero,
            new NodeExecutionMetadata()
        );

        // Act & Assert
        var matched = result switch
        {
            NodeExecutionResult.Success s => "success",
            _ => "other"
        };

        matched.Should().Be("success");
    }

    #endregion

    #region Failure Tests

    [Fact]
    public void Failure_WithException_StoresCorrectly()
    {
        // Arrange
        var exception = new InvalidOperationException("Test error");
        var duration = TimeSpan.FromMilliseconds(25);

        // Act
        var result = new NodeExecutionResult.Failure(
            exception,
            ErrorSeverity.Fatal,
            IsTransient: false,
            duration
        );

        // Assert
        result.Exception.Should().Be(exception);
        result.Exception.Message.Should().Be("Test error");
        result.Severity.Should().Be(ErrorSeverity.Fatal);
        result.IsTransient.Should().BeFalse();
        result.Duration.Should().Be(duration);
        result.ErrorCode.Should().BeNull();
        result.PartialOutputs.Should().BeNull();
    }

    [Fact]
    public void Failure_WithErrorCode_StoresCorrectly()
    {
        // Arrange
        var exception = new Exception("Error");
        var duration = TimeSpan.FromMilliseconds(10);

        // Act
        var result = new NodeExecutionResult.Failure(
            exception,
            ErrorSeverity.Warning,
            IsTransient: false,
            duration,
            ErrorCode: "ERR_001"
        );

        // Assert
        result.ErrorCode.Should().Be("ERR_001");
    }

    [Fact]
    public void Failure_WithPartialOutputs_StoresCorrectly()
    {
        // Arrange
        var exception = new Exception("Partial failure");
        var duration = TimeSpan.FromMilliseconds(75);
        var partialOutputs = new Dictionary<string, object> { ["partial"] = "data" };

        // Act
        var result = new NodeExecutionResult.Failure(
            exception,
            ErrorSeverity.Warning,
            IsTransient: false,
            duration,
            PartialOutputs: partialOutputs
        );

        // Assert
        result.PartialOutputs.Should().NotBeNull();
        result.PartialOutputs.Should().ContainKey("partial");
        result.PartialOutputs!["partial"].Should().Be("data");
    }

    [Fact]
    public void Failure_TransientVsFatal_DifferentSeverities()
    {
        // Arrange & Act
        var transient = new NodeExecutionResult.Failure(
            new Exception("Timeout"),
            ErrorSeverity.Transient,
            IsTransient: true,
            TimeSpan.Zero
        );

        var fatal = new NodeExecutionResult.Failure(
            new Exception("Invalid input"),
            ErrorSeverity.Fatal,
            IsTransient: false,
            TimeSpan.Zero
        );

        // Assert
        transient.Severity.Should().Be(ErrorSeverity.Transient);
        transient.IsTransient.Should().BeTrue();

        fatal.Severity.Should().Be(ErrorSeverity.Fatal);
        fatal.IsTransient.Should().BeFalse();
    }

    [Fact]
    public void Failure_PatternMatching_Works()
    {
        // Arrange
        NodeExecutionResult result = new NodeExecutionResult.Failure(
            new Exception(),
            ErrorSeverity.Fatal,
            IsTransient: false,
            TimeSpan.Zero
        );

        // Act & Assert
        var matched = result switch
        {
            NodeExecutionResult.Failure f => "failure",
            _ => "other"
        };

        matched.Should().Be("failure");
    }

    #endregion

    #region Skipped Tests

    [Fact]
    public void Skipped_WithReason_StoresCorrectly()
    {
        // Arrange & Act
        var result = new NodeExecutionResult.Skipped(
            SkipReason.ConditionNotMet,
            Message: "Condition not satisfied"
        );

        // Assert
        result.Reason.Should().Be(SkipReason.ConditionNotMet);
        result.Message.Should().Be("Condition not satisfied");
        result.UpstreamFailedNode.Should().BeNull();
    }

    [Fact]
    public void Skipped_WithUpstreamFailure_StoresNodeId()
    {
        // Arrange & Act
        var result = new NodeExecutionResult.Skipped(
            SkipReason.DependencyFailed,
            Message: "Upstream node failed",
            UpstreamFailedNode: "node_123"
        );

        // Assert
        result.Reason.Should().Be(SkipReason.DependencyFailed);
        result.UpstreamFailedNode.Should().Be("node_123");
    }

    [Fact]
    public void Skipped_PatternMatching_Works()
    {
        // Arrange
        NodeExecutionResult result = new NodeExecutionResult.Skipped(
            SkipReason.ConditionNotMet
        );

        // Act & Assert
        var matched = result switch
        {
            NodeExecutionResult.Skipped s => "skipped",
            _ => "other"
        };

        matched.Should().Be("skipped");
    }

    #endregion

    #region Suspended Tests

    [Fact]
    public void Suspended_WithToken_StoresCorrectly()
    {
        // Arrange & Act
        var result = NodeExecutionResult.Suspended.ForHumanApproval(
            suspendToken: "suspend-token-123",
            message: "Waiting for approval"
        );

        // Assert
        result.SuspendToken.Should().Be("suspend-token-123");
        result.Message.Should().Be("Waiting for approval");
        result.ResumeValue.Should().BeNull();
    }

    [Fact]
    public void Suspended_WithResumeValue_StoresCorrectly()
    {
        // Arrange
        var resumeValue = new { approved = true, approver = "Alice" };

        // Act
        var result = NodeExecutionResult.Suspended.ForHumanApproval(
            suspendToken: "token-456",
            resumeValue: resumeValue
        );

        // Assert
        result.ResumeValue.Should().NotBeNull();
        result.ResumeValue.Should().Be(resumeValue);
    }

    [Fact]
    public void Suspended_PatternMatching_Works()
    {
        // Arrange
        NodeExecutionResult result = NodeExecutionResult.Suspended.ForHumanApproval("token");

        // Act & Assert
        var matched = result switch
        {
            NodeExecutionResult.Suspended s => "suspended",
            _ => "other"
        };

        matched.Should().Be("suspended");
    }

    #endregion

    #region Cancelled Tests

    [Fact]
    public void Cancelled_WithReason_StoresCorrectly()
    {
        // Arrange & Act
        var result = new NodeExecutionResult.Cancelled(
            CancellationReason.UserRequested,
            Message: "User cancelled operation"
        );

        // Assert
        result.Reason.Should().Be(CancellationReason.UserRequested);
        result.Message.Should().Be("User cancelled operation");
    }

    [Fact]
    public void Cancelled_Timeout_StoresCorrectly()
    {
        // Arrange & Act
        var result = new NodeExecutionResult.Cancelled(
            CancellationReason.Timeout,
            Message: "Operation timed out after 30s"
        );

        // Assert
        result.Reason.Should().Be(CancellationReason.Timeout);
    }

    [Fact]
    public void Cancelled_PatternMatching_Works()
    {
        // Arrange
        NodeExecutionResult result = new NodeExecutionResult.Cancelled(
            CancellationReason.ParentFailed
        );

        // Act & Assert
        var matched = result switch
        {
            NodeExecutionResult.Cancelled c => "cancelled",
            _ => "other"
        };

        matched.Should().Be("cancelled");
    }

    #endregion

    #region Discriminated Union Tests

    [Fact]
    public void NodeExecutionResult_ExhaustivePatternMatching_CoversAllCases()
    {
        // Arrange
        var results = new NodeExecutionResult[]
        {
            NodeExecutionResult.Success.Single(new Dictionary<string, object>(), TimeSpan.Zero, new NodeExecutionMetadata()),
            new NodeExecutionResult.Failure(new Exception(), ErrorSeverity.Fatal, false, TimeSpan.Zero),
            new NodeExecutionResult.Skipped(SkipReason.ConditionNotMet),
            NodeExecutionResult.Suspended.ForHumanApproval("token"),
            new NodeExecutionResult.Cancelled(CancellationReason.UserRequested)
        };

        // Act
        var matches = results.Select(r => r switch
        {
            NodeExecutionResult.Success => "Success",
            NodeExecutionResult.Failure => "Failure",
            NodeExecutionResult.Skipped => "Skipped",
            NodeExecutionResult.Suspended => "Suspended",
            NodeExecutionResult.Cancelled => "Cancelled",
            _ => throw new InvalidOperationException("Unhandled result type")
        }).ToList();

        // Assert
        matches.Should().HaveCount(5);
        matches.Should().Contain("Success");
        matches.Should().Contain("Failure");
        matches.Should().Contain("Skipped");
        matches.Should().Contain("Suspended");
        matches.Should().Contain("Cancelled");
    }

    [Fact]
    public void NodeExecutionResult_TypeChecking_Works()
    {
        // Arrange
        NodeExecutionResult success = NodeExecutionResult.Success.Single(
            new Dictionary<string, object>(),
            TimeSpan.Zero,
            new NodeExecutionMetadata()
        );

        NodeExecutionResult failure = new NodeExecutionResult.Failure(
            new Exception(),
            ErrorSeverity.Fatal,
            false,
            TimeSpan.Zero
        );

        // Act & Assert
        (success is NodeExecutionResult.Success).Should().BeTrue();
        (success is NodeExecutionResult.Failure).Should().BeFalse();

        (failure is NodeExecutionResult.Failure).Should().BeTrue();
        (failure is NodeExecutionResult.Success).Should().BeFalse();
    }

    [Fact]
    public void NodeExecutionResult_RecordEquality_Works()
    {
        // Arrange
        var result1 = new NodeExecutionResult.Skipped(
            SkipReason.ConditionNotMet,
            Message: "test"
        );

        var result2 = new NodeExecutionResult.Skipped(
            SkipReason.ConditionNotMet,
            Message: "test"
        );

        var result3 = new NodeExecutionResult.Skipped(
            SkipReason.DependencyFailed,
            Message: "test"
        );

        // Act & Assert
        result1.Should().Be(result2); // Same values
        result1.Should().NotBe(result3); // Different reason
    }

    #endregion
}
