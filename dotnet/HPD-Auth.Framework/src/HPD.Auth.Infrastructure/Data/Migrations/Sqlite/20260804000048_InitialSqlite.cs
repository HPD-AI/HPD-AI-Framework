using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace HPD.Auth.Infrastructure.Data.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class InitialSqlite : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AspNetRoles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    Created = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUsers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Audience = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    UserMetadata = table.Column<string>(type: "jsonb", nullable: false),
                    AppMetadata = table.Column<string>(type: "jsonb", nullable: false),
                    RequiredActions = table.Column<string>(type: "TEXT", nullable: false),
                    FirstName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    LastName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    AvatarUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    Created = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Updated = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastLoginAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    LastLoginIp = table.Column<string>(type: "TEXT", maxLength: 45, nullable: true),
                    SubscriptionTier = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    EmailConfirmedAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    UserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedUserName = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    Email = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    NormalizedEmail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    EmailConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: true),
                    SecurityStamp = table.Column<string>(type: "TEXT", nullable: true),
                    ConcurrencyStamp = table.Column<string>(type: "TEXT", nullable: true),
                    PhoneNumber = table.Column<string>(type: "TEXT", nullable: true),
                    PhoneNumberConfirmed = table.Column<bool>(type: "INTEGER", nullable: false),
                    TwoFactorEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    LockoutEnd = table.Column<DateTimeOffset>(type: "TEXT", nullable: true),
                    LockoutEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    AccessFailedCount = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUsers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AuthAuditEntries",
                columns: table => new
                {
                    AuditId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    OccurredAtUtc = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Category = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Success = table.Column<bool>(type: "INTEGER", nullable: false),
                    SubjectUserId = table.Column<Guid>(type: "TEXT", nullable: true),
                    SubjectSessionId = table.Column<Guid>(type: "TEXT", nullable: true),
                    IpAddress = table.Column<string>(type: "TEXT", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "TEXT", maxLength: 512, nullable: true),
                    FailureCode = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    CorrelationId = table.Column<string>(type: "TEXT", maxLength: 128, nullable: true),
                    FactsJson = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuthAuditEntries", x => x.AuditId);
                });

            migrationBuilder.CreateTable(
                name: "DataProtectionKeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    FriendlyName = table.Column<string>(type: "TEXT", nullable: true),
                    Xml = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DataProtectionKeys", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SSOProviders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProviderId = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ClientId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    ClientSecret = table.Column<string>(type: "TEXT", nullable: false),
                    Scopes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    EntityId = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    MetadataXml = table.Column<string>(type: "TEXT", nullable: true),
                    AttributeMapping = table.Column<string>(type: "jsonb", nullable: true),
                    NameIdFormat = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    SigningCertificate = table.Column<string>(type: "TEXT", nullable: true),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    Created = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SSOProviders", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "TenantSettings",
                columns: table => new
                {
                    InstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    LogoUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    FaviconUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    PrimaryColor = table.Column<string>(type: "TEXT", maxLength: 7, nullable: true),
                    AccentColor = table.Column<string>(type: "TEXT", maxLength: 7, nullable: true),
                    EmailFromName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    EmailFromAddress = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    SiteUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    SupportEmail = table.Column<string>(type: "TEXT", maxLength: 320, nullable: true),
                    Settings = table.Column<string>(type: "jsonb", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TenantSettings", x => x.InstanceId);
                });

            migrationBuilder.CreateTable(
                name: "AspNetRoleClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    RoleId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetRoleClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetRoleClaims_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserClaims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ClaimType = table.Column<string>(type: "TEXT", nullable: true),
                    ClaimValue = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserClaims", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AspNetUserClaims_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserLogins",
                columns: table => new
                {
                    LoginProvider = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderKey = table.Column<string>(type: "TEXT", nullable: false),
                    ProviderDisplayName = table.Column<string>(type: "TEXT", nullable: true),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserLogins", x => new { x.LoginProvider, x.ProviderKey });
                    table.ForeignKey(
                        name: "FK_AspNetUserLogins_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserRoles",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    RoleId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetRoles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "AspNetRoles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_AspNetUserRoles_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "AspNetUserTokens",
                columns: table => new
                {
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    LoginProvider = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AspNetUserTokens", x => new { x.UserId, x.LoginProvider, x.Name });
                    table.ForeignKey(
                        name: "FK_AspNetUserTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RefreshTokens",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Token = table.Column<string>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    JwtId = table.Column<string>(type: "TEXT", nullable: false),
                    SecurityStamp = table.Column<string>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsUsed = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsRevoked = table.Column<bool>(type: "INTEGER", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefreshTokens", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RefreshTokens_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserIdentities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Provider = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    ProviderId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    IdentityData = table.Column<string>(type: "jsonb", nullable: false),
                    LastSignInAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FederationSourceId = table.Column<Guid>(type: "TEXT", nullable: true),
                    LastSyncAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    ProviderTokens = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserIdentities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserIdentities_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserPasskeys",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CredentialId = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: false),
                    PublicKey = table.Column<string>(type: "TEXT", nullable: false),
                    SignatureCounter = table.Column<uint>(type: "INTEGER", nullable: false),
                    AaGuid = table.Column<Guid>(type: "TEXT", nullable: true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    UserVerified = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDiscoverable = table.Column<bool>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPasskeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserPasskeys_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    InstanceId = table.Column<Guid>(type: "TEXT", nullable: false),
                    UserId = table.Column<Guid>(type: "TEXT", nullable: false),
                    AAL = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    BrokerSessionId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    BrokerUserId = table.Column<string>(type: "TEXT", maxLength: 256, nullable: true),
                    SSOProviderId = table.Column<Guid>(type: "TEXT", nullable: true),
                    NotBefore = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NotAfter = table.Column<DateTime>(type: "TEXT", nullable: true),
                    OAuthClientId = table.Column<Guid>(type: "TEXT", nullable: true),
                    Scopes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    ClientSessions = table.Column<string>(type: "jsonb", nullable: true),
                    SessionState = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IpAddress = table.Column<string>(type: "TEXT", maxLength: 45, nullable: true),
                    UserAgent = table.Column<string>(type: "TEXT", maxLength: 1024, nullable: true),
                    DeviceInfo = table.Column<string>(type: "TEXT", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastActiveAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsRevoked = table.Column<bool>(type: "INTEGER", nullable: false),
                    RevokedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSessions_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AspNetRoleClaims_RoleId",
                table: "AspNetRoleClaims",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "RoleNameIndex",
                table: "AspNetRoles",
                column: "NormalizedName",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserClaims_UserId",
                table: "AspNetUserClaims",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserLogins_UserId",
                table: "AspNetUserLogins",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUserRoles_RoleId",
                table: "AspNetUserRoles",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "EmailIndex",
                table: "AspNetUsers",
                column: "NormalizedEmail");

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_InstanceId_NormalizedEmail",
                table: "AspNetUsers",
                columns: new[] { "InstanceId", "NormalizedEmail" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AspNetUsers_InstanceId_NormalizedUserName",
                table: "AspNetUsers",
                columns: new[] { "InstanceId", "NormalizedUserName" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "UserNameIndex",
                table: "AspNetUsers",
                column: "NormalizedUserName");

            migrationBuilder.CreateIndex(
                name: "IX_AuthAuditEntries_InstanceId_Action_OccurredAtUtc_AuditId",
                table: "AuthAuditEntries",
                columns: new[] { "InstanceId", "Action", "OccurredAtUtc", "AuditId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuthAuditEntries_InstanceId_Category_OccurredAtUtc_AuditId",
                table: "AuthAuditEntries",
                columns: new[] { "InstanceId", "Category", "OccurredAtUtc", "AuditId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuthAuditEntries_InstanceId_CorrelationId_OccurredAtUtc_AuditId",
                table: "AuthAuditEntries",
                columns: new[] { "InstanceId", "CorrelationId", "OccurredAtUtc", "AuditId" });

            migrationBuilder.CreateIndex(
                name: "IX_AuthAuditEntries_InstanceId_SubjectUserId_OccurredAtUtc_AuditId",
                table: "AuthAuditEntries",
                columns: new[] { "InstanceId", "SubjectUserId", "OccurredAtUtc", "AuditId" });

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_Token",
                table: "RefreshTokens",
                column: "Token",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefreshTokens_UserId_IsRevoked",
                table: "RefreshTokens",
                columns: new[] { "UserId", "IsRevoked" });

            migrationBuilder.CreateIndex(
                name: "IX_SSOProviders_InstanceId_ProviderId",
                table: "SSOProviders",
                columns: new[] { "InstanceId", "ProviderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserIdentities_InstanceId_Provider_ProviderId",
                table: "UserIdentities",
                columns: new[] { "InstanceId", "Provider", "ProviderId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserIdentities_UserId",
                table: "UserIdentities",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserPasskeys_CredentialId",
                table: "UserPasskeys",
                column: "CredentialId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserPasskeys_UserId",
                table: "UserPasskeys",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_UserId_IsRevoked_ExpiresAt",
                table: "UserSessions",
                columns: new[] { "UserId", "IsRevoked", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AspNetRoleClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserClaims");

            migrationBuilder.DropTable(
                name: "AspNetUserLogins");

            migrationBuilder.DropTable(
                name: "AspNetUserRoles");

            migrationBuilder.DropTable(
                name: "AspNetUserTokens");

            migrationBuilder.DropTable(
                name: "AuthAuditEntries");

            migrationBuilder.DropTable(
                name: "DataProtectionKeys");

            migrationBuilder.DropTable(
                name: "RefreshTokens");

            migrationBuilder.DropTable(
                name: "SSOProviders");

            migrationBuilder.DropTable(
                name: "TenantSettings");

            migrationBuilder.DropTable(
                name: "UserIdentities");

            migrationBuilder.DropTable(
                name: "UserPasskeys");

            migrationBuilder.DropTable(
                name: "UserSessions");

            migrationBuilder.DropTable(
                name: "AspNetRoles");

            migrationBuilder.DropTable(
                name: "AspNetUsers");
        }
    }
}
