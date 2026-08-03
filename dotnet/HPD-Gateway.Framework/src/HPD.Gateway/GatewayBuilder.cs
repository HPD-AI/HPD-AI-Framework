using HPD.Gateway.Core;
using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;

namespace HPD.Gateway;

public sealed class GatewayBuilder
{
    private bool _sealed;
    private GatewayDeclarationFamilies _installedFamilies;
    private GatewayNodeActivationRequest? _initialCandidate;

    internal GatewayBuilder(IServiceCollection services) => Services = services;

    internal IServiceCollection Services { get; }

    public GatewayBuilder AddCoreFamilies()
    {
        ThrowIfSealed();
        _installedFamilies |= GatewayDeclarationFamilies.RequestTimeout |
            GatewayDeclarationFamilies.RequestTransforms |
            GatewayDeclarationFamilies.ResponseTransforms |
            GatewayDeclarationFamilies.CredentialDisposition;
        return this;
    }

    public GatewayBuilder UseInitialCandidate(GatewayNodeActivationRequest request)
    {
        ThrowIfSealed();
        ArgumentNullException.ThrowIfNull(request);
        if (_initialCandidate is not null)
            throw new InvalidOperationException("An initial Gateway candidate is already registered.");
        _initialCandidate = request with
        {
            Utf8Configuration = request.Utf8Configuration.IsDefault
                ? default
                : ImmutableArray.CreateRange(request.Utf8Configuration.AsSpan().ToArray())
        };
        return this;
    }

    internal GatewayCompositionState Seal()
    {
        ThrowIfSealed();
        _sealed = true;
        return new GatewayCompositionState(_installedFamilies, _initialCandidate);
    }

    internal void ThrowIfSealed()
    {
        if (_sealed)
            throw new InvalidOperationException("The HPD Gateway composition is already sealed.");
    }
}

internal sealed record GatewayCompositionState(
    GatewayDeclarationFamilies InstalledFamilies,
    GatewayNodeActivationRequest? InitialCandidate);

internal sealed class HpdGatewayCompositionMarker;
