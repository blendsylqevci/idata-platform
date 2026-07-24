using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Idata.Platform.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class F1AccessControlCore : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "app_user",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_app_user", x => x.id);
                    table.CheckConstraint("ck_app_user_status", "status IN ('active','disabled')");
                });

            migrationBuilder.CreateTable(
                name: "business",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business", x => x.id);
                    table.CheckConstraint("ck_business_status", "status IN ('active','disabled')");
                });

            migrationBuilder.CreateTable(
                name: "permission",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    feature_key = table.Column<string>(type: "text", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_permission", x => x.id);
                    table.CheckConstraint("ck_permission_action", "action IN ('R','C','U','X','P','A','E','Cl','Ro')");
                });

            migrationBuilder.CreateTable(
                name: "command_receipt",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    operation_key = table.Column<string>(type: "text", nullable: false),
                    idempotency_key = table.Column<string>(type: "text", nullable: false),
                    request_fingerprint = table.Column<string>(type: "text", nullable: false),
                    result_status = table.Column<string>(type: "text", nullable: false),
                    result_payload = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    completed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_command_receipt", x => x.id);
                    table.UniqueConstraint("AK_command_receipt_business_id_id", x => new { x.business_id, x.id });
                    table.CheckConstraint("ck_command_receipt_result_status", "result_status IN ('completed')");
                    table.ForeignKey(
                        name: "FK_command_receipt_business_business_id",
                        column: x => x.business_id,
                        principalSchema: "platform",
                        principalTable: "business",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "role",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    is_system = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role", x => x.id);
                    table.UniqueConstraint("AK_role_business_id_id", x => new { x.business_id, x.id });
                    table.ForeignKey(
                        name: "FK_role_business_business_id",
                        column: x => x.business_id,
                        principalSchema: "platform",
                        principalTable: "business",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "audit_log",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    entity = table.Column<string>(type: "text", nullable: false),
                    entity_id = table.Column<Guid>(type: "uuid", nullable: false),
                    action = table.Column<string>(type: "text", nullable: false),
                    before_state = table.Column<string>(type: "jsonb", nullable: true),
                    after_state = table.Column<string>(type: "jsonb", nullable: true),
                    command_receipt_id = table.Column<Guid>(type: "uuid", nullable: false),
                    correlation_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_audit_log", x => x.id);
                    table.ForeignKey(
                        name: "FK_audit_log_business_business_id",
                        column: x => x.business_id,
                        principalSchema: "platform",
                        principalTable: "business",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_audit_log_command_receipt_business_id_command_receipt_id",
                        columns: x => new { x.business_id, x.command_receipt_id },
                        principalSchema: "platform",
                        principalTable: "command_receipt",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "business_membership",
                schema: "platform",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    user_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    version = table.Column<long>(type: "bigint", nullable: false),
                    status = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_business_membership", x => x.id);
                    table.CheckConstraint("ck_business_membership_status", "status IN ('active','disabled')");
                    table.ForeignKey(
                        name: "FK_business_membership_app_user_user_id",
                        column: x => x.user_id,
                        principalSchema: "platform",
                        principalTable: "app_user",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_business_membership_business_business_id",
                        column: x => x.business_id,
                        principalSchema: "platform",
                        principalTable: "business",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_business_membership_role_business_id_role_id",
                        columns: x => new { x.business_id, x.role_id },
                        principalSchema: "platform",
                        principalTable: "role",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "role_permission",
                schema: "platform",
                columns: table => new
                {
                    business_id = table.Column<Guid>(type: "uuid", nullable: false),
                    role_id = table.Column<Guid>(type: "uuid", nullable: false),
                    permission_id = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_role_permission", x => new { x.business_id, x.role_id, x.permission_id });
                    table.ForeignKey(
                        name: "FK_role_permission_permission_permission_id",
                        column: x => x.permission_id,
                        principalSchema: "platform",
                        principalTable: "permission",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_role_permission_role_business_id_role_id",
                        columns: x => new { x.business_id, x.role_id },
                        principalSchema: "platform",
                        principalTable: "role",
                        principalColumns: new[] { "business_id", "id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_business_id_command_receipt_id",
                schema: "platform",
                table: "audit_log",
                columns: new[] { "business_id", "command_receipt_id" });

            migrationBuilder.CreateIndex(
                name: "IX_audit_log_business_id_event_at",
                schema: "platform",
                table: "audit_log",
                columns: new[] { "business_id", "event_at" });

            migrationBuilder.CreateIndex(
                name: "IX_business_membership_business_id_role_id",
                schema: "platform",
                table: "business_membership",
                columns: new[] { "business_id", "role_id" });

            migrationBuilder.CreateIndex(
                name: "IX_business_membership_business_id_user_id",
                schema: "platform",
                table: "business_membership",
                columns: new[] { "business_id", "user_id" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_business_membership_user_id",
                schema: "platform",
                table: "business_membership",
                column: "user_id");

            migrationBuilder.CreateIndex(
                name: "ux_command_receipt_idempotency",
                schema: "platform",
                table: "command_receipt",
                columns: new[] { "business_id", "operation_key", "idempotency_key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_permission_feature_key_action",
                schema: "platform",
                table: "permission",
                columns: new[] { "feature_key", "action" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_role_business_id_name",
                schema: "platform",
                table: "role",
                columns: new[] { "business_id", "name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_role_permission_permission_id",
                schema: "platform",
                table: "role_permission",
                column: "permission_id");

            // ── SQL i audituar BRENDA migrimit (ADR-024 §"Një migration owner"):
            // audit_log = APPEND-ONLY për DML të zakonshëm — UPDATE/DELETE
            // (row-level) dhe TRUNCATE (statement-level) refuzohen me trigger.
            // KUFIZIM i dokumentuar: owner/superuser/schema-control mund t'i
            // çaktivizojë — NUK pretendohet tamper-proof; roli runtime i
            // aplikacionit (jo-owner, pa disable-trigger) e bën efektiv.
            migrationBuilder.Sql("""
                CREATE FUNCTION platform.audit_log_block_mutation() RETURNS trigger
                LANGUAGE plpgsql AS $$
                BEGIN
                    RAISE EXCEPTION 'audit_log eshte append-only: % refuzohet', TG_OP
                        USING ERRCODE = 'raise_exception';
                END;
                $$;

                CREATE TRIGGER trg_audit_log_no_update
                    BEFORE UPDATE ON platform.audit_log
                    FOR EACH ROW EXECUTE FUNCTION platform.audit_log_block_mutation();

                CREATE TRIGGER trg_audit_log_no_delete
                    BEFORE DELETE ON platform.audit_log
                    FOR EACH ROW EXECUTE FUNCTION platform.audit_log_block_mutation();

                CREATE TRIGGER trg_audit_log_no_truncate
                    BEFORE TRUNCATE ON platform.audit_log
                    FOR EACH STATEMENT EXECUTE FUNCTION platform.audit_log_block_mutation();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Heqja e triggers/funksionit PARA tabelave (rend i sigurt).
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_audit_log_no_truncate ON platform.audit_log;
                DROP TRIGGER IF EXISTS trg_audit_log_no_delete ON platform.audit_log;
                DROP TRIGGER IF EXISTS trg_audit_log_no_update ON platform.audit_log;
                DROP FUNCTION IF EXISTS platform.audit_log_block_mutation();
                """);

            migrationBuilder.DropTable(
                name: "audit_log",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "business_membership",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "role_permission",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "command_receipt",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "app_user",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "permission",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "role",
                schema: "platform");

            migrationBuilder.DropTable(
                name: "business",
                schema: "platform");
        }
    }
}
