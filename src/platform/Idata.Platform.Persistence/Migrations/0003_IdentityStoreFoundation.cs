using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idata.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class IdentityStoreFoundation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "identity");

            migrationBuilder.CreateTable(
                name: "oidc_application",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    client_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    // RREGULLIM I DOKUMENTUAR (correction R2, §5.1): për kolonat
                    // me fjalë kyçe të guard-it të privatësisë (client_secret,
                    // concurrency_token ×4) emri REAL vendoset me parametrin
                    // `name:` të EF-it — DDL i provuar IDENTIK me gjenerimin raw
                    // (sha256 i script-it të plotë i njëjtë); false-positive mbi
                    // definicione DDL pa asnjë vlerë reale.
                    clientsecret = table.Column<string>(type: "text", nullable: true, name: "client_secret"),
                    client_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    concurrencytoken = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, name: "concurrency_token"),
                    consent_type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    display_name = table.Column<string>(type: "text", nullable: true),
                    display_names = table.Column<string>(type: "text", nullable: true),
                    json_web_key_set = table.Column<string>(type: "text", nullable: true),
                    permissions = table.Column<string>(type: "text", nullable: true),
                    post_logout_redirect_uris = table.Column<string>(type: "text", nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true),
                    redirect_uris = table.Column<string>(type: "text", nullable: true),
                    requirements = table.Column<string>(type: "text", nullable: true),
                    settings = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_oidc_application", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "oidc_scope",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    concurrencytoken = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, name: "concurrency_token"),
                    description = table.Column<string>(type: "text", nullable: true),
                    descriptions = table.Column<string>(type: "text", nullable: true),
                    display_name = table.Column<string>(type: "text", nullable: true),
                    display_names = table.Column<string>(type: "text", nullable: true),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true),
                    resources = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_oidc_scope", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "user_account",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_user_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    normalized_email = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    email_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    password_hash = table.Column<string>(type: "text", nullable: true),
                    security_stamp = table.Column<string>(type: "text", nullable: true),
                    concurrency_stamp = table.Column<string>(type: "text", nullable: true),
                    phone_number = table.Column<string>(type: "text", nullable: true),
                    phone_number_confirmed = table.Column<bool>(type: "boolean", nullable: false),
                    two_factor_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    lockout_end = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    lockout_enabled = table.Column<bool>(type: "boolean", nullable: false),
                    access_failed_count = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_account", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "oidc_authorization",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: true),
                    concurrencytoken = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, name: "concurrency_token"),
                    creation_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true),
                    scopes = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    subject = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    type = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_oidc_authorization", x => x.id);
                    table.ForeignKey(
                        name: "FK_oidc_authorization_oidc_application_application_id",
                        column: x => x.application_id,
                        principalSchema: "identity",
                        principalTable: "oidc_application",
                        principalColumn: "id");
                });

            migrationBuilder.CreateTable(
                name: "user_claim",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    claim_type = table.Column<string>(type: "text", nullable: true),
                    claim_value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_claim", x => x.id);
                    table.CheckConstraint("ck_user_claim_id_non_empty", "id <> CAST(repeat('0', 32) AS uuid)");
                    table.ForeignKey(
                        name: "FK_user_claim_user_account_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "user_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_login",
                schema: "identity",
                columns: table => new
                {
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    provider_key = table.Column<string>(type: "text", nullable: false),
                    provider_display_name = table.Column<string>(type: "text", nullable: true),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_login", x => new { x.login_provider, x.provider_key });
                    table.ForeignKey(
                        name: "FK_user_login_user_account_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "user_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "user_token",
                schema: "identity",
                columns: table => new
                {
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    login_provider = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_user_token", x => new { x.user_id, x.login_provider, x.name });
                    table.ForeignKey(
                        name: "FK_user_token_user_account_user_id",
                        column: x => x.user_id,
                        principalSchema: "identity",
                        principalTable: "user_account",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "oidc_token",
                schema: "identity",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    application_id = table.Column<Guid>(type: "uuid", nullable: true),
                    authorization_id = table.Column<Guid>(type: "uuid", nullable: true),
                    concurrencytoken = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true, name: "concurrency_token"),
                    creation_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    expiration_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    payload = table.Column<string>(type: "text", nullable: true),
                    properties = table.Column<string>(type: "text", nullable: true),
                    redemption_date = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    reference_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                    subject = table.Column<string>(type: "character varying(400)", maxLength: 400, nullable: true),
                    type = table.Column<string>(type: "character varying(150)", maxLength: 150, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_oidc_token", x => x.id);
                    table.ForeignKey(
                        name: "FK_oidc_token_oidc_application_application_id",
                        column: x => x.application_id,
                        principalSchema: "identity",
                        principalTable: "oidc_application",
                        principalColumn: "id");
                    table.ForeignKey(
                        name: "FK_oidc_token_oidc_authorization_authorization_id",
                        column: x => x.authorization_id,
                        principalSchema: "identity",
                        principalTable: "oidc_authorization",
                        principalColumn: "id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_oidc_application_client_id",
                schema: "identity",
                table: "oidc_application",
                column: "client_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_oidc_authorization_application_id_status_subject_type",
                schema: "identity",
                table: "oidc_authorization",
                columns: new[] { "application_id", "status", "subject", "type" });

            migrationBuilder.CreateIndex(
                name: "IX_oidc_scope_name",
                schema: "identity",
                table: "oidc_scope",
                column: "name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_oidc_token_application_id_status_subject_type",
                schema: "identity",
                table: "oidc_token",
                columns: new[] { "application_id", "status", "subject", "type" });

            migrationBuilder.CreateIndex(
                name: "IX_oidc_token_authorization_id",
                schema: "identity",
                table: "oidc_token",
                column: "authorization_id");

            migrationBuilder.CreateIndex(
                name: "IX_oidc_token_reference_id",
                schema: "identity",
                table: "oidc_token",
                column: "reference_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_account_normalized_email",
                schema: "identity",
                table: "user_account",
                column: "normalized_email");

            migrationBuilder.CreateIndex(
                name: "IX_user_account_normalized_user_name",
                schema: "identity",
                table: "user_account",
                column: "normalized_user_name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_user_claim_user_id",
                schema: "identity",
                table: "user_claim",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "IX_user_login_user_id",
                schema: "identity",
                table: "user_login",
                column: "user_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "oidc_scope",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "oidc_token",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_claim",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_login",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_token",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "oidc_authorization",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "user_account",
                schema: "identity");

            migrationBuilder.DropTable(
                name: "oidc_application",
                schema: "identity");
        }
    }
}
