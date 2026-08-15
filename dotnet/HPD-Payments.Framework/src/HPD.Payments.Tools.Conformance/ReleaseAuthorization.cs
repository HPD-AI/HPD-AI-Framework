using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace HPD.Payments.Tools.Conformance;

internal enum ReleaseAuthorizationAction { Publish = 0, Withdraw }

internal sealed record ReleaseApproval
{
    internal required string SchemaVersion { get; init; }
    internal required string ManifestAddress { get; init; }
    internal required ReleaseAuthorizationAction Action { get; init; }
    internal required string ApproverId { get; init; }
    internal required string KeyId { get; init; }
    internal required string PolicyRevision { get; init; }
    internal required DateTimeOffset IssuedAtUtc { get; init; }
    internal required DateTimeOffset ExpiresAtUtc { get; init; }
    internal required string Signature { get; init; }

    internal string UnsignedCanonicalText() => ProofCanonical.Join(SchemaVersion, ManifestAddress, Action.ToString(),
        ApproverId, KeyId, PolicyRevision, IssuedAtUtc.ToString("O", CultureInfo.InvariantCulture),
        ExpiresAtUtc.ToString("O", CultureInfo.InvariantCulture));

    internal string ToCanonicalText() => ProofCanonical.Join(UnsignedCanonicalText(), Signature);

    internal string ContentAddress() => Convert.ToHexStringLower(
        SHA256.HashData(Encoding.UTF8.GetBytes(ToCanonicalText())));

    internal static ReleaseApproval Parse(string canonical)
    {
        var outer = ProofCanonical.Split(canonical, 2);
        var fields = ProofCanonical.Split(outer[0], 8);
        if (!Enum.TryParse<ReleaseAuthorizationAction>(fields[2], false, out var action) || !Enum.IsDefined(action) ||
            !DateTimeOffset.TryParseExact(fields[6], "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var issued) ||
            !DateTimeOffset.TryParseExact(fields[7], "O", CultureInfo.InvariantCulture, DateTimeStyles.None, out var expires))
            throw new InvalidDataException("Release approval encoding is invalid.");
        var approval = new ReleaseApproval { SchemaVersion = fields[0], ManifestAddress = fields[1], Action = action,
            ApproverId = fields[3], KeyId = fields[4], PolicyRevision = fields[5], IssuedAtUtc = issued,
            ExpiresAtUtc = expires, Signature = outer[1] };
        if (!ValidateEncodedShape(approval)) throw new InvalidDataException("Release approval encoding is malformed.");
        return approval;
    }

    private static bool ValidateEncodedShape(ReleaseApproval approval)
    {
        if (approval.SchemaVersion != "hpd.payments.release-approval.v1" || approval.ManifestAddress.Length != 64 ||
            approval.ManifestAddress.Any(static c => !(c is >= '0' and <= '9' or >= 'a' and <= 'f')) ||
            approval.ApproverId.Length is 0 or > 256 || approval.KeyId.Length != 71 ||
            !approval.KeyId.StartsWith("sha256:", StringComparison.Ordinal) || approval.PolicyRevision.Length is 0 or > 256 ||
            approval.IssuedAtUtc.Offset != TimeSpan.Zero || approval.ExpiresAtUtc.Offset != TimeSpan.Zero ||
            approval.IssuedAtUtc >= approval.ExpiresAtUtc) return false;
        try { return Convert.FromBase64String(approval.Signature).Length == 64; }
        catch (FormatException) { return false; }
    }
}

internal sealed class ReleaseApprovalKey
{
    private readonly byte[] _subjectPublicKeyInfo;
    private readonly HashSet<ReleaseAuthorizationAction> _allowedActions;
    internal string KeyId { get; }
    internal string ApproverId { get; }
    internal DateTimeOffset ValidFromUtc { get; }
    internal DateTimeOffset ValidUntilUtc { get; }
    internal IReadOnlySet<ReleaseAuthorizationAction> AllowedActions => _allowedActions;
    internal ReadOnlySpan<byte> SubjectPublicKeyInfo => _subjectPublicKeyInfo;

    internal ReleaseApprovalKey(string approverId, ReadOnlySpan<byte> subjectPublicKeyInfo,
        DateTimeOffset validFromUtc, DateTimeOffset validUntilUtc,
        IEnumerable<ReleaseAuthorizationAction> allowedActions)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(approverId);
        ArgumentNullException.ThrowIfNull(allowedActions);
        if (approverId.Length > 256 || subjectPublicKeyInfo.Length is < 32 or > 4096 ||
            validFromUtc.Offset != TimeSpan.Zero || validUntilUtc.Offset != TimeSpan.Zero || validFromUtc >= validUntilUtc)
            throw new ArgumentException("Release approval key metadata is invalid.");
        _subjectPublicKeyInfo = subjectPublicKeyInfo.ToArray();
        _allowedActions = new(allowedActions);
        if (_allowedActions.Count == 0 || _allowedActions.Any(static action => !Enum.IsDefined(action)))
            throw new ArgumentException("Release approval key has no valid action.");
        ApproverId = approverId; ValidFromUtc = validFromUtc; ValidUntilUtc = validUntilUtc;
        KeyId = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(_subjectPublicKeyInfo));
    }
}

internal sealed record ReleaseAuthorizationPolicy(string Revision, int RequiredApprovals,
    IReadOnlySet<string> AllowedKeyIds);

internal sealed record ReleaseAuthorizationContext(IReadOnlyList<ReleaseApproval> Approvals,
    IReadOnlyDictionary<string, ReleaseApprovalKey> Keys, ReleaseAuthorizationPolicy Policy, DateTimeOffset EvaluatedAtUtc);

internal static class ReleaseAuthorizationValidator
{
    internal static IReadOnlyList<string> Validate(ReleaseManifest manifest, ReleaseAuthorizationContext context)
    {
        ArgumentNullException.ThrowIfNull(manifest); ArgumentNullException.ThrowIfNull(context);
        var errors = new List<string>();
        if (manifest.Lifecycle == ReleaseManifestLifecycle.Candidate)
        {
            if (context.Approvals.Count != 0) errors.Add("candidate-manifest-has-release-approval");
            return errors;
        }
        var action = manifest.Lifecycle == ReleaseManifestLifecycle.Published
            ? ReleaseAuthorizationAction.Publish : ReleaseAuthorizationAction.Withdraw;
        if (context.EvaluatedAtUtc.Offset != TimeSpan.Zero || string.IsNullOrWhiteSpace(context.Policy.Revision) ||
            context.Policy.RequiredApprovals is < 1 or > 32 || context.Policy.AllowedKeyIds.Count < context.Policy.RequiredApprovals)
            return ["release-authorization-policy-invalid"];

        var validApprovers = new HashSet<string>(StringComparer.Ordinal);
        foreach (var approval in context.Approvals)
        {
            if (!ValidateApprovalShape(approval, manifest.ContentAddress(), action, context.Policy.Revision, context.EvaluatedAtUtc, errors))
                continue;
            if (!context.Policy.AllowedKeyIds.Contains(approval.KeyId) || !context.Keys.TryGetValue(approval.KeyId, out var key) ||
                !StringComparer.Ordinal.Equals(key.ApproverId, approval.ApproverId) || !key.AllowedActions.Contains(action) ||
                approval.IssuedAtUtc < key.ValidFromUtc || approval.IssuedAtUtc >= key.ValidUntilUtc ||
                context.EvaluatedAtUtc >= key.ValidUntilUtc)
            {
                errors.Add("release-approval-key-not-authorized"); continue;
            }
            if (!Verify(approval, key)) { errors.Add("release-approval-signature-invalid"); continue; }
            if (!validApprovers.Add(approval.ApproverId)) errors.Add("release-approval-approver-duplicated");
        }
        if (validApprovers.Count < context.Policy.RequiredApprovals) errors.Add("release-approval-threshold-not-met");
        return errors.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();
    }

    private static bool ValidateApprovalShape(ReleaseApproval approval, string manifestAddress,
        ReleaseAuthorizationAction action, string policyRevision, DateTimeOffset evaluatedAtUtc, List<string> errors)
    {
        if (approval.SchemaVersion != "hpd.payments.release-approval.v1" ||
            !StringComparer.Ordinal.Equals(approval.ManifestAddress, manifestAddress) || approval.Action != action ||
            !StringComparer.Ordinal.Equals(approval.PolicyRevision, policyRevision) ||
            string.IsNullOrWhiteSpace(approval.ApproverId) || approval.ApproverId.Length > 256 ||
            approval.IssuedAtUtc.Offset != TimeSpan.Zero || approval.ExpiresAtUtc.Offset != TimeSpan.Zero ||
            approval.IssuedAtUtc >= approval.ExpiresAtUtc || evaluatedAtUtc < approval.IssuedAtUtc || evaluatedAtUtc >= approval.ExpiresAtUtc)
        {
            errors.Add("release-approval-envelope-invalid"); return false;
        }
        try { if (Convert.FromBase64String(approval.Signature).Length != 64) throw new FormatException(); }
        catch (FormatException) { errors.Add("release-approval-signature-malformed"); return false; }
        return true;
    }

    private static bool Verify(ReleaseApproval approval, ReleaseApprovalKey key)
    {
        using var algorithm = ECDsa.Create();
        try { algorithm.ImportSubjectPublicKeyInfo(key.SubjectPublicKeyInfo, out var read); if (read != key.SubjectPublicKeyInfo.Length) return false; }
        catch (CryptographicException) { return false; }
        return algorithm.VerifyData(Encoding.UTF8.GetBytes(approval.UnsignedCanonicalText()),
            Convert.FromBase64String(approval.Signature), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }
}

internal static class ReleaseApprovalSigner
{
    internal static ReleaseApproval Sign(ReleaseManifest manifest, ReleaseAuthorizationAction action,
        string approverId, string policyRevision, DateTimeOffset issuedAtUtc, DateTimeOffset expiresAtUtc, ECDsa key)
    {
        ArgumentNullException.ThrowIfNull(manifest); ArgumentNullException.ThrowIfNull(key);
        var keyId = "sha256:" + Convert.ToHexStringLower(SHA256.HashData(key.ExportSubjectPublicKeyInfo()));
        var approval = new ReleaseApproval { SchemaVersion = "hpd.payments.release-approval.v1",
            ManifestAddress = manifest.ContentAddress(), Action = action, ApproverId = approverId, KeyId = keyId,
            PolicyRevision = policyRevision, IssuedAtUtc = issuedAtUtc, ExpiresAtUtc = expiresAtUtc, Signature = string.Empty };
        var signature = key.SignData(Encoding.UTF8.GetBytes(approval.UnsignedCanonicalText()), HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        return approval with { Signature = Convert.ToBase64String(signature) };
    }
}
