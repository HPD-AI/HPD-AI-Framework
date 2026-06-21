// Copyright 2026 Einstein Essibu
// SPDX-License-Identifier: Apache-2.0

using FluentAssertions;
using HPD.Agent.Evaluations.Annotation;
using HPD.Agent.Evaluations.Integration;

namespace HPD.Agent.Evaluations.Tests.Annotation;

public sealed class AnnotationQueueTests
{
    [Fact]
    public void SubmitResponse_PendingItem_CompletesAndStoresHumanFields()
    {
        var queue = new AnnotationQueue();
        var annotationId = queue.TryEnqueueFromScore(
            "session",
            "thread",
            turnIndex: 2,
            evaluatorName: "Task Success",
            score: 0.2);

        annotationId.Should().NotBeNull();

        var completed = queue.SubmitResponse(
            annotationId!,
            reviewerId: "reviewer-1",
            label: "pass",
            score: 1.0,
            comment: "Human verified success.");

        completed.Should().BeTrue();
        queue.GetCompleted().Should().ContainSingle()
            .Which.Should().Match<AnnotationItem>(item =>
                item.AnnotationId == annotationId &&
                item.Status == AnnotationStatus.Completed &&
                item.LockedBy == "reviewer-1" &&
                item.HumanLabel == "pass" &&
                item.HumanScore == 1.0 &&
                item.HumanComment == "Human verified success.");
    }

    [Fact]
    public void Claim_LockTimeout_ReleasesStaleLock()
    {
        var queue = new AnnotationQueue(new AnnotationQueueOptions
        {
            LockTimeout = TimeSpan.FromMilliseconds(1),
        });
        var annotationId = queue.TryEnqueueFromScore("session", "thread", 0, "Safety", 0.1);
        queue.Claim(annotationId!, "reviewer-1").Should().NotBeNull();

        System.Threading.Thread.Sleep(20);

        var claimed = queue.Claim(annotationId!, "reviewer-2");

        claimed.Should().NotBeNull();
        claimed!.LockedBy.Should().Be("reviewer-2");
    }

    [Fact]
    public void Complete_ClaimedItem_StoresScore()
    {
        var queue = new AnnotationQueue();
        var annotationId = queue.TryEnqueueFromScore("session", "thread", 0, "Safety", 0.1);
        queue.Claim(annotationId!, "reviewer-1").Should().NotBeNull();

        queue.Complete(annotationId!, "reviewer-1", "unsafe", score: 0.0)
            .Should().BeTrue();

        queue.GetCompleted().Should().ContainSingle()
            .Which.HumanScore.Should().Be(0.0);
    }

    [Fact]
    public void SubmitResponse_AlreadyCompleted_ReturnsFalse()
    {
        var queue = new AnnotationQueue();
        var annotationId = queue.TryEnqueueFromScore("session", "thread", 0, "Safety", 0.1);
        queue.SubmitResponse(annotationId!, "reviewer-1", "unsafe").Should().BeTrue();

        queue.SubmitResponse(annotationId!, "reviewer-2", "safe")
            .Should().BeFalse();
    }

    [Fact]
    public void SubmitResponse_MissingId_ReturnsFalse()
    {
        var queue = new AnnotationQueue();

        queue.SubmitResponse("missing", "reviewer-1", "unsafe")
            .Should().BeFalse();
    }

    [Fact]
    public void Complete_WrongReviewer_ReturnsFalse()
    {
        var queue = new AnnotationQueue();
        var annotationId = queue.TryEnqueueFromScore("session", "thread", 0, "Safety", 0.1);
        queue.Claim(annotationId!, "reviewer-1").Should().NotBeNull();

        queue.Complete(annotationId!, "reviewer-2", "unsafe")
            .Should().BeFalse();
    }

    [Fact]
    public void AnnotationResponseEvent_ImplementsResponseContract()
    {
        var response = new AnnotationResponseEvent
        {
            AnnotationId = "annotation-123",
            ReviewerId = "reviewer-1",
            Label = "pass",
        };

        response.RequestId.Should().Be("annotation-123");
        response.SourceName.Should().Be("HPD.Agent.Evaluations.Annotation");
    }
}
