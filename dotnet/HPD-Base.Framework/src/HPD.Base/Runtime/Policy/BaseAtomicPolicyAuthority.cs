using System.Security.Cryptography;
using System.Text;

namespace HPD.Base;

internal static class BaseAtomicPolicyAuthority
{
    internal static bool IsAdmissible(IReadOnlyList<BasePolicyEvaluation> evaluations) =>
        evaluations.Count > 0
        && evaluations.All(static evaluation =>
            evaluation.Decision.Effect == PolicyEffect.Allow
            && evaluation.Authority is not null);

    internal static string BindPlanDigest(string planDigest, BaseAtomicPolicyAuthorityDigest policyDigest)
    {
        byte[] plan = Encoding.UTF8.GetBytes(planDigest);
        byte[] policy = policyDigest.ToArray();
        byte[] framed = new byte[plan.Length + 1 + policy.Length];
        plan.CopyTo(framed, 0); framed[plan.Length] = 0; policy.CopyTo(framed, plan.Length + 1);
        return Convert.ToHexStringLower(SHA256.HashData(framed));
    }

    internal static BaseAtomicPolicyAuthorityDigest Compute(
        string applicationId,
        string operationIdentity,
        IReadOnlyList<BasePolicyEvaluation> evaluations)
    {
        using var stream = new MemoryStream();
        using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true))
        {
            BasePolicyAuthorityCanonicalizer.Write(writer, "base.atomic.policyAuthority.v1");
            BasePolicyAuthorityCanonicalizer.Write(writer, applicationId);
            BasePolicyAuthorityCanonicalizer.Write(writer, operationIdentity);
            BasePolicyAuthorityCanonicalizer.Write(writer, evaluations.Count);
            for (int ordinal = 0; ordinal < evaluations.Count; ordinal++)
            {
                BasePolicyEvaluation evaluation = evaluations[ordinal];
                BasePolicyAuthorityCanonicalizer.Write(writer, ordinal);
                writer.Write(evaluation.Authority is not null);
                if (evaluation.Authority is null) continue;
                BasePolicyEvaluationAuthority authority = evaluation.Authority;
                BasePolicyAuthorityCanonicalizer.Write(writer, authority.PolicyGraphGeneration);
                writer.Write(authority.PolicyOwnerChecksum.Length); writer.Write(authority.PolicyOwnerChecksum.AsSpan());
                byte[] checksum = authority.Checksum.ToArray();
                writer.Write(checksum.Length); writer.Write(checksum);
            }
        }
        return BaseAtomicPolicyAuthorityDigest.Create(SHA256.HashData(stream.GetBuffer().AsSpan(0, checked((int)stream.Length))));
    }
}
