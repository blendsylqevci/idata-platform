using System.Runtime.CompilerServices;
using Npgsql;
using Xunit;

namespace Idata.Platform.IntegrationTests;

/// <summary>
/// Lidhja DB-enforced audit ↔ command_receipt + correlation (§6): composite
/// FK (business_id, command_receipt_id) → command_receipt(business_id, id)
/// refuzon çdo link inekzistent OSE cross-tenant NË VETË PostgreSQL. (Rrjedha
/// e suksesit — audit i lidhur me receipt-in ekzakt + correlation server-gen
/// — provohet te AuditProtectionTests; këtu provohet enforcement-i i DB-së.)
/// </summary>
[Collection(nameof(MigratorEnvCollection))]
public sealed class AuditCorrelationTests
{
    private static async Task WithSeededDbAsync(Func<NpgsqlConnection, string, Task> body, [CallerMemberName] string label = "")
    {
        await using var db = await PlatformTestDatabase.CreateAsync(label);
        var previous = Environment.GetEnvironmentVariable("PLATFORM_PGDATABASE");
        Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", db.DatabaseName);
        try
        {
            await using var factory = await AccessControlSeed.MigrateSeedAndCreateFactoryAsync(db.DatabaseName);
            await using var connection = new NpgsqlConnection(TenantIsolationEndpointTests.ConnectionStringFor(db.DatabaseName));
            await connection.OpenAsync();
            await SyntheticDatabaseSafety.AssertSyntheticTargetAsync(connection, db.DatabaseName);
            await body(connection, db.DatabaseName);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", previous);
        }
    }

    private const string EmptyJson = "'{}'";

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static string InsertReceipt(Guid id, Guid businessId, string key)
        => $"""
            INSERT INTO platform.command_receipt
              (id, business_id, operation_key, idempotency_key, request_fingerprint, result_status, result_payload, created_at, completed_at)
            VALUES ('{id:D}', '{businessId:D}', 'membership.role.update', '{key}', 'fp', 'completed', {EmptyJson}, now(), now())
            """;

    private static string InsertAudit(Guid auditId, Guid businessId, Guid receiptId)
        => $"""
            INSERT INTO platform.audit_log
              (id, business_id, user_id, entity, entity_id, action, before_state, after_state, command_receipt_id, correlation_id, event_at)
            VALUES ('{auditId:D}', '{businessId:D}', '{AccessControlSeed.UserA:D}', 'business_membership', '{AccessControlSeed.MembershipC:D}', 'U', {EmptyJson}, {EmptyJson}, '{receiptId:D}', gen_random_uuid(), now())
            """;

    [Fact]
    public async Task Audit_me_receipt_inekzistent_refuzohet_nga_composite_FK()
        => await WithSeededDbAsync(async (connection, _) =>
        {
            var violation = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection,
                InsertAudit(AccessControlSeed.NonexistentId, AccessControlSeed.BusinessA, AccessControlSeed.NonexistentId)));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, violation.SqlState);
        });

    [Fact]
    public async Task Audit_me_receipt_cross_tenant_refuzohet_nga_composite_FK()
        => await WithSeededDbAsync(async (connection, _) =>
        {
            // Receipt ekziston, por për business B.
            var receiptId = Guid.Parse("dbadbadb-dbad-4bad-8bad-dbadbadbdbad");
            await ExecuteAsync(connection, InsertReceipt(receiptId, AccessControlSeed.BusinessB, "cross-key"));

            // Audit për business A që i referohet receipt-it të B → FK violation
            // (composite key (business_id, command_receipt_id) nuk përputhet).
            var violation = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(connection,
                InsertAudit(Guid.Parse("dacedace-dace-4ace-8ace-dacedacedace"), AccessControlSeed.BusinessA, receiptId)));
            Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, violation.SqlState);

            // Audit për business B me të njëjtin receipt → kalon (link i vlefshëm).
            await ExecuteAsync(connection, InsertAudit(
                Guid.Parse("daffdaff-daff-4aff-8aff-daffdaffdaff"), AccessControlSeed.BusinessB, receiptId));
        });
}
