-- hpd.auth.legacy.sqlite.20260804000048.v1
-- Parameterless read-only extraction statements.

-- AspNetRoles
SELECT "Id","InstanceId","Description","Created","Name","NormalizedName","ConcurrencyStamp" FROM "AspNetRoles" ORDER BY "Id" COLLATE BINARY;

-- AspNetUsers
SELECT "Id","InstanceId","Audience","UserMetadata","AppMetadata","RequiredActions","FirstName","LastName","DisplayName","AvatarUrl","IsActive","IsDeleted","DeletedAt","Created","Updated","LastLoginAt","LastLoginIp","SubscriptionTier","EmailConfirmedAt","UserName","NormalizedUserName","Email","NormalizedEmail","EmailConfirmed","PasswordHash","SecurityStamp","ConcurrencyStamp","PhoneNumber","PhoneNumberConfirmed","TwoFactorEnabled","LockoutEnd","LockoutEnabled","AccessFailedCount" FROM "AspNetUsers" ORDER BY "Id" COLLATE BINARY;

-- AuthAuditEntries
SELECT "AuditId","InstanceId","OccurredAtUtc","Action","Category","Success","SubjectUserId","SubjectSessionId","IpAddress","UserAgent","FailureCode","CorrelationId","FactsJson" FROM "AuthAuditEntries" ORDER BY "AuditId" COLLATE BINARY;

-- DataProtectionKeys
SELECT "Id","FriendlyName","Xml" FROM "DataProtectionKeys" ORDER BY "Id";

-- SSOProviders
SELECT "Id","InstanceId","ProviderId","ClientId","ClientSecret","Scopes","EntityId","MetadataXml","AttributeMapping","NameIdFormat","SigningCertificate","IsEnabled","Created","UpdatedAt" FROM "SSOProviders" ORDER BY "Id" COLLATE BINARY;

-- TenantSettings
SELECT "InstanceId","DisplayName","LogoUrl","FaviconUrl","PrimaryColor","AccentColor","EmailFromName","EmailFromAddress","SiteUrl","SupportEmail","Settings","CreatedAt","UpdatedAt" FROM "TenantSettings" ORDER BY "InstanceId" COLLATE BINARY;

-- AspNetRoleClaims
SELECT "Id","RoleId","ClaimType","ClaimValue" FROM "AspNetRoleClaims" ORDER BY "Id";

-- AspNetUserClaims
SELECT "Id","UserId","ClaimType","ClaimValue" FROM "AspNetUserClaims" ORDER BY "Id";

-- AspNetUserLogins
SELECT "LoginProvider","ProviderKey","ProviderDisplayName","UserId" FROM "AspNetUserLogins" ORDER BY "LoginProvider" COLLATE BINARY,"ProviderKey" COLLATE BINARY;

-- AspNetUserRoles
SELECT "UserId","RoleId" FROM "AspNetUserRoles" ORDER BY "UserId" COLLATE BINARY,"RoleId" COLLATE BINARY;

-- AspNetUserTokens
SELECT "UserId","LoginProvider","Name","Value" FROM "AspNetUserTokens" ORDER BY "UserId" COLLATE BINARY,"LoginProvider" COLLATE BINARY,"Name" COLLATE BINARY;

-- RefreshTokens
SELECT "Id","Token","UserId","InstanceId","JwtId","SecurityStamp","ExpiresAt","CreatedAt","IsUsed","IsRevoked","RevokedAt" FROM "RefreshTokens" ORDER BY "Id" COLLATE BINARY;

-- UserIdentities
SELECT "Id","InstanceId","UserId","Provider","ProviderId","IdentityData","LastSignInAt","FederationSourceId","LastSyncAt","ProviderTokens","CreatedAt","UpdatedAt" FROM "UserIdentities" ORDER BY "Id" COLLATE BINARY;

-- UserPasskeys
SELECT "Id","InstanceId","UserId","CredentialId","PublicKey","SignatureCounter","AaGuid","Name","UserVerified","IsDiscoverable","CreatedAt","LastUsedAt" FROM "UserPasskeys" ORDER BY "Id" COLLATE BINARY;

-- UserSessions
SELECT "Id","InstanceId","UserId","AAL","BrokerSessionId","BrokerUserId","SSOProviderId","NotBefore","NotAfter","OAuthClientId","Scopes","ClientSessions","SessionState","IpAddress","UserAgent","DeviceInfo","CreatedAt","LastActiveAt","ExpiresAt","IsRevoked","RevokedAt" FROM "UserSessions" ORDER BY "Id" COLLATE BINARY;
