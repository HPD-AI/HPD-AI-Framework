using System.Diagnostics;

namespace HPD.Auth.Base;

// These authoritative DTOs expose values only through named, authorized persistence
// boundaries. Record-generated formatting must never become a second Secret channel.

[DebuggerDisplay("{ToString(),nq}")]
internal sealed partial record AuthRecoveryCodeRecordV1
{
    public override string ToString() => "AuthRecoveryCodeRecordV1[redacted]";
}

[DebuggerDisplay("{ToString(),nq}")]
internal sealed partial record AuthPasskeyRecordV1
{
    public override string ToString() => "AuthPasskeyRecordV1[redacted]";
}

[DebuggerDisplay("{ToString(),nq}")]
internal sealed partial record AuthRefreshTokenDeliveryRecordV1
{
    public override string ToString() => "AuthRefreshTokenDeliveryRecordV1[redacted]";
}

[DebuggerDisplay("{ToString(),nq}")]
internal sealed partial record AuthUserTokenRecordV1
{
    public override string ToString() => "AuthUserTokenRecordV1[redacted]";
}

[DebuggerDisplay("{ToString(),nq}")]
internal sealed partial record AuthUserRecordV1
{
    public override string ToString() => "AuthUserRecordV1[redacted]";
}

[DebuggerDisplay("{ToString(),nq}")]
internal sealed partial record AuthDataProtectionKeyRecordV1
{
    public override string ToString() => "AuthDataProtectionKeyRecordV1[redacted]";
}

[DebuggerDisplay("{ToString(),nq}")]
internal sealed partial record AuthSsoProviderRecordV1
{
    public override string ToString() => "AuthSsoProviderRecordV1[redacted]";
}

[DebuggerDisplay("{ToString(),nq}")]
internal sealed partial record AuthUserIdentityRecordV1
{
    public override string ToString() => "AuthUserIdentityRecordV1[redacted]";
}

[DebuggerDisplay("{ToString(),nq}")]
internal sealed partial record AuthPasskeyRegisterV1
{
    public override string ToString() => "AuthPasskeyRegisterV1[redacted]";
}

[DebuggerDisplay("{ToString(),nq}")]
internal sealed partial record AuthRecoveryNewSlotV1
{
    public override string ToString() => "AuthRecoveryNewSlotV1[redacted]";
}

[DebuggerDisplay("{ToString(),nq}")]
internal sealed partial record AuthRecoveryCodesReplaceV1
{
    public override string ToString() => "AuthRecoveryCodesReplaceV1[redacted]";
}

[DebuggerDisplay("{ToString(),nq}")]
internal sealed partial record AuthRecoveryCodeConsumeV1
{
    public override string ToString() => "AuthRecoveryCodeConsumeV1[redacted]";
}

[DebuggerDisplay("{ToString(),nq}")]
internal sealed partial record AuthCreateUserV1
{
    public override string ToString() => "AuthCreateUserV1[redacted]";
}

[DebuggerDisplay("{ToString(),nq}")]
internal sealed partial record AuthChangePasswordV1
{
    public override string ToString() => "AuthChangePasswordV1[redacted]";
}

[DebuggerDisplay("{ToString(),nq}")]
internal sealed partial record AuthRemovePasswordV1
{
    public override string ToString() => "AuthRemovePasswordV1[redacted]";
}

[DebuggerDisplay("{ToString(),nq}")]
internal sealed partial record AuthResetPasswordV1
{
    public override string ToString() => "AuthResetPasswordV1[redacted]";
}

[DebuggerDisplay("{ToString(),nq}")]
internal sealed partial record AuthSetSecurityStateV1
{
    public override string ToString() => "AuthSetSecurityStateV1[redacted]";
}

internal sealed partial record AuthUserPasswordReadV1
{
    [DebuggerDisplay("{ToString(),nq}")]
    public sealed partial record Row
    {
        public override string ToString() => "AuthUserPasswordReadV1.Row[redacted]";
    }
}

internal sealed partial record AuthUserTwoFactorSecretsReadV1
{
    [DebuggerDisplay("{ToString(),nq}")]
    public sealed partial record Row
    {
        public override string ToString() => "AuthUserTwoFactorSecretsReadV1.Row[redacted]";
    }
}

internal sealed partial record AuthUserTokenSecretReadV1
{
    [DebuggerDisplay("{ToString(),nq}")]
    public sealed partial record Row
    {
        public override string ToString() => "AuthUserTokenSecretReadV1.Row[redacted]";
    }
}
