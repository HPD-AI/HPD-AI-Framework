using System.Text.Json.Serialization;
using HPD.Base;

namespace HPD.Auth.Base;

internal sealed record AuthRecoveryPriorSlotV1
{
    [BaseField("auth.operation.recoveryCodes.replace.prior.active")] public required bool Active { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior.id", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64)] public required string Id { get; init; }
}

internal sealed record AuthRecoveryNewSlotV1
{
    [BaseField("auth.operation.recoveryCodes.replace.new.active")] public required bool Active { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new.codeDigest", MaximumBytes = 32), BaseFieldConfidentiality(BaseFieldConfidentiality.Secret)] public required BaseBinary CodeDigest { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new.digestKeyVersion", HasMinimumInt32 = true, MinimumInt32 = 1)] public required int DigestKeyVersion { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new.id", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64)] public required string Id { get; init; }
}

internal sealed partial record AuthRecoveryCodesReplaceV1
{
    [BaseField("auth.operation.recoveryCodes.replace.new00")] public required AuthRecoveryNewSlotV1 New00 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new01")] public required AuthRecoveryNewSlotV1 New01 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new02")] public required AuthRecoveryNewSlotV1 New02 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new03")] public required AuthRecoveryNewSlotV1 New03 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new04")] public required AuthRecoveryNewSlotV1 New04 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new05")] public required AuthRecoveryNewSlotV1 New05 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new06")] public required AuthRecoveryNewSlotV1 New06 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new07")] public required AuthRecoveryNewSlotV1 New07 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new08")] public required AuthRecoveryNewSlotV1 New08 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new09")] public required AuthRecoveryNewSlotV1 New09 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new10")] public required AuthRecoveryNewSlotV1 New10 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new11")] public required AuthRecoveryNewSlotV1 New11 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new12")] public required AuthRecoveryNewSlotV1 New12 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new13")] public required AuthRecoveryNewSlotV1 New13 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new14")] public required AuthRecoveryNewSlotV1 New14 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new15")] public required AuthRecoveryNewSlotV1 New15 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new16")] public required AuthRecoveryNewSlotV1 New16 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new17")] public required AuthRecoveryNewSlotV1 New17 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new18")] public required AuthRecoveryNewSlotV1 New18 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new19")] public required AuthRecoveryNewSlotV1 New19 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new20")] public required AuthRecoveryNewSlotV1 New20 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new21")] public required AuthRecoveryNewSlotV1 New21 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new22")] public required AuthRecoveryNewSlotV1 New22 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new23")] public required AuthRecoveryNewSlotV1 New23 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new24")] public required AuthRecoveryNewSlotV1 New24 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new25")] public required AuthRecoveryNewSlotV1 New25 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new26")] public required AuthRecoveryNewSlotV1 New26 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new27")] public required AuthRecoveryNewSlotV1 New27 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new28")] public required AuthRecoveryNewSlotV1 New28 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new29")] public required AuthRecoveryNewSlotV1 New29 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new30")] public required AuthRecoveryNewSlotV1 New30 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new31")] public required AuthRecoveryNewSlotV1 New31 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new32")] public required AuthRecoveryNewSlotV1 New32 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new33")] public required AuthRecoveryNewSlotV1 New33 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new34")] public required AuthRecoveryNewSlotV1 New34 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new35")] public required AuthRecoveryNewSlotV1 New35 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new36")] public required AuthRecoveryNewSlotV1 New36 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new37")] public required AuthRecoveryNewSlotV1 New37 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new38")] public required AuthRecoveryNewSlotV1 New38 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new39")] public required AuthRecoveryNewSlotV1 New39 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new40")] public required AuthRecoveryNewSlotV1 New40 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new41")] public required AuthRecoveryNewSlotV1 New41 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new42")] public required AuthRecoveryNewSlotV1 New42 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new43")] public required AuthRecoveryNewSlotV1 New43 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new44")] public required AuthRecoveryNewSlotV1 New44 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new45")] public required AuthRecoveryNewSlotV1 New45 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new46")] public required AuthRecoveryNewSlotV1 New46 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new47")] public required AuthRecoveryNewSlotV1 New47 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new48")] public required AuthRecoveryNewSlotV1 New48 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new49")] public required AuthRecoveryNewSlotV1 New49 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new50")] public required AuthRecoveryNewSlotV1 New50 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new51")] public required AuthRecoveryNewSlotV1 New51 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new52")] public required AuthRecoveryNewSlotV1 New52 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new53")] public required AuthRecoveryNewSlotV1 New53 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new54")] public required AuthRecoveryNewSlotV1 New54 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new55")] public required AuthRecoveryNewSlotV1 New55 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new56")] public required AuthRecoveryNewSlotV1 New56 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new57")] public required AuthRecoveryNewSlotV1 New57 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new58")] public required AuthRecoveryNewSlotV1 New58 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new59")] public required AuthRecoveryNewSlotV1 New59 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new60")] public required AuthRecoveryNewSlotV1 New60 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new61")] public required AuthRecoveryNewSlotV1 New61 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new62")] public required AuthRecoveryNewSlotV1 New62 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.new63")] public required AuthRecoveryNewSlotV1 New63 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior00")] public required AuthRecoveryPriorSlotV1 Prior00 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior01")] public required AuthRecoveryPriorSlotV1 Prior01 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior02")] public required AuthRecoveryPriorSlotV1 Prior02 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior03")] public required AuthRecoveryPriorSlotV1 Prior03 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior04")] public required AuthRecoveryPriorSlotV1 Prior04 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior05")] public required AuthRecoveryPriorSlotV1 Prior05 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior06")] public required AuthRecoveryPriorSlotV1 Prior06 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior07")] public required AuthRecoveryPriorSlotV1 Prior07 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior08")] public required AuthRecoveryPriorSlotV1 Prior08 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior09")] public required AuthRecoveryPriorSlotV1 Prior09 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior10")] public required AuthRecoveryPriorSlotV1 Prior10 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior11")] public required AuthRecoveryPriorSlotV1 Prior11 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior12")] public required AuthRecoveryPriorSlotV1 Prior12 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior13")] public required AuthRecoveryPriorSlotV1 Prior13 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior14")] public required AuthRecoveryPriorSlotV1 Prior14 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior15")] public required AuthRecoveryPriorSlotV1 Prior15 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior16")] public required AuthRecoveryPriorSlotV1 Prior16 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior17")] public required AuthRecoveryPriorSlotV1 Prior17 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior18")] public required AuthRecoveryPriorSlotV1 Prior18 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior19")] public required AuthRecoveryPriorSlotV1 Prior19 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior20")] public required AuthRecoveryPriorSlotV1 Prior20 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior21")] public required AuthRecoveryPriorSlotV1 Prior21 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior22")] public required AuthRecoveryPriorSlotV1 Prior22 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior23")] public required AuthRecoveryPriorSlotV1 Prior23 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior24")] public required AuthRecoveryPriorSlotV1 Prior24 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior25")] public required AuthRecoveryPriorSlotV1 Prior25 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior26")] public required AuthRecoveryPriorSlotV1 Prior26 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior27")] public required AuthRecoveryPriorSlotV1 Prior27 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior28")] public required AuthRecoveryPriorSlotV1 Prior28 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior29")] public required AuthRecoveryPriorSlotV1 Prior29 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior30")] public required AuthRecoveryPriorSlotV1 Prior30 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior31")] public required AuthRecoveryPriorSlotV1 Prior31 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior32")] public required AuthRecoveryPriorSlotV1 Prior32 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior33")] public required AuthRecoveryPriorSlotV1 Prior33 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior34")] public required AuthRecoveryPriorSlotV1 Prior34 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior35")] public required AuthRecoveryPriorSlotV1 Prior35 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior36")] public required AuthRecoveryPriorSlotV1 Prior36 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior37")] public required AuthRecoveryPriorSlotV1 Prior37 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior38")] public required AuthRecoveryPriorSlotV1 Prior38 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior39")] public required AuthRecoveryPriorSlotV1 Prior39 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior40")] public required AuthRecoveryPriorSlotV1 Prior40 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior41")] public required AuthRecoveryPriorSlotV1 Prior41 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior42")] public required AuthRecoveryPriorSlotV1 Prior42 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior43")] public required AuthRecoveryPriorSlotV1 Prior43 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior44")] public required AuthRecoveryPriorSlotV1 Prior44 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior45")] public required AuthRecoveryPriorSlotV1 Prior45 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior46")] public required AuthRecoveryPriorSlotV1 Prior46 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior47")] public required AuthRecoveryPriorSlotV1 Prior47 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior48")] public required AuthRecoveryPriorSlotV1 Prior48 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior49")] public required AuthRecoveryPriorSlotV1 Prior49 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior50")] public required AuthRecoveryPriorSlotV1 Prior50 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior51")] public required AuthRecoveryPriorSlotV1 Prior51 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior52")] public required AuthRecoveryPriorSlotV1 Prior52 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior53")] public required AuthRecoveryPriorSlotV1 Prior53 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior54")] public required AuthRecoveryPriorSlotV1 Prior54 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior55")] public required AuthRecoveryPriorSlotV1 Prior55 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior56")] public required AuthRecoveryPriorSlotV1 Prior56 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior57")] public required AuthRecoveryPriorSlotV1 Prior57 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior58")] public required AuthRecoveryPriorSlotV1 Prior58 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior59")] public required AuthRecoveryPriorSlotV1 Prior59 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior60")] public required AuthRecoveryPriorSlotV1 Prior60 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior61")] public required AuthRecoveryPriorSlotV1 Prior61 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior62")] public required AuthRecoveryPriorSlotV1 Prior62 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.prior63")] public required AuthRecoveryPriorSlotV1 Prior63 { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.concurrencyStamp", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public required string ConcurrencyStamp { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.expectedSecurityGeneration")] public required BaseModuleGeneration ExpectedSecurityGeneration { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.expectedUserRevision")] public required RevisionToken ExpectedUserRevision { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.newCount", HasMinimumInt32 = true, MinimumInt32 = 0, HasMaximumInt32 = true, MaximumInt32 = 64)] public required int NewCount { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.operationTime"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset OperationTime { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.priorCount", HasMinimumInt32 = true, MinimumInt32 = 0, HasMaximumInt32 = true, MaximumInt32 = 64)] public required int PriorCount { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.securityStamp", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Secret)] public required string SecurityStamp { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.tenantId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid TenantId { get; init; }
    [BaseField("auth.operation.recoveryCodes.replace.userId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid UserId { get; init; }
}

internal sealed record AuthRecoveryCodeConsumeV1
{
    [BaseField("auth.operation.recoveryCode.consume.codeDigest", MaximumBytes = 32), BaseFieldConfidentiality(BaseFieldConfidentiality.Secret)] public required BaseBinary CodeDigest { get; init; }
    [BaseField("auth.operation.recoveryCode.consume.codeId", MinimumUtf8Bytes = 64, MaximumUtf8Bytes = 64)] public required string CodeId { get; init; }
    [BaseField("auth.operation.recoveryCode.consume.concurrencyStamp", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc)] public required string ConcurrencyStamp { get; init; }
    [BaseField("auth.operation.recoveryCode.consume.expectedCodeRevision")] public required RevisionToken ExpectedCodeRevision { get; init; }
    [BaseField("auth.operation.recoveryCode.consume.expectedSecurityGeneration")] public required BaseModuleGeneration ExpectedSecurityGeneration { get; init; }
    [BaseField("auth.operation.recoveryCode.consume.expectedUserRevision")] public required RevisionToken ExpectedUserRevision { get; init; }
    [BaseField("auth.operation.recoveryCode.consume.operationTime"), JsonConverter(typeof(BaseUtcDateTimeJsonConverter))] public required DateTimeOffset OperationTime { get; init; }
    [BaseField("auth.operation.recoveryCode.consume.securityStamp", MinimumUtf8Bytes = 1, MaximumUtf8Bytes = 256, StringNormalization = BaseStringNormalizationRequirement.RequireNfc), BaseFieldConfidentiality(BaseFieldConfidentiality.Secret)] public required string SecurityStamp { get; init; }
    [BaseField("auth.operation.recoveryCode.consume.tenantId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid TenantId { get; init; }
    [BaseField("auth.operation.recoveryCode.consume.userId"), JsonConverter(typeof(BaseCanonicalGuidJsonConverter))] public required Guid UserId { get; init; }
}

internal sealed record AuthRecoveryCodeMutationResultV1
{
    [BaseField("auth.operation.recoveryCode.result.securityGeneration")] public required BaseModuleGeneration SecurityGeneration { get; init; }
    [BaseField("auth.operation.recoveryCode.result.userRevision")] public required RevisionToken UserRevision { get; init; }
}
