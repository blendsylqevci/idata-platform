using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Npgsql;
using Xunit;

namespace Idata.Platform.IntegrationTests;

/// <summary>
/// Mbrojtjet e audit-it (§10.E + §8A) me DY role të ndara SINTETIKE:
/// (1) migration/admin (`idata`, vetëm schema setup); (2) runtime i
/// aplikacionit — jo-owner, jo-superuser, pa CREATE/ALTER/DROP, pa
/// disable-trigger, vetëm DML minimal. Komandat reale ekzekutohen me rolin
/// runtime. Append-only vlen për DML të zakonshëm; owner/superuser mund t'i
/// çaktivizojë — NUK tamper-proof; provisioning production i rolit = OPEN.
/// Provohet edhe rollback-i i transaction-it kur dështon shkrimi i auditit
/// OSE i receipt-it (503, zero efekt).
/// </summary>
[Collection(nameof(MigratorEnvCollection))]
public sealed class AuditProtectionTests
{
    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return await command.ExecuteScalarAsync();
    }

    private static string ConnectionString(string databaseName, string username)
        => Idata.Platform.Persistence.PlatformDatabaseConnection.Parse(
            Environment.GetEnvironmentVariable("PGHOST") ?? "localhost",
            Environment.GetEnvironmentVariable("PGPORT") ?? "5432",
            username,
            databaseName).ToConnectionString();

    private static async Task CreateRuntimeRoleAsync(NpgsqlConnection adminConnection, string roleName)
    {
        await ExecuteAsync(adminConnection, $"""
            CREATE ROLE "{roleName}" LOGIN;
            GRANT CONNECT ON DATABASE "{await SyntheticDatabaseSafety.CurrentDatabaseAsync(adminConnection)}" TO "{roleName}";
            GRANT USAGE ON SCHEMA platform TO "{roleName}";
            GRANT SELECT ON platform.business, platform.app_user, platform.role,
                platform.permission, platform.role_permission TO "{roleName}";
            GRANT SELECT, UPDATE ON platform.business_membership TO "{roleName}";
            GRANT SELECT, INSERT ON platform.audit_log TO "{roleName}";
            GRANT SELECT, INSERT ON platform.command_receipt TO "{roleName}";
            """);
    }

    private static async Task DropRuntimeRoleAsync(string roleName)
    {
        await using var maintenance = new NpgsqlConnection(PlatformTestDatabase.MaintenanceConnectionString());
        await maintenance.OpenAsync();
        await SyntheticDatabaseSafety.AssertMaintenanceAsync(maintenance);
        await ExecuteAsync(maintenance, $"DROP ROLE IF EXISTS \"{roleName}\"");
    }

    private static HttpRequestMessage RoleUpdateRequest(string idempotencyKey, Guid roleId, long expectedVersion = 0)
    {
        var request = new HttpRequestMessage(
            HttpMethod.Put,
            new Uri($"/api/platform/memberships/{AccessControlSeed.MembershipC:D}/role", UriKind.Relative))
        {
            Content = JsonContent.Create(new { roleId, expectedVersion }),
        };
        request.Headers.Add("X-Test-UserId", AccessControlSeed.UserA.ToString("D"));
        request.Headers.Add("X-Test-BusinessId", AccessControlSeed.BusinessA.ToString("D"));
        request.Headers.Add("Idempotency-Key", idempotencyKey);
        return request;
    }

    private static async Task<(long Audits, long Receipts)> CountersAsync(NpgsqlConnection connection)
        => ((long)(await ScalarAsync(connection, "SELECT COUNT(*) FROM platform.audit_log"))!,
            (long)(await ScalarAsync(connection, "SELECT COUNT(*) FROM platform.command_receipt"))!);

    [Fact]
    public async Task Roli_runtime_i_kufizuar_audit_append_only_dhe_rollback_ne_deshtim_shkrimi()
    {
        await using var db = await PlatformTestDatabase.CreateAsync("auditprotect");
        var roleName = $"{db.DatabaseName}_app";
        if (roleName.Length > 63)
        {
            roleName = roleName[..63];
        }

        var previousDb = Environment.GetEnvironmentVariable("PLATFORM_PGDATABASE");
        var previousUser = Environment.GetEnvironmentVariable("PGUSER");
        Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", db.DatabaseName);
        try
        {
            await using var factory = await AccessControlSeed.MigrateSeedAndCreateFactoryAsync(db.DatabaseName);

            await using var admin = new NpgsqlConnection(ConnectionString(db.DatabaseName, "idata"));
            await admin.OpenAsync(TestContext.Current.CancellationToken);
            await SyntheticDatabaseSafety.AssertSyntheticTargetAsync(admin, db.DatabaseName);
            await CreateRuntimeRoleAsync(admin, roleName);
            try
            {
                Assert.Equal(false, await ScalarAsync(admin,
                    $"SELECT rolsuper OR rolcreatedb OR rolcreaterole FROM pg_roles WHERE rolname = '{roleName}'"));
                Assert.Equal(0L, await ScalarAsync(admin,
                    $"SELECT COUNT(*) FROM pg_tables WHERE schemaname='platform' AND tableowner = '{roleName}'"));

                await using var runtime = new NpgsqlConnection(ConnectionString(db.DatabaseName, roleName));
                await runtime.OpenAsync(TestContext.Current.CancellationToken);
                await SyntheticDatabaseSafety.AssertSyntheticTargetAsync(runtime, db.DatabaseName);

                var createDenied = await Assert.ThrowsAsync<PostgresException>(() =>
                    ExecuteAsync(runtime, "CREATE TABLE platform.tabele_e_ndaluar (id int)"));
                Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, createDenied.SqlState);
                var disableDenied = await Assert.ThrowsAsync<PostgresException>(() =>
                    ExecuteAsync(runtime, "ALTER TABLE platform.audit_log DISABLE TRIGGER trg_audit_log_no_update"));
                Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, disableDenied.SqlState);

                // Komanda REALE me rolin runtime → sukses.
                await RunCommandAsync(roleName, previousUser, "key-runtime-role", AccessControlSeed.RoleA2,
                    HttpStatusCode.OK);

                // Audit i lidhur me receipt + correlation server-generated; redaktim.
                var auditJson = (string?)await ScalarAsync(admin, """
                    SELECT json_build_object(
                        'business_id', business_id, 'user_id', user_id, 'entity', entity,
                        'action', action, 'before_state', before_state,
                        'entity_id', entity_id, 'after_state', after_state,
                        'event_at_present', event_at IS NOT NULL,
                        'command_receipt_id', command_receipt_id,
                        'correlation_null', correlation_id IS NULL,
                        'receipt_exists', EXISTS(SELECT 1 FROM platform.command_receipt r WHERE r.id = audit_log.command_receipt_id AND r.business_id = audit_log.business_id)
                    )::text FROM platform.audit_log LIMIT 1
                    """);
                Assert.NotNull(auditJson);
                using (var audit = JsonDocument.Parse(auditJson))
                {
                    Assert.Equal(AccessControlSeed.BusinessA.ToString("D"), audit.RootElement.GetProperty("business_id").GetString());
                    Assert.Equal(AccessControlSeed.UserA.ToString("D"), audit.RootElement.GetProperty("user_id").GetString());
                    Assert.Equal("business_membership", audit.RootElement.GetProperty("entity").GetString());
                    Assert.Equal("U", audit.RootElement.GetProperty("action").GetString());
                    Assert.False(audit.RootElement.GetProperty("correlation_null").GetBoolean());
                    Assert.True(audit.RootElement.GetProperty("receipt_exists").GetBoolean());
                    // Prova DB-backed e fushave (jo vetem unit): entity_id, after_state, event_at.
                    Assert.Equal(
                        AccessControlSeed.MembershipC.ToString("D"),
                        audit.RootElement.GetProperty("entity_id").GetString());
                    Assert.True(audit.RootElement.GetProperty("event_at_present").GetBoolean());
                    var before = audit.RootElement.GetProperty("before_state");
                    Assert.Equal(["RoleId"], before.EnumerateObject().Select(p => p.Name).ToArray());
                    Assert.Equal(AccessControlSeed.RoleA, before.GetProperty("RoleId").GetGuid());
                    var after = audit.RootElement.GetProperty("after_state");
                    Assert.Equal(["RoleId"], after.EnumerateObject().Select(p => p.Name).ToArray());
                    Assert.Equal(AccessControlSeed.RoleA2, after.GetProperty("RoleId").GetGuid());
                }

                // Append-only nën rolin runtime.
                foreach (var sql in (string[])
                [
                    "UPDATE platform.audit_log SET action = 'X'",
                    "DELETE FROM platform.audit_log",
                    "TRUNCATE platform.audit_log",
                ])
                {
                    var blocked = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(runtime, sql));
                    Assert.Contains(blocked.SqlState,
                        (string[])[PostgresErrorCodes.RaiseException, PostgresErrorCodes.InsufficientPrivilege]);
                }

                // Edhe si OWNER: triggers refuzojnë UPDATE/DELETE/TRUNCATE.
                foreach (var sql in (string[])
                [
                    "UPDATE platform.audit_log SET action = 'X'",
                    "DELETE FROM platform.audit_log",
                    "TRUNCATE platform.audit_log",
                ])
                {
                    var blocked = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(admin, sql));
                    Assert.Equal(PostgresErrorCodes.RaiseException, blocked.SqlState);
                }

                // ── Audit-write failure → rollback i mutation+receipt (503, zero efekt). ──
                await ExecuteAsync(admin, $"REVOKE INSERT ON platform.audit_log FROM \"{roleName}\"");
                var beforeAuditFail = await CountersAsync(admin);
                await RunCommandAsync(roleName, previousUser, "key-audit-fail", AccessControlSeed.RoleA,
                    HttpStatusCode.ServiceUnavailable, expectedVersion: 1);
                Assert.Equal(beforeAuditFail, await CountersAsync(admin));
                Assert.Equal(AccessControlSeed.RoleA2, (Guid)(await ScalarAsync(admin,
                    $"SELECT role_id FROM platform.business_membership WHERE id = '{AccessControlSeed.MembershipC:D}'"))!);
                await ExecuteAsync(admin, $"GRANT INSERT ON platform.audit_log TO \"{roleName}\"");

                // ── Receipt-write failure → rollback i mutation+audit (503, zero efekt). ──
                await ExecuteAsync(admin, $"REVOKE INSERT ON platform.command_receipt FROM \"{roleName}\"");
                var beforeReceiptFail = await CountersAsync(admin);
                await RunCommandAsync(roleName, previousUser, "key-receipt-fail", AccessControlSeed.RoleA,
                    HttpStatusCode.ServiceUnavailable, expectedVersion: 1);
                Assert.Equal(beforeReceiptFail, await CountersAsync(admin));
                Assert.Equal(AccessControlSeed.RoleA2, (Guid)(await ScalarAsync(admin,
                    $"SELECT role_id FROM platform.business_membership WHERE id = '{AccessControlSeed.MembershipC:D}'"))!);
            }
            finally
            {
                await ExecuteAsync(admin, $"DROP OWNED BY \"{roleName}\"");
            }
        }
        finally
        {
            Environment.SetEnvironmentVariable("PLATFORM_PGDATABASE", previousDb);
            Environment.SetEnvironmentVariable("PGUSER", previousUser);
            await DropRuntimeRoleAsync(roleName);
        }
    }

    private static async Task RunCommandAsync(
        string roleName, string? previousUser, string key, Guid roleId, HttpStatusCode expected, long expectedVersion = 0)
    {
        Environment.SetEnvironmentVariable("PGUSER", roleName);
        try
        {
            await using var runtimeFactory = new AccessControlApiFactory();
            using var client = runtimeFactory.CreateClient();
            using var response = await client.SendAsync(RoleUpdateRequest(key, roleId, expectedVersion), TestContext.Current.CancellationToken);
            Assert.Equal(expected, response.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("PGUSER", previousUser);
        }
    }
}
