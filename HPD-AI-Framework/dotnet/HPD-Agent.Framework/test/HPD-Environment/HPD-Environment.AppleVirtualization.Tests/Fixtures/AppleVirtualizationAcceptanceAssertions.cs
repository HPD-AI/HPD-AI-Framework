namespace HPD.Environment.AppleVirtualization.Tests.Fixtures;

using FluentAssertions;
using HPD.Environment.AppleVirtualization.Protocol;
using HPD.Environment.Contracts;

public static class AppleVirtualizationAcceptanceAssertions
{
    public static void ShouldRepresentHostPhase(
        this AppleVirtualizationHelperEnvelope envelope,
        RuntimeHostPhase expectedPhase,
        bool expectedGuestControlReachable)
    {
        envelope.HostStatusResponse.Should().NotBeNull();
        envelope.HostStatusResponse!.HostPhase.Should().Be(expectedPhase);
        envelope.HostStatusResponse.GuestControlReachable.Should().Be(expectedGuestControlReachable);
    }

    public static void ShouldRepresentProjection(
        this AppleVirtualizationHelperEnvelope envelope,
        ContentProjectionPhase phase,
        ProjectionRealizationKind realization)
    {
        envelope.ProjectionStatusResponse.Should().NotBeNull();
        envelope.ProjectionStatusResponse!.ProjectionPhase.Should().Be(phase);
        envelope.ProjectionStatusResponse.EffectiveRealization.Should().Be(realization);
    }

    public static void ShouldRepresentUnitPhase(this AppleVirtualizationHelperEnvelope envelope, ExecutionUnitPhase phase)
    {
        envelope.UnitStatusResponse.Should().NotBeNull();
        envelope.UnitStatusResponse!.UnitPhase.Should().Be(phase);
    }

    public static void ShouldRepresentProcessExit(
        this AppleVirtualizationHelperEnvelope envelope,
        ProcessCompletionKind completionKind,
        int? exitCode)
    {
        envelope.ProcessStatusResponse.Should().NotBeNull();
        envelope.ProcessStatusResponse!.Result.Should().NotBeNull();
        envelope.ProcessStatusResponse.Result!.CompletionKind.Should().Be(completionKind);
        envelope.ProcessStatusResponse.Result.ExitCode.Should().Be(exitCode);
    }

    public static void ShouldRepresentOutput(
        this AppleVirtualizationHelperEnvelope envelope,
        ProcessOutputStream stream,
        ReadOnlyMemory<byte> expectedBytes,
        ProcessOutputChunkFlags expectedFlags)
    {
        envelope.ProcessOutputEvent.Should().NotBeNull();
        envelope.ProcessOutputEvent!.Stream.Should().Be(stream);
        envelope.ProcessOutputEvent.Flags.Should().HaveFlag(expectedFlags);
        envelope.ProcessOutputEvent.Bytes.Span.SequenceEqual(expectedBytes.Span).Should().BeTrue();
    }

    public static void ShouldHaveStableDiagnostic(
        this AppleVirtualizationHelperEnvelope envelope,
        string code,
        bool retryable)
    {
        envelope.Error.Should().NotBeNull();
        envelope.Error!.Code.Should().Be(code);
        envelope.Error.Retryable.Should().Be(retryable);
    }

    public static void ShouldUseAppleProviderHandle<TTarget>(
        this TargetHandle<TTarget> handle,
        TargetRouteSegmentKind expectedSegment,
        ulong expectedProviderGeneration)
        where TTarget : IOperationTargetMarker
    {
        handle.Route.ProviderId.Should().Be(AppleVirtualizationProviderDescriptor.ProviderId);
        handle.Route.ProviderHandle.Should().NotBeNull();
        handle.Route.ProviderHandle!.Value.ProviderId.Should().Be(AppleVirtualizationProviderDescriptor.ProviderId);
        handle.ProviderGeneration.Should().Be(expectedProviderGeneration);
        handle.Route.Segments.Should().Contain(segment => segment.Kind == expectedSegment);
    }
}
