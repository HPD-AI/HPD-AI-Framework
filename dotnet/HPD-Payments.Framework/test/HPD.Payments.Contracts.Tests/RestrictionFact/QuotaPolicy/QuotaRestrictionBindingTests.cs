using HPD.Payments.Contracts.RestrictionFact.QuotaPolicy;
using HPD.Payments.Primitives.Identity;

namespace HPD.Payments.Contracts.Tests.RestrictionFact.QuotaPolicy;

internal static class QuotaRestrictionBindingTests
{
    internal static void Run()
    {
        ScopeId scope = ScopeId.Create("tenant", "live", "quota");
        var subject = SemanticId.Create(scope, "quota", "subject", "one");
        var owner = SemanticId.Create(scope, "restriction", "owner", "quota-policy");
        var fact = SemanticId.Create(scope, "restriction", "fact", "one");
        var binding = new QuotaRestrictionBinding(subject, owner, fact, "api-calls", "request", Revision.Create("policy", 1), OwnerGeneration.Create(3));
        if (!binding.CanRelease(owner, OwnerGeneration.Create(3)) || binding.CanRelease(owner, OwnerGeneration.Create(2)))
            throw new InvalidOperationException("Quota restriction owner fencing failed.");
    }
}
